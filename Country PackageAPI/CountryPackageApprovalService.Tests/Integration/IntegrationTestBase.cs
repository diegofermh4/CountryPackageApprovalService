using System.Net.Http.Headers;
using System.Net.Http.Json;
using Country_PackageAPI.Auth;
using CountryPackageApprovalService.Application.Dtos;
using CountryPackageApprovalService.Domain;

namespace CountryPackageApprovalService.Tests.Integration;

/// <summary>Shared plumbing for every HTTP-level test: an authenticated client per seeded user, and thin
/// wrappers around the four write endpoints so individual test methods read as the workflow steps they are,
/// not as repeated HTTP boilerplate.</summary>
public abstract class IntegrationTestBase : IClassFixture<ApiFactory>
{
    protected readonly ApiFactory Factory;

    protected IntegrationTestBase(ApiFactory factory) => Factory = factory;

    /// <summary>An <see cref="HttpClient"/> carrying the dev-header identity of <paramref name="userId"/>, or
    /// an unauthenticated client when null.</summary>
    protected HttpClient CreateClient(Guid? userId = null)
    {
        var client = Factory.CreateClient();
        if (userId is { } id) client.DefaultRequestHeaders.Add(DevHeaderAuthenticationHandler.HeaderName, id.ToString());
        return client;
    }

    protected static async Task<CountryPackageDto> CreatePackageAsync(HttpClient client, string countryCode, string title)
    {
        var response = await client.PostAsJsonAsync("/api/v1/country-packages",
            new CreateRoadmapRequest { CountryCode = countryCode, Title = title });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CountryPackageDto>())!;
    }

    protected static async Task<HttpResponseMessage> UploadDocumentRawAsync(HttpClient client, Guid packageId, int stepOrder, string fileName = "test.txt")
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("fictional test content"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", fileName);
        return await client.PostAsync($"/api/v1/country-packages/{packageId}/steps/{stepOrder}/document", content);
    }

    protected static async Task UploadDocumentAsync(HttpClient client, Guid packageId, int stepOrder, string fileName = "test.txt")
    {
        var response = await UploadDocumentRawAsync(client, packageId, stepOrder, fileName);
        response.EnsureSuccessStatusCode();
    }

    protected static async Task<HttpResponseMessage> SubmitStepRawAsync(HttpClient client, Guid packageId, int stepOrder, Guid approverOrRecipientId, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/country-packages/{packageId}/steps/{stepOrder}/submit")
        {
            Content = JsonContent.Create(new SubmitStepRequest { ApproverOrRecipientUserId = approverOrRecipientId })
        };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    protected static async Task<ApprovalStepDto> SubmitStepAsync(HttpClient client, Guid packageId, int stepOrder, Guid approverOrRecipientId, string? idempotencyKey = null)
    {
        var response = await SubmitStepRawAsync(client, packageId, stepOrder, approverOrRecipientId, idempotencyKey);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApprovalStepDto>())!;
    }

    protected static async Task<HttpResponseMessage> DecideStepRawAsync(HttpClient client, Guid packageId, int stepOrder, StepDecision decision, string? comment = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/country-packages/{packageId}/steps/{stepOrder}/decision")
        {
            Content = JsonContent.Create(new StepDecisionRequest { Decision = decision, Comment = comment })
        };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    protected static async Task<ApprovalStepDto> DecideStepAsync(HttpClient client, Guid packageId, int stepOrder, StepDecision decision, string? comment = null, string? idempotencyKey = null)
    {
        var response = await DecideStepRawAsync(client, packageId, stepOrder, decision, comment, idempotencyKey);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApprovalStepDto>())!;
    }
}
