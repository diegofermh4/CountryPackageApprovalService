using CountryPackageApprovalService.Application.Dtos;

namespace CountryPackageApprovalService.Application.Services;

/// <summary>
/// The use cases from the brief's "Expose core operations" section. Callers (the API layer) are expected to
/// have already run the coarse-grained, resource-based authorization check (Editor/Reviewer role + country
/// code against the loaded package - docs/ARCHITECTURE.md §4.2) before calling in here; this service adds the
/// checks that need a *second* aggregate loaded (does the named approver/decider currently hold clearance),
/// which a per-request authorization handler can't naturally express against someone other than the caller.
/// </summary>
public interface IApprovalWorkflowService
{
    Task<CountryPackageDto> CreateRoadmapAsync(CreateRoadmapRequest request, Guid createdBy, CancellationToken ct);

    Task<CountryPackageDto> GetPackageAsync(Guid packageId, CancellationToken ct);

    Task<IReadOnlyList<AuditLogEntryDto>> GetAuditTrailAsync(Guid packageId, CancellationToken ct);

    Task<DocumentVersionDto> UploadDocumentAsync(
        Guid packageId, int stepOrder, Guid uploadedBy,
        Stream content, string fileName, string contentType, CancellationToken ct);

    Task<ApprovalStepDto> SubmitStepAsync(
        Guid packageId, int stepOrder, Guid submittedBy,
        SubmitStepRequest request, string? idempotencyKey, CancellationToken ct);

    Task<ApprovalStepDto> DecideStepAsync(
        Guid packageId, int stepOrder, Guid deciderId,
        StepDecisionRequest request, string? idempotencyKey, CancellationToken ct);
}
