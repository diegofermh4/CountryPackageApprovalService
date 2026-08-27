using System.Security.Claims;

namespace Country_PackageAPI.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The authenticated caller's seeded user id, or null if unauthenticated / the claim is missing
    /// or malformed. Controllers use this instead of touching <see cref="DevHeaderAuthenticationHandler.HeaderName"/>
    /// directly, so nothing outside Auth/ knows how identity was established.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    /// <summary>Same as <see cref="GetUserId"/> but throws if identity is missing - safe to call from any
    /// action guarded by <c>[Authorize]</c>, where the middleware pipeline guarantees a valid claim exists.</summary>
    public static Guid RequireUserId(this ClaimsPrincipal principal) =>
        principal.GetUserId() ?? throw new InvalidOperationException(
            "No authenticated user id on the current principal; this action must be protected by [Authorize].");
}
