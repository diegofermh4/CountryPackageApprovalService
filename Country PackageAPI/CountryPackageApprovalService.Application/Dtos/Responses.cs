namespace CountryPackageApprovalService.Application.Dtos;

public sealed record DocumentVersionDto(
    Guid Id,
    int VersionNumber,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid UploadedBy,
    DateTime UploadedAtUtc);

public sealed record ApprovalStepDto(
    Guid Id,
    int StepOrder,
    string StepType,
    string OrgLevel,
    string Name,
    string Status,
    bool RequiresDocument,
    Guid? AssignedApproverId,
    Guid? SubmittedBy,
    DateTime? SubmittedAtUtc,
    Guid? DecidedBy,
    DateTime? DecidedAtUtc,
    string? DecisionComment,
    bool IsLocked,
    IReadOnlyList<DocumentVersionDto> Documents);

public sealed record CountryPackageDto(
    Guid Id,
    string CountryCode,
    string Title,
    string Status,
    int CurrentStepOrder,
    Guid CreatedBy,
    DateTime CreatedAtUtc,
    IReadOnlyList<ApprovalStepDto> Steps);

public sealed record AuditLogEntryDto(
    Guid Id,
    Guid PackageId,
    Guid? StepId,
    Guid ActorUserId,
    string Action,
    string? DetailsJson,
    DateTime TimestampUtc);
