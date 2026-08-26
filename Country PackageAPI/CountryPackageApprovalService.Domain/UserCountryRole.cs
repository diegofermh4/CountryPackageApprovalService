namespace CountryPackageApprovalService.Domain;

/// <summary>
/// The authorization store (docs/ARCHITECTURE.md §4.1): one row per (user, country, role), optionally
/// scoped further to a single org level. Role is never global - it is always granted for a country, and
/// clearance to review at Country level does not imply clearance at Regional level (and vice versa) unless
/// <see cref="OrgLevel"/> is <see cref="Domain.OrgLevel.Both"/>.
/// </summary>
public class UserCountryRole
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string CountryCode { get; private set; } = default!;
    public UserRole Role { get; private set; }
    public OrgLevel OrgLevel { get; private set; }

    private UserCountryRole() { } // EF Core

    internal UserCountryRole(Guid userId, string countryCode, UserRole role, OrgLevel orgLevel)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        CountryCode = countryCode;
        Role = role;
        OrgLevel = orgLevel;
    }

    public bool CoversOrgLevel(OrgLevel required) =>
        OrgLevel == OrgLevel.Both || OrgLevel == required;
}
