using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Country_PackageAPI.Auth;

/// <summary>
/// Stand-in for Microsoft Entra ID in this exercise: identity is established from an
/// <c>X-User-Id</c> header carrying a seeded <see cref="Domain.User"/>'s GUID, instead of validating a real
/// OIDC bearer token. This is deliberately the *only* place in the solution that knows about that header -
/// everywhere else deals in <c>ClaimsPrincipal</c>/<c>Guid</c> the same way it would with real Entra ID tokens.
/// Swapping to Entra ID in the Azure target architecture (docs/ARCHITECTURE.md §6.3) means replacing the
/// scheme registration in Program.cs with <c>AddMicrosoftIdentityWebApi</c> - nothing downstream of
/// authentication (authorization handlers, controllers, Application/Domain) changes, because none of it reads
/// this header directly; it only ever reads <see cref="ClaimTypes.NameIdentifier"/> off the principal.
///
/// This handler ONLY establishes identity. It never queries roles/clearance - that is always re-read from
/// <c>UserCountryRole</c> by the resource-based authorization handlers and, defensively, by the Application
/// layer itself (docs/ARCHITECTURE.md §4.1).
/// </summary>
public sealed class DevHeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DevHeader";
    public const string HeaderName = "X-User-Id";

    public DevHeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var headerValues))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                $"Missing required '{HeaderName}' header. See GET /api/v1/test-users for seeded user ids to use during testing."));
        }

        var rawUserId = headerValues.ToString();
        if (!Guid.TryParse(rawUserId, out var userId))
        {
            return Task.FromResult(AuthenticateResult.Fail($"'{HeaderName}' header value '{rawUserId}' is not a valid GUID."));
        }

        // Identity only - existence of the user, and every permission decision, is left to downstream
        // authorization (see the type-level remarks above).
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new
        {
            status = 401,
            title = "Unauthorized",
            detail = $"Provide a valid '{HeaderName}' header with a seeded user id. See GET /api/v1/test-users."
        });
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(new
        {
            status = 403,
            title = "Forbidden",
            detail = "The current user does not hold the role/country/organizational-level clearance required for this action."
        });
    }
}
