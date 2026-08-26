using CountryPackageApprovalService.Domain;

namespace CountryPackageApprovalService.Tests.Domain;

/// <summary>Pure unit tests for the RBAC core check (docs/ARCHITECTURE.md §4.1) - role is always scoped to a
/// country, and country-level clearance never implies regional clearance (or vice versa) unless granted at
/// <see cref="OrgLevel.Both"/>.</summary>
public class UserRbacTests
{
    private static User CreateUser() => new(Guid.NewGuid(), "dev|x", "Test User", "test@example.test");

    [Fact]
    public void HasClearance_respects_org_level_scoping()
    {
        var user = CreateUser();
        user.GrantRole("RUR", UserRole.CountryReviewer, OrgLevel.Country);

        Assert.True(user.HasClearance("RUR", UserRole.CountryReviewer, OrgLevel.Country));
        Assert.False(user.HasClearance("RUR", UserRole.CountryReviewer, OrgLevel.Regional));
        Assert.False(user.HasClearance("SOL", UserRole.CountryReviewer, OrgLevel.Country));
        Assert.False(user.HasClearance("RUR", UserRole.CountryEditor, OrgLevel.Country));
    }

    [Fact]
    public void HasClearance_with_Both_covers_every_org_level()
    {
        var user = CreateUser();
        user.GrantRole("RUR", UserRole.CountryReviewer, OrgLevel.Both);

        Assert.True(user.HasClearance("RUR", UserRole.CountryReviewer, OrgLevel.Country));
        Assert.True(user.HasClearance("RUR", UserRole.CountryReviewer, OrgLevel.Regional));
    }

    [Fact]
    public void HasAnyClearance_ignores_org_level_but_still_scopes_by_country_and_role()
    {
        var user = CreateUser();
        user.GrantRole("RUR", UserRole.CountryEditor, OrgLevel.Regional);

        Assert.True(user.HasAnyClearance("RUR", UserRole.CountryEditor));
        Assert.False(user.HasAnyClearance("RUR", UserRole.CountryReviewer));
        Assert.False(user.HasAnyClearance("SOL", UserRole.CountryEditor));
    }

    [Fact]
    public void Country_code_comparison_is_case_insensitive()
    {
        var user = CreateUser();
        user.GrantRole("RUR", UserRole.CountryEditor, OrgLevel.Both);

        Assert.True(user.HasAnyClearance("rur", UserRole.CountryEditor));
    }
}
