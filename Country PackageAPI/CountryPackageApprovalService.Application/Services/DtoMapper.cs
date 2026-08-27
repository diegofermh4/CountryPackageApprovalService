using CountryPackageApprovalService.Application.Dtos;
using CountryPackageApprovalService.Domain;

namespace CountryPackageApprovalService.Application.Services;


internal static class DtoMapper
{
    public static DocumentVersionDto ToDto(DocumentVersion d) =>
        new(d.Id, d.VersionNumber, d.FileName, d.ContentType, d.SizeBytes, d.UploadedBy, d.UploadedAtUtc);

    public static ApprovalStepDto ToDto(ApprovalStep s) =>
        new(
            s.Id,
            s.StepOrder,
            s.StepType.ToString(),
            s.OrgLevel.ToString(),
            s.Name,
            s.Status.ToString(),
            s.RequiresDocument,
            s.AssignedApproverId,
            s.SubmittedBy,
            s.SubmittedAtUtc,
            s.DecidedBy,
            s.DecidedAtUtc,
            s.DecisionComment,
            s.IsLocked,
            s.Documents.OrderBy(d => d.VersionNumber).Select(ToDto).ToList());

    public static CountryPackageDto ToDto(CountryPackage p) =>
        new(
            p.Id,
            p.CountryCode,
            p.Title,
            p.Status,
            p.CurrentStepOrder,
            p.CreatedBy,
            p.CreatedAtUtc,
            p.Steps.OrderBy(s => s.StepOrder).Select(ToDto).ToList());

    public static AuditLogEntryDto ToDto(AuditLogEntry a) =>
        new(a.Id, a.PackageId, a.StepId, a.ActorUserId, a.Action, a.DetailsJson, a.TimestampUtc);
}
