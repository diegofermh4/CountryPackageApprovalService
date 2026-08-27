using CountryPackageApprovalService.Domain.Events;
using CountryPackageApprovalService.Domain.Exceptions;

namespace CountryPackageApprovalService.Domain;


public class ApprovalStep
{
    public Guid Id { get; private set; }
    public Guid PackageId { get; private set; }
    public Guid TemplateStepId { get; private set; }
    public int StepOrder { get; private set; }
    public StepType StepType { get; private set; }
    public OrgLevel OrgLevel { get; private set; }
    public string Name { get; private set; } = default!;
    public StepStatus Status { get; private set; } = StepStatus.NotStarted;

    /// <summary>Decision steps: the named Reviewer. Information steps: the named recipient. Set at submission time.</summary>
    public Guid? AssignedApproverId { get; private set; }
    public Guid? SubmittedBy { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTime? DecidedAtUtc { get; private set; }
    public string? DecisionComment { get; private set; }

    /// <summary>True once this step is Completed - its document snapshot is then read-only (see <see cref="DocumentVersion"/>).</summary>
    public bool IsLocked { get; private set; }

    /// <summary>EF Core row-version concurrency token (auto-generated/incremented by the InMemory provider on SaveChanges;
    /// maps 1:1 onto a SQL Server `rowversion` column in the Azure target). See docs/ARCHITECTURE.md §3.3.</summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private readonly List<DocumentVersion> _documents = new();
    public IReadOnlyList<DocumentVersion> Documents => _documents.AsReadOnly();

    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    internal void ClearDomainEvents() => _domainEvents.Clear();

    private ApprovalStep() { } // EF Core

    internal static ApprovalStep CreateFromTemplate(Guid packageId, RoadmapStepTemplate template) =>
        new()
        {
            Id = Guid.NewGuid(),
            PackageId = packageId,
            TemplateStepId = template.Id,
            StepOrder = template.StepOrder,
            StepType = template.StepType,
            OrgLevel = template.OrgLevel,
            Name = template.Name,
            Status = StepStatus.NotStarted
        };

    /// <summary>Only Decision steps carry a reviewable document; Information steps just need a named recipient.</summary>
    public bool RequiresDocument => StepType == StepType.Decision;

    /// <summary>Cheap pre-check mirroring the guard inside <see cref="AttachDocument"/>, so callers (and the
    /// API's response DTOs) can tell upload is currently allowed without needing to attempt it.</summary>
    public bool CanAcceptDocument =>
        StepType == StepType.Decision && !IsLocked && Status is StepStatus.NotStarted or StepStatus.ReturnedForRevision;

    public DocumentVersion AttachDocument(Guid uploadedBy, string fileName, string blobUri, string contentType, long sizeBytes, string checksum)
    {
        if (StepType != StepType.Decision)
            throw new InvalidStepStateException($"Step {StepOrder} ('{Name}') is an information step and does not accept documents.");
        if (IsLocked)
            throw new StepLockedException($"Step {StepOrder} ('{Name}') is locked; its approved document snapshot cannot be replaced.");
        if (Status is not (StepStatus.NotStarted or StepStatus.ReturnedForRevision))
            throw new InvalidStepStateException($"Step {StepOrder} ('{Name}') does not accept documents while in status '{Status}'.");

        var version = _documents.Count == 0 ? 1 : _documents.Max(d => d.VersionNumber) + 1;
        var doc = new DocumentVersion(Id, version, fileName, blobUri, contentType, sizeBytes, checksum, uploadedBy);
        _documents.Add(doc);
        return doc;
    }

    /// <summary>Editor submits: for a Decision step this opens it for review; for an Information step,
    /// submission itself completes and locks the step (no reviewer action - per the brief).</summary>
    public void Submit(Guid submittedBy, Guid approverOrRecipientId)
    {
        if (Status is not (StepStatus.NotStarted or StepStatus.ReturnedForRevision))
            throw new InvalidStepStateException($"Step {StepOrder} ('{Name}') cannot be submitted from status '{Status}'.");
        if (RequiresDocument && _documents.Count == 0)
            throw new InvalidStepStateException($"Step {StepOrder} ('{Name}') requires a document before it can be submitted.");

        SubmittedBy = submittedBy;
        AssignedApproverId = approverOrRecipientId;
        SubmittedAtUtc = DateTime.UtcNow;

        if (StepType == StepType.Decision)
        {
            Status = StepStatus.PendingApproval;
            _domainEvents.Add(new StepSubmittedEvent(PackageId, Id, StepOrder, submittedBy, approverOrRecipientId, DateTime.UtcNow));
        }
        else
        {
            Status = StepStatus.Completed;
            DecidedBy = submittedBy;
            DecidedAtUtc = DateTime.UtcNow;
            IsLocked = true;
            _domainEvents.Add(new StepCompletedEvent(PackageId, Id, StepOrder, StepType, submittedBy, DateTime.UtcNow));
        }
    }

    public void Approve(Guid deciderId, string? comment)
    {
        EnsurePendingAndAssignedTo(deciderId);
        Status = StepStatus.Completed;
        DecidedBy = deciderId;
        DecidedAtUtc = DateTime.UtcNow;
        DecisionComment = comment;
        IsLocked = true; // snapshot: this step's document(s) are now immutable
        _domainEvents.Add(new StepCompletedEvent(PackageId, Id, StepOrder, StepType, deciderId, DateTime.UtcNow));
    }

    public void Return(Guid deciderId, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            throw new InvalidStepStateException("A comment is required when returning a step for revision.");

        EnsurePendingAndAssignedTo(deciderId);
        Status = StepStatus.ReturnedForRevision;
        DecidedBy = deciderId;
        DecidedAtUtc = DateTime.UtcNow;
        DecisionComment = comment;
        _domainEvents.Add(new StepReturnedEvent(PackageId, Id, StepOrder, deciderId, comment, DateTime.UtcNow));
    }

    private void EnsurePendingAndAssignedTo(Guid deciderId)
    {
        if (StepType != StepType.Decision)
            throw new InvalidStepStateException($"Step {StepOrder} ('{Name}') is not a decision step.");
        if (Status != StepStatus.PendingApproval)
            throw new InvalidStepStateException($"Step {StepOrder} ('{Name}') is not pending approval (current status '{Status}').");
        if (AssignedApproverId != deciderId)
            throw new UnauthorizedStepActionException("Only the reviewer named as approver for this step may act on it.");
    }
}
