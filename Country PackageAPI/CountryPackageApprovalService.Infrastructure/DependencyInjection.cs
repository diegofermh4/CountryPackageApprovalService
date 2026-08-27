using CountryPackageApprovalService.Application.Interfaces;
using CountryPackageApprovalService.Application.Services;
using CountryPackageApprovalService.Infrastructure.DocumentStore;
using CountryPackageApprovalService.Infrastructure.Idempotency;
using CountryPackageApprovalService.Infrastructure.Outbox;
using CountryPackageApprovalService.Infrastructure.Persistence;
using CountryPackageApprovalService.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CountryPackageApprovalService.Infrastructure;

/// <summary>
/// Composition root for this project: registers the EF Core InMemory <see cref="AppDbContext"/>, every
/// repository/service backing an Application-layer interface, and the outbox dispatcher. The Api project
/// calls <see cref="AddInfrastructure"/> once at startup and otherwise never references EF Core directly
/// (docs/ARCHITECTURE.md §3.1) - swapping the InMemory registration below for SqlServer/Npgsql is the
/// entire migration to the Azure target architecture.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Fixed InMemory database name: every <see cref="AppDbContext"/> instance in the process
    /// (request-scoped, or the outbox dispatcher's own background scope) must share one in-memory store, or
    /// writes made under one scope become invisible to another.</summary>
    private const string InMemoryDatabaseName = "CountryPackageApprovalServiceDb";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(InMemoryDatabaseName));

        services.AddScoped<ICountryPackageRepository, CountryPackageRepository>();
        services.AddScoped<IRoadmapTemplateRepository, RoadmapTemplateRepository>();
        services.AddScoped<ICountryRepository, CountryRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();

        // Singleton: the idempotency cache must survive across requests within the process (see the type's own docs).
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        services.AddScoped<IDocumentStore, LocalDiskDocumentStore>();

        services.AddScoped<IApprovalWorkflowService, ApprovalWorkflowService>();

        services.AddHostedService<OutboxDispatcherHostedService>();

        return services;
    }

    /// <summary>Applies fictional seed data (docs/ARCHITECTURE.md - "Use fictional data only"). Called once at
    /// startup from Program.cs, deliberately separate from registration so it runs against a fully-built
    /// <see cref="IServiceProvider"/> rather than mid-registration.</summary>
    public static void SeedDatabase(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        SeedData.EnsureSeeded(db);
    }
}
