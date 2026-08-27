using CountryPackageApprovalService.Domain.Events;

namespace CountryPackageApprovalService.Application.Interfaces;

/// <summary>Local disk in this exercise; Azure Blob Storage (versioned, immutability-policy-backed) in the
/// Azure target architecture - only the Infrastructure implementation changes (docs/ARCHITECTURE.md §6.4).</summary>
public interface IDocumentStore
{
    Task<DocumentStoreResult> SaveAsync(
        Guid packageId,
        Guid stepId,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken ct);
}

public sealed record DocumentStoreResult(string Uri, long SizeBytes, string Checksum);

/// <summary>
/// Writes a domain event to the transactional outbox in the *same* DB transaction as the state change that
/// raised it (see docs/ARCHITECTURE.md §3.3) - never publishes directly, so the event that tells the wider
/// platform "step completed" can't be lost or double-fired independently of the state change itself.
/// A background dispatcher (Infrastructure) later "publishes" queued messages - logged in this exercise,
/// Azure Service Bus in the Azure target architecture.
/// </summary>
public interface IOutboxWriter
{
    void Enqueue(IDomainEvent domainEvent);
}

/// <summary>
/// Backs the `Idempotency-Key` header on submit/decision endpoints (docs/ARCHITECTURE.md §3.3): a retried
/// request with the same key returns the original result instead of re-executing. In-process/in-memory in
/// this exercise; a distributed cache (e.g. Azure Cache for Redis) in a multi-instance production deployment.
/// </summary>
public interface IIdempotencyStore
{
    bool TryGetResponse<TResponse>(string idempotencyKey, out TResponse? response);
    void StoreResponse<TResponse>(string idempotencyKey, TResponse response);
}
