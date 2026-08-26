using System.Text.Json;
using CountryPackageApprovalService.Application.Interfaces;
using CountryPackageApprovalService.Domain.Events;
using CountryPackageApprovalService.Infrastructure.Persistence;

namespace CountryPackageApprovalService.Infrastructure.Outbox;

/// <summary>
/// Adds an <see cref="OutboxMessage"/> to the same <see cref="AppDbContext"/> change tracker the calling unit
/// of work is about to commit, so the outbox row lands in the same <c>SaveChanges</c> transaction as the
/// domain state change that raised the event - the transactional-outbox guarantee described on
/// <see cref="IOutboxWriter"/> and in docs/ARCHITECTURE.md §3.3.
/// </summary>
public sealed class OutboxWriter : IOutboxWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    private readonly AppDbContext _db;

    public OutboxWriter(AppDbContext db) => _db = db;

    public void Enqueue(IDomainEvent domainEvent)
    {
        var eventType = domainEvent.GetType().Name;
        var payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), SerializerOptions);
        _db.OutboxMessages.Add(new OutboxMessage(eventType, payload, domainEvent.OccurredAtUtc));
    }
}
