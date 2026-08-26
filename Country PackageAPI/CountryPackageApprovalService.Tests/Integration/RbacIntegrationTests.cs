using System.Net;
using System.Net.Http.Json;
using CountryPackageApprovalService.Application.Dtos;
using CountryPackageApprovalService.Domain;
using CountryPackageApprovalService.Infrastructure.Persistence;

namespace CountryPackageApprovalService.Tests.Integration;

/// <summary>RBAC scoped by role, country code, and organizational level (docs/ARCHITECTURE.md §4), exercised
/// through the real authentication + resource-based authorization pipeline rather than by calling Domain/Application
/// directly - these tests are the ones that would actually catch a wiring mistake in Program.cs or the
/// authorization handler.</summary>
public sealed class RbacIntegrationTests : IntegrationTestBase
{
    public RbacIntegrationTests(ApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Missing_X_User_Id_header_returns_401()
    {
        var client = Factory.CreateClient(); // no header at all
        var response = await client.GetAsync($"/api/v1/country-packages/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task User_with_no_role_grants_cannot_create_a_roadmap()
    {
        var client = CreateClient(SeedData.NoGrantsUserId);
        var response = await client.PostAsJsonAsync("/api/v1/country-packages",
            new CreateRoadmapRequest { CountryCode = "RUR", Title = "Should be rejected" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Editor_for_one_country_cannot_create_a_roadmap_for_another_country()
    {
        var client = CreateClient(SeedData.EditorRuritaniaId); // Editor for RUR only
        var response = await client.PostAsJsonAsync("/api/v1/country-packages",
            new CreateRoadmapRequest { CountryCode = "SOL", Title = "Should be rejected" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Naming_an_approver_without_clearance_for_the_steps_org_level_is_rejected()
    {
        var editor = CreateClient(SeedData.EditorRuritaniaId);
        var package = await CreatePackageAsync(editor, "RUR", "RBAC org-level test");
        await UploadDocumentAsync(editor, package.Id, 1); // step 1 is Country level

        // This reviewer only holds Regional clearance for RUR, so cannot be named approver on a Country-level step
        // (docs/ARCHITECTURE.md §4.2) - caught before any state changes, distinct from the decision-time re-check.
        var response = await SubmitStepRawAsync(editor, package.Id, 1, SeedData.ReviewerRuritaniaRegionalId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Cleared_reviewer_can_approve_the_step_they_were_named_on()
    {
        var editor = CreateClient(SeedData.EditorRuritaniaId);
        var package = await CreatePackageAsync(editor, "RUR", "RBAC happy path");
        await UploadDocumentAsync(editor, package.Id, 1);
        await SubmitStepAsync(editor, package.Id, 1, SeedData.ReviewerRuritaniaCountryId);

        var reviewerCountry = CreateClient(SeedData.ReviewerRuritaniaCountryId);
        var step = await DecideStepAsync(reviewerCountry, package.Id, 1, StepDecision.Approve, "Approved.");

        Assert.Equal("Completed", step.Status);
    }

    [Fact]
    public async Task Reading_a_package_that_does_not_exist_returns_404_not_403()
    {
        var client = CreateClient(SeedData.EditorRuritaniaId);
        var response = await client.GetAsync($"/api/v1/country-packages/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
