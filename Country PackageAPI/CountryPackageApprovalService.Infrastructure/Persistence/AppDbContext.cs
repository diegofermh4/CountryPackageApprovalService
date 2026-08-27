using CountryPackageApprovalService.Domain;
using CountryPackageApprovalService.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CountryPackageApprovalService.Infrastructure.Persistence;

/// <summary>
/// The only place in the solution that references EF Core's provider. Swap the InMemory registration in
/// DependencyInjection.cs for SqlServer/Npgsql to move to the Azure target architecture (docs/ARCHITECTURE.md
/// §9) - nothing outside this project needs to change.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Country> Countries => Set<Country>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserCountryRole> UserCountryRoles => Set<UserCountryRole>();
    public DbSet<RoadmapTemplate> RoadmapTemplates => Set<RoadmapTemplate>();
    public DbSet<RoadmapStepTemplate> RoadmapStepTemplates => Set<RoadmapStepTemplate>();
    public DbSet<CountryPackage> CountryPackages => Set<CountryPackage>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Country>(b =>
        {
            b.HasKey(c => c.Code);
        });

        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);
            b.HasMany(u => u.Roles).WithOne().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(u => u.Roles).HasField("_roles").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<UserCountryRole>(b =>
        {
            b.HasKey(r => r.Id);
        });

        modelBuilder.Entity<RoadmapTemplate>(b =>
        {
            b.HasKey(t => t.Id);
            b.HasMany(t => t.Steps).WithOne().HasForeignKey(s => s.RoadmapTemplateId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(t => t.Steps).HasField("_steps").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<RoadmapStepTemplate>(b =>
        {
            b.HasKey(s => s.Id);
        });

        modelBuilder.Entity<CountryPackage>(b =>
        {
            b.HasKey(p => p.Id);
            b.HasMany(p => p.Steps).WithOne().HasForeignKey(s => s.PackageId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(p => p.Steps).HasField("_steps").UsePropertyAccessMode(PropertyAccessMode.Field);
            b.Property(p => p.RowVersion).IsRowVersion();
            b.Ignore(p => p.Status);
            b.Ignore(p => p.DomainEvents);
        });

        modelBuilder.Entity<ApprovalStep>(b =>
        {
            b.HasKey(s => s.Id);
            b.HasMany(s => s.Documents).WithOne().HasForeignKey(d => d.StepId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(s => s.Documents).HasField("_documents").UsePropertyAccessMode(PropertyAccessMode.Field);
            b.Property(s => s.RowVersion).IsRowVersion();
            b.Ignore(s => s.RequiresDocument);
            b.Ignore(s => s.CanAcceptDocument);
            b.Ignore(s => s.DomainEvents);
        });

        modelBuilder.Entity<DocumentVersion>(b =>
        {
            b.HasKey(d => d.Id);
        });

        modelBuilder.Entity<AuditLogEntry>(b =>
        {
            b.HasKey(a => a.Id);
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(m => m.Id);
        });
    }
}
