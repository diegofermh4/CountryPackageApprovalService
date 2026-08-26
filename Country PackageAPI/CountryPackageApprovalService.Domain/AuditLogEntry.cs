namespace CountryPackageApprovalService.Domain;

/// <summary>
/// One immutable record of a state-changing action, always written in the same DB transaction as the
/// state change it describes (see ApprovalWorkflowService). <see cref="StepId"/> is null for package-level
/// actions (e.g. roadmap creation).
/// </summary>
public class AuditLogEntry
{
    public Guid Id { get; private set; }
    public Guid PackageId { get; private set; }
    public Guid? StepId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Action { get; private set; } = default!;
    public string? DetailsJson { get; private set; }
    public DateTime TimestampUtc { get; private set; }

    private AuditLogEntry() { } // EF Core

    public AuditLogEntry(Guid packageId, Guid? stepId, Guid actorUserId, string action, string? detailsJson = null)
    {
        Id = Guid.NewGuid();
        PackageId = packageId;
        StepId = stepId;
        ActorUserId = actorUserId;
        Action = action;
        DetailsJson = detailsJson;
        TimestampUtc = DateTime.UtcNow;
    }
}
