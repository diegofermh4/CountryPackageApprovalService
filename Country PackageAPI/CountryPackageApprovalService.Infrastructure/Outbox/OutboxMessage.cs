namespace CountryPackageApprovalService.Infrastructure.Outbox;

/// <summary>
/// Transactional outbox row (docs/ARCHITECTURE.md §3.3 / §6.2 "Eventing"). Written in the same DB transaction
/// as the state change that raised the event; <see cref="OutboxDispatcherHostedService"/> polls for unpublished
/// rows and "publishes" them - logged here, Azure Service Bus in the Azure target architecture.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = default!;
    public string PayloadJson { get; private set; } = default!;
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }

    private OutboxMessage() { } // EF Core

    public OutboxMessage(string eventType, string payloadJson, DateTime occurredAtUtc)
    {
        Id = Guid.NewGuid();
        EventType = eventType;
        PayloadJson = payloadJson;
        OccurredAtUtc = occurredAtUtc;
    }

    public void MarkPublished() => PublishedAtUtc = DateTime.UtcNow;
}
