using CountryPackageApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CountryPackageApprovalService.Infrastructure.Outbox;

/// <summary>
/// Polls for unpublished outbox rows and "publishes" them - logged in this exercise; in the Azure target
/// architecture this becomes a relay that forwards each row to Azure Service Bus, under the same
/// at-least-once/poll contract (docs/ARCHITECTURE.md §3.3, §6.2). Deliberately a separate step from the
/// write inside the original transaction: it decouples "the state change committed" from "the rest of the
/// platform was told about it", so a downstream subscriber outage never blocks an approval action. Runs in
/// its own DI scope on a timer rather than holding a scoped <see cref="AppDbContext"/> for the app's lifetime.
/// </summary>
public sealed class OutboxDispatcherHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcherHostedService> _logger;

    public OutboxDispatcherHostedService(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatcherHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Outbox dispatch cycle failed; will retry on the next poll.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }

    private async Task DispatchPendingAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = await db.OutboxMessages
            .Where(m => m.PublishedAtUtc == null)
            .OrderBy(m => m.OccurredAtUtc)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        foreach (var message in pending)
        {
            _logger.LogInformation(
                "Outbox publish: {EventType} occurred {OccurredAtUtc:o} payload={Payload}",
                message.EventType, message.OccurredAtUtc, message.PayloadJson);
            message.MarkPublished();
        }

        await db.SaveChangesAsync(ct);
    }
}
