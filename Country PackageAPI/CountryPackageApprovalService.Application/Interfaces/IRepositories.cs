using CountryPackageApprovalService.Domain;

namespace CountryPackageApprovalService.Application.Interfaces;

/// <summary>
/// Persistence seams. Application depends only on these interfaces, never on EF Core - the concrete
/// implementations (backed by the InMemory provider in this exercise, Azure SQL in production) live in
/// Infrastructure. This is what keeps "swap the database" a one-project change (docs/ARCHITECTURE.md §9).
/// </summary>
public interface ICountryPackageRepository
{
    /// <summary>Loads a package with its steps and each step's document versions. Null if not found.</summary>
    Task<CountryPackage?> GetByIdAsync(Guid id, CancellationToken ct);

    Task AddAsync(CountryPackage package, CancellationToken ct);
}

public interface IRoadmapTemplateRepository
{
    Task<RoadmapTemplate?> GetActiveAsync(CancellationToken ct);
}

public interface ICountryRepository
{
    Task<bool> ExistsAsync(string countryCode, CancellationToken ct);
}

public interface IUserRepository
{
    /// <summary>Loads a user with their country/role grants. Null if not found.</summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Every seeded user with their country/role grants. Backs the Api layer's test-users convenience
    /// endpoint only (there is no legitimate production use case for "list every user") - see
    /// Controllers/TestUsersController.cs.</summary>
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct);
}

public interface IAuditLogRepository
{
    void Add(AuditLogEntry entry);
    Task<IReadOnlyList<AuditLogEntry>> GetForPackageAsync(Guid packageId, CancellationToken ct);
}

/// <summary>Commits the current unit of work in one transaction. Implementations translate the provider's
/// concurrency exception into <see cref="Domain.Exceptions.ConcurrencyConflictException"/> so callers above
/// Infrastructure never need to know which database is behind this interface.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
