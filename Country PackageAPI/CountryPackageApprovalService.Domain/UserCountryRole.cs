namespace CountryPackageApprovalService.Domain;

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
