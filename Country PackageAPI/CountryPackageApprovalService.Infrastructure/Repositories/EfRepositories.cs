using CountryPackageApprovalService.Application.Interfaces;
using CountryPackageApprovalService.Domain;
using CountryPackageApprovalService.Domain.Exceptions;
using CountryPackageApprovalService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CountryPackageApprovalService.Infrastructure.Repositories;

public sealed class CountryPackageRepository : ICountryPackageRepository
{
    private readonly AppDbContext _db;
    public CountryPackageRepository(AppDbContext db) => _db = db;

    public Task<CountryPackage?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.CountryPackages
            .Include(p => p.Steps)
            .ThenInclude(s => s.Documents)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task AddAsync(CountryPackage package, CancellationToken ct) =>
        await _db.CountryPackages.AddAsync(package, ct);
}

public sealed class RoadmapTemplateRepository : IRoadmapTemplateRepository
{
    private readonly AppDbContext _db;
    public RoadmapTemplateRepository(AppDbContext db) => _db = db;

    public Task<RoadmapTemplate?> GetActiveAsync(CancellationToken ct) =>
        _db.RoadmapTemplates
            .Include(t => t.Steps)
            .Where(t => t.IsActive)
            .FirstOrDefaultAsync(ct);
}

public sealed class CountryRepository : ICountryRepository
{
    private readonly AppDbContext _db;
    public CountryRepository(AppDbContext db) => _db = db;

    public Task<bool> ExistsAsync(string countryCode, CancellationToken ct) =>
        _db.Countries.AnyAsync(c => c.Code == countryCode, ct);
}

public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    public UserRepository(AppDbContext db) => _db = db;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct) =>
        await _db.Users
            .Include(u => u.Roles)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(ct);
}

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly AppDbContext _db;
    public AuditLogRepository(AppDbContext db) => _db = db;

    public void Add(AuditLogEntry entry) => _db.AuditLogEntries.Add(entry);

    public async Task<IReadOnlyList<AuditLogEntry>> GetForPackageAsync(Guid packageId, CancellationToken ct) =>
        await _db.AuditLogEntries
            .Where(a => a.PackageId == packageId)
            .OrderBy(a => a.TimestampUtc)
            .ToListAsync(ct);
}

/// <summary>
/// Commits every change tracked across this scope's repositories in a single <c>SaveChanges</c> call - the
/// transactional boundary for one use case (docs/ARCHITECTURE.md §3.3): the state change, the audit log
/// entry, and any outbox row it raised either all land or none do. Translates EF Core's optimistic-concurrency
/// exception into the Domain-level <see cref="ConcurrencyConflictException"/> so nothing above Infrastructure
/// needs to know which provider (or that a provider at all) is behind <see cref="IUnitOfWork"/>.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;
    public UnitOfWork(AppDbContext db) => _db = db;

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException(
                "The package or step was modified by another request since it was loaded. Reload and retry.");
        }
    }
}
