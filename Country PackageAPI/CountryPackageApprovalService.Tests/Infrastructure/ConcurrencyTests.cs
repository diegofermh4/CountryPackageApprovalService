using CountryPackageApprovalService.Domain;
using CountryPackageApprovalService.Domain.Exceptions;
using CountryPackageApprovalService.Infrastructure.Persistence;
using CountryPackageApprovalService.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CountryPackageApprovalService.Tests.Infrastructure;

/// <summary>
/// Exercises optimistic concurrency directly against two independent <see cref="AppDbContext"/> instances
/// sharing one InMemory database - the same shape of race an HTTP client hits by loading a package, then
/// PATCHing a now-stale copy after another request already changed it. This is deliberately an
/// Infrastructure-level test rather than an HTTP one: forcing a genuine race through two concurrent requests
/// against an in-process test host is flaky by nature, while driving two DbContexts directly is deterministic
/// and tests exactly the translation this layer is responsible for (docs/ARCHITECTURE.md §3.3): EF Core's
/// <see cref="DbUpdateConcurrencyException"/> becoming the Domain's own <see cref="ConcurrencyConflictException"/>.
/// </summary>
public class ConcurrencyTests
{
    private static AppDbContext CreateContext(string dbName) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);

    [Fact]
    public async Task Two_stale_writers_to_the_same_step_the_second_save_throws_ConcurrencyConflictException()
    {
        var dbName = $"ConcurrencyTest-{Guid.NewGuid()}";

        Guid packageId;
        await using (var seedDb = CreateContext(dbName))
        {
            var template = RoadmapTemplate.CreateDefault();
            seedDb.RoadmapTemplates.Add(template);
            var package = CountryPackage.CreateFromTemplate("RUR", template, "Concurrency Test", Guid.NewGuid());
            seedDb.CountryPackages.Add(package);
            await seedDb.SaveChangesAsync();
            packageId = package.Id;
        }

        // Two independent contexts loading the same row, simulating two concurrent requests each holding
        // their own snapshot of the step's RowVersion.
        await using var dbA = CreateContext(dbName);
        await using var dbB = CreateContext(dbName);

        var packageA = await new CountryPackageRepository(dbA).GetByIdAsync(packageId, CancellationToken.None);
        var packageB = await new CountryPackageRepository(dbB).GetByIdAsync(packageId, CancellationToken.None);
        Assert.NotNull(packageA);
        Assert.NotNull(packageB);

        // Both attach a document (harmless additive inserts) then submit - Submit is the scalar mutation on
        // ApprovalStep itself that actually engages the RowVersion concurrency token.
        packageA!.GetStep(1).AttachDocument(Guid.NewGuid(), "a.pdf", "file://a", "application/pdf", 10, "chkA");
        packageA.GetStep(1).Submit(Guid.NewGuid(), Guid.NewGuid());

        packageB!.GetStep(1).AttachDocument(Guid.NewGuid(), "b.pdf", "file://b", "application/pdf", 10, "chkB");
        packageB.GetStep(1).Submit(Guid.NewGuid(), Guid.NewGuid());

        await new UnitOfWork(dbA).SaveChangesAsync(CancellationToken.None); // wins the race, bumps RowVersion

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => new UnitOfWork(dbB).SaveChangesAsync(CancellationToken.None)); // stale RowVersion -> conflict
    }
}
