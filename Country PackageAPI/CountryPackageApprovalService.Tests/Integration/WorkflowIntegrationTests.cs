using System.Net.Http.Json;
using CountryPackageApprovalService.Application.Dtos;
using CountryPackageApprovalService.Domain;
using CountryPackageApprovalService.Infrastructure.Persistence;

namespace CountryPackageApprovalService.Tests.Integration;

/// <summary>End-to-end happy path through the real Api pipeline: create a roadmap, walk all four steps
/// (Decision/Country, Information/Country, Decision/Regional, Information/Regional) to completion, and check
/// the audit trail records every state change along the way.</summary>
public sealed class WorkflowIntegrationTests : IntegrationTestBase
{
    public WorkflowIntegrationTests(ApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Full_roadmap_walks_all_four_steps_to_completion_with_a_full_audit_trail()
    {
        var editor = CreateClient(SeedData.EditorRuritaniaId);

        var package = await CreatePackageAsync(editor, "RUR", "FY26 Country Package");
        Assert.Equal(4, package.Steps.Count);
        Assert.Equal("InProgress", package.Status);

        // Step 1: Decision / Country.
        await UploadDocumentAsync(editor, package.Id, 1, "roadmap-v1.pdf");
        await SubmitStepAsync(editor, package.Id, 1, SeedData.ReviewerRuritaniaCountryId);
        var reviewerCountry = CreateClient(SeedData.ReviewerRuritaniaCountryId);
        await DecideStepAsync(reviewerCountry, package.Id, 1, StepDecision.Approve, "Approved at country level.");

        // Step 2: Information / Country - submission itself completes the step, no reviewer action.
        var step2 = await SubmitStepAsync(editor, package.Id, 2, SeedData.ReviewerRuritaniaCountryId);
        Assert.Equal("Completed", step2.Status);

        // Step 3: Decision / Regional.
        await UploadDocumentAsync(editor, package.Id, 3, "roadmap-regional-v1.pdf");
        await SubmitStepAsync(editor, package.Id, 3, SeedData.ReviewerRuritaniaRegionalId);
        var reviewerRegional = CreateClient(SeedData.ReviewerRuritaniaRegionalId);
        await DecideStepAsync(reviewerRegional, package.Id, 3, StepDecision.Approve, "Approved at regional level.");

        // Step 4: Information / Regional.
        var step4 = await SubmitStepAsync(editor, package.Id, 4, SeedData.ReviewerRuritaniaRegionalId);
        Assert.Equal("Completed", step4.Status);

        var final = await (await editor.GetAsync($"/api/v1/country-packages/{package.Id}"))
            .Content.ReadFromJsonAsync<CountryPackageDto>();
        Assert.Equal("Completed", final!.Status);
        Assert.Equal(4, final.CurrentStepOrder);
        Assert.All(final.Steps, s => Assert.Equal("Completed", s.Status));
        Assert.All(final.Steps, s => Assert.True(s.IsLocked));

        var auditLog = await (await editor.GetAsync($"/api/v1/country-packages/{package.Id}/audit-log"))
            .Content.ReadFromJsonAsync<List<AuditLogEntryDto>>();
        Assert.NotEmpty(auditLog!);
        Assert.Contains(auditLog!, e => e.Action == "RoadmapCreated");
        Assert.Contains(auditLog!, e => e.Action == "DocumentUploaded");
        Assert.Contains(auditLog!, e => e.Action == "StepSubmitted");
        Assert.Contains(auditLog!, e => e.Action == "StepApproved");
        Assert.Contains(auditLog!, e => e.Action == "StepDistributed"); // Information steps log distinctly from Decision steps
    }
}
