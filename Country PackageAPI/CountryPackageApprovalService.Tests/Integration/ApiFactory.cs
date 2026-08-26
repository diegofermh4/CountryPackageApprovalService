using CountryPackageApprovalService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CountryPackageApprovalService.Tests.Integration;

/// <summary>
/// Boots the real Api pipeline end to end - authentication, resource-based authorization, the exception
/// middleware, controllers, and Swagger generation - with each factory instance pointed at its own isolated
/// InMemory database. The fixed database name in <c>DependencyInjection.AddInfrastructure</c> is right for the
/// running app (one process, one store) but wrong for parallel test classes, which must never see each
/// other's seeded users or packages; <see cref="ConfigureTestServices"/> below swaps it for a unique one per
/// factory instance while everything else about the real composition root stays exactly as Program.cs wires it.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null) services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase($"ApiFactoryTests-{Guid.NewGuid()}"));
        });
    }
}
