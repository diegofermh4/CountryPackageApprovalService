using System.Reflection;
using Country_PackageAPI.Auth;
using Country_PackageAPI.Authorization;
using Country_PackageAPI.Middleware;
using Country_PackageAPI.Swagger;
using CountryPackageApprovalService.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---- Services -----------------------------------------------------------------------------------------

builder.Services.AddControllers();

// Dev-header authentication stands in for Entra ID in this exercise - see DevHeaderAuthenticationHandler
// for exactly what would change to swap it for the real thing (docs/ARCHITECTURE.md §6.3).
builder.Services
    .AddAuthentication(DevHeaderAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DevHeaderAuthenticationHandler>(DevHeaderAuthenticationHandler.SchemeName, options => { });

builder.Services.AddAuthorization();
// Scoped (not singleton): it depends on IUserRepository, which is itself scoped to an EF Core DbContext per request.
builder.Services.AddScoped<IAuthorizationHandler, CountryRoleAuthorizationHandler>();

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Country Package Approval Service API",
        Version = "v1",
        Description =
            "Manages a country's approval roadmap through its fixed Decision/Information steps, with RBAC " +
            "scoped by role, country code, and organizational level. See README.md for the full walkthrough, " +
            "and GET /api/v1/test-users for seeded identities to use with the X-User-Id header below."
    });

    // Header-based "auth" so Swagger UI's Authorize button lets a tester paste a seeded user id once and have
    // it sent with every request, mirroring how a real bearer-token scheme would be wired up here.
    options.AddSecurityDefinition(DevHeaderAuthenticationHandler.SchemeName, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = DevHeaderAuthenticationHandler.HeaderName,
        Description = "Development-only header-based identity. Paste a seeded user id from GET /api/v1/test-users."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = DevHeaderAuthenticationHandler.SchemeName }
            },
            Array.Empty<string>()
        }
    });

    options.OperationFilter<IdempotencyKeyOperationFilter>();

    // Surfaces the /// <summary> comments from Api and (where a DTO/enum from those projects appears in a
    // request/response body) Application and Domain, so Swagger UI reads like the code, not a blank schema.
    foreach (var xmlFile in new[]
             {
                 $"{Assembly.GetExecutingAssembly().GetName().Name}.xml",
                 "CountryPackageApprovalService.Application.xml",
                 "CountryPackageApprovalService.Domain.xml"
             })
    {
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath)) options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// ---- Pipeline -------------------------------------------------------------------------------------------

// First, so it also catches anything thrown by later middleware/authentication/authorization, not just controllers.
app.UseDomainExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// The InMemory provider's store lives only for this process's lifetime, so fictional seed data is (re)applied
// on every startup rather than via a one-time migration (docs/ARCHITECTURE.md - "Use fictional data only").
app.Services.SeedDatabase();

app.Run();

/// <summary>Partial Program class so WebApplicationFactory&lt;Program&gt; can bootstrap this app in integration tests.</summary>
public partial class Program
{
}
