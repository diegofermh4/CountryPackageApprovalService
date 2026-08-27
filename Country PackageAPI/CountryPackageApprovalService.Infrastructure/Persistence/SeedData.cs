using CountryPackageApprovalService.Domain;

namespace CountryPackageApprovalService.Infrastructure.Persistence;

/// <summary>
/// Deterministic fictional seed data (per the brief: "Use fictional data only"), applied once at startup -
/// the InMemory provider's store lives only for the process's lifetime, so there is nothing to seed
/// idempotently across restarts. GUIDs are fixed literals so the values in README.md / the .http sample
/// requests keep working run after run. Covers every RBAC shape the workflow cares about: a Country-level-only
/// reviewer, a Regional-level-only reviewer, a reviewer cleared at Both levels, and a user with zero grants
/// (to exercise the 403 path).
/// </summary>
public static class SeedData
{
    public static readonly Guid EditorRuritaniaId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    public static readonly Guid ReviewerRuritaniaCountryId = Guid.Parse("11111111-0000-0000-0000-000000000002");
    public static readonly Guid ReviewerRuritaniaRegionalId = Guid.Parse("11111111-0000-0000-0000-000000000003");
    public static readonly Guid EditorSolantisId = Guid.Parse("11111111-0000-0000-0000-000000000004");
    public static readonly Guid ReviewerSolantisBothId = Guid.Parse("11111111-0000-0000-0000-000000000005");
    public static readonly Guid NoGrantsUserId = Guid.Parse("11111111-0000-0000-0000-000000000006");

    public static void EnsureSeeded(AppDbContext db)
    {
        if (db.Countries.Any()) return; // defensive guard; not expected to fire given the InMemory provider's per-run lifetime

        db.Countries.AddRange(
            new Country("RUR", "Ruritania", "Eastlands"),
            new Country("SOL", "Solantis", "Eastlands"),
            new Country("VEG", "Vega", "Westlands"));

        var editorRuritania = new User(EditorRuritaniaId, "dev|editor.ruritania", "Ana Petrova", "ana.petrova@example.test");
        editorRuritania.GrantRole("RUR", UserRole.CountryEditor, OrgLevel.Both);

        var reviewerRuritaniaCountry = new User(ReviewerRuritaniaCountryId, "dev|reviewer.ruritania.country", "Marcus Ionescu", "marcus.ionescu@example.test");
        reviewerRuritaniaCountry.GrantRole("RUR", UserRole.CountryReviewer, OrgLevel.Country);

        var reviewerRuritaniaRegional = new User(ReviewerRuritaniaRegionalId, "dev|reviewer.ruritania.regional", "Elena Kova", "elena.kova@example.test");
        reviewerRuritaniaRegional.GrantRole("RUR", UserRole.CountryReviewer, OrgLevel.Regional);

        var editorSolantis = new User(EditorSolantisId, "dev|editor.solantis", "Noah Bergman", "noah.bergman@example.test");
        editorSolantis.GrantRole("SOL", UserRole.CountryEditor, OrgLevel.Both);

        var reviewerSolantisBoth = new User(ReviewerSolantisBothId, "dev|reviewer.solantis.both", "Priya Shah", "priya.shah@example.test");
        reviewerSolantisBoth.GrantRole("SOL", UserRole.CountryReviewer, OrgLevel.Both);

        // No role grants anywhere - authenticated but zero clearance, for exercising the 403 path.
        var noGrantsUser = new User(NoGrantsUserId, "dev|no.grants", "Diego Reyes", "diego.reyes@example.test");

        db.Users.AddRange(editorRuritania, reviewerRuritaniaCountry, reviewerRuritaniaRegional, editorSolantis, reviewerSolantisBoth, noGrantsUser);

        db.RoadmapTemplates.Add(RoadmapTemplate.CreateDefault());

        db.SaveChanges();
    }
}
