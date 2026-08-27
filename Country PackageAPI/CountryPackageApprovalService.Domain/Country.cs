namespace CountryPackageApprovalService.Domain;

/// <summary>A country code the roadmap/RBAC model scopes users and packages to. Fictional data only.</summary>
public class Country
{
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Region { get; private set; } = default!;

    private Country() { } // EF Core

    public Country(string code, string name, string region)
    {
        Code = code;
        Name = name;
        Region = region;
    }
}
