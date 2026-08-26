namespace CountryPackageApprovalService.Domain;

/// <summary>
/// A platform user. In the take-home, authentication is a development header that resolves directly to a
/// seeded User's <see cref="Id"/>; in the Azure target architecture, <see cref="ExternalId"/> holds the
/// Microsoft Entra ID object id (oid claim) and authentication is a real OIDC token (see docs/ARCHITECTURE.md §6.3).
/// Either way, Entra ID (or the dev header) only ever establishes identity - authorization is always
/// re-read from <see cref="UserCountryRole"/> below.
/// </summary>
public class User
{
    public Guid Id { get; private set; }
    public string ExternalId { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public string Email { get; private set; } = default!;

    private readonly List<UserCountryRole> _roles = new();
    public IReadOnlyList<UserCountryRole> Roles => _roles.AsReadOnly();

    private User() { } // EF Core

    public User(Guid id, string externalId, string displayName, string email)
    {
        Id = id;
        ExternalId = externalId;
        DisplayName = displayName;
        Email = email;
    }

    public UserCountryRole GrantRole(string countryCode, UserRole role, OrgLevel orgLevel)
    {
        var grant = new UserCountryRole(Id, countryCode, role, orgLevel);
        _roles.Add(grant);
        return grant;
    }

    /// <summary>True if this user currently holds <paramref name="role"/> for <paramref name="countryCode"/>
    /// at (or covering) <paramref name="orgLevel"/>. The core RBAC check - see docs/ARCHITECTURE.md §4.1.</summary>
    public bool HasClearance(string countryCode, UserRole role, OrgLevel orgLevel) =>
        _roles.Any(r =>
            string.Equals(r.CountryCode, countryCode, StringComparison.OrdinalIgnoreCase) &&
            r.Role == role &&
            r.CoversOrgLevel(orgLevel));

    /// <summary>True if this user holds <paramref name="role"/> for <paramref name="countryCode"/> at any org
    /// level. Used where the action itself is not org-level-scoped (e.g. an Editor submitting a step) - see
    /// <see cref="HasClearance"/> for the org-level-scoped check used for Reviewer decisions.</summary>
    public bool HasAnyClearance(string countryCode, UserRole role) =>
        _roles.Any(r =>
            string.Equals(r.CountryCode, countryCode, StringComparison.OrdinalIgnoreCase) &&
            r.Role == role);
}
