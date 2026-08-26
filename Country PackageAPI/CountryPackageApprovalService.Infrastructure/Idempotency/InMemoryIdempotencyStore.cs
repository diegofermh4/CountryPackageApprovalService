using System.Collections.Concurrent;
using CountryPackageApprovalService.Application.Interfaces;

namespace CountryPackageApprovalService.Infrastructure.Idempotency;

/// <summary>
/// In-process cache for this exercise; Azure Cache for Redis in a multi-instance production deployment
/// (docs/ARCHITECTURE.md §3.3) - only this class would change. Registered as a singleton so the cache
/// survives across requests within the process; keyed by the caller-supplied <c>Idempotency-Key</c> header,
/// scoped per operation+package+step by the caller (see <c>ApprovalWorkflowService.BuildIdempotencyKey</c>)
/// so the same raw key on two different steps never collides.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, object?> _cache = new();

    public bool TryGetResponse<TResponse>(string idempotencyKey, out TResponse? response)
    {
        if (_cache.TryGetValue(idempotencyKey, out var cached) && cached is TResponse typed)
        {
            response = typed;
            return true;
        }

        response = default;
        return false;
    }

    public void StoreResponse<TResponse>(string idempotencyKey, TResponse response) =>
        _cache[idempotencyKey] = response;
}
