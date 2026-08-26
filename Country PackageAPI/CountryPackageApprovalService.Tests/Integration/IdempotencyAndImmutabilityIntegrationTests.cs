using System.Net;
using System.Net.Http.Json;
using CountryPackageApprovalService.Application.Dtos;
using CountryPackageApprovalService.Domain;
using CountryPackageApprovalService.Infrastructure.Persistence;

namespace CountryPackageApprovalService.Tests.Integration;

/// <summary>Idempotency-key replay, document-snapshot immutability once a step locks, and preservation of
/// prior document versions across a return-for-revision loop - the "robust integration behavior" concerns
/// called out in docs/ARCHITECTURE.md §3.3.</summary>
public sealed class IdempotencyAndImmutabilityIntegrationTests : IntegrationTestBase
{
    public IdempotencyAndImmutabilityIntegrationTests(ApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Replaying_submit_with_the_same_Idempotency_Key_returns_the_original_result_instead_of_re_executing()
    {
        var editor = CreateClient(SeedData.EditorRuritaniaId);
        var package = await CreatePackageAsync(editor, "RUR", "Idempotency test");
        await UploadDocumentAsync(editor, package.Id, 1);

        const string key = "submit-step-1-once";
        var first = await SubmitStepAsync(editor, package.Id, 1, SeedData.ReviewerRuritaniaCountryId, key);

        // Without idempotency this would 409 - the step is no longer NotStarted/ReturnedForRevision. A
        // correct replay instead returns the exact original response.
        var second = await SubmitStepAsync(editor, package.Id, 1, SeedData.ReviewerRuritaniaCountryId, key);

        Assert.Equal(first.SubmittedAtUtc, second.SubmittedAtUtc);
        Assert.Equal(first.Status, second.Status);
    }

    [Fact]
    public async Task Submitting_twice_without_an_Idempotency_Key_is_rejected_as_an_invalid_state_transition()
    {
        var editor = CreateClient(SeedData.EditorRuritaniaId);
        var package = await CreatePackageAsync(editor, "RUR", "No idempotency key test");
        await UploadDocumentAsync(editor, package.Id, 1);
        await SubmitStepAsync(editor, package.Id, 1, SeedData.ReviewerRuritaniaCountryId);

        var response = await SubmitStepRawAsync(editor, package.Id, 1, SeedData.ReviewerRuritaniaCountryId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_to_a_locked_step_returns_409()
    {
        var editor = CreateClient(SeedData.EditorRuritaniaId);
        var package = await CreatePackageAsync(editor, "RUR", "Immutability test");
        await UploadDocumentAsync(editor, package.Id, 1);
        await SubmitStepAsync(editor, package.Id, 1, SeedData.ReviewerRuritaniaCountryId);

        var reviewer = CreateClient(SeedData.ReviewerRuritaniaCountryId);
        await DecideStepAsync(reviewer, package.Id, 1, StepDecision.Approve, "Approved.");

        var secondUpload = await UploadDocumentRawAsync(editor, package.Id, 1);

        Assert.Equal(HttpStatusCode.Conflict, secondUpload.StatusCode);
    }

    [Fact]
    public async Task Returned_step_accepts_a_new_document_version_and_keeps_the_previous_one_unchanged()
    {
        var editor = CreateClient(SeedData.EditorRuritaniaId);
        var package = await CreatePackageAsync(editor, "RUR", "Return for revision test");
        await UploadDocumentAsync(editor, package.Id, 1, "v1.pdf");
        await SubmitStepAsync(editor, package.Id, 1, SeedData.ReviewerRuritaniaCountryId);

        var reviewer = CreateClient(SeedData.ReviewerRuritaniaCountryId);
        var returnedStep = await DecideStepAsync(reviewer, package.Id, 1, StepDecision.ReturnForRevision, "Please add the missing annex.");
        Assert.Equal("ReturnedForRevision", returnedStep.Status);
        Assert.False(returnedStep.IsLocked);

        await UploadDocumentAsync(editor, package.Id, 1, "v2.pdf");
        await SubmitStepAsync(editor, package.Id, 1, SeedData.ReviewerRuritaniaCountryId);

        var final = await (await editor.GetAsync($"/api/v1/country-packages/{package.Id}"))
            .Content.ReadFromJsonAsync<CountryPackageDto>();
        var step1 = final!.Steps.Single(s => s.StepOrder == 1);

        Assert.Equal(2, step1.Documents.Count);
        Assert.Equal(1, step1.Documents[0].VersionNumber);
        Assert.Equal("v1.pdf", step1.Documents[0].FileName);
        Assert.Equal(2, step1.Documents[1].VersionNumber);
        Assert.Equal("v2.pdf", step1.Documents[1].FileName);
    }

    [Fact]
    public async Task Returning_a_step_without_a_comment_is_rejected()
    {
        var editor = CreateClient(SeedData.EditorRuritaniaId);
        var package = await CreatePackageAsync(editor, "RUR", "Missing comment test");
        await UploadDocumentAsync(editor, package.Id, 1);
        await SubmitStepAsync(editor, package.Id, 1, SeedData.ReviewerRuritaniaCountryId);

        var reviewer = CreateClient(SeedData.ReviewerRuritaniaCountryId);
        var response = await DecideStepRawAsync(reviewer, package.Id, 1, StepDecision.ReturnForRevision, comment: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
