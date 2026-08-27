using CountryPackageApprovalService.Domain.Events;
using CountryPackageApprovalService.Domain.Exceptions;

namespace CountryPackageApprovalService.Domain;


public class CountryPackage
{
    public Guid Id { get; private set; }
    public string CountryCode { get; private set; } = default!;
    public Guid RoadmapTemplateId { get; private set; }
    public string Title { get; private set; } = default!;
    public int CurrentStepOrder { get; private set; } = 1;
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>EF Core row-version concurrency token - see <see cref="ApprovalStep.RowVersion"/> for the rationale.</summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    // Exposed directly (not re-copied/sorted) so EF Core's field-backed navigation materializer can add to this
    // exact collection - see the "_steps" field-access configuration in AppDbContext.OnModelCreating. Callers
    // that need step order should read StepOrder themselves or use GetStep/GetCurrentStep; DtoMapper sorts on
    // the way out to a response.
    private readonly List<ApprovalStep> _steps = new();
    public IReadOnlyList<ApprovalStep> Steps => _steps;

    /// <summary>Aggregates this package's own events with every step's - see <see cref="Events.IDomainEvent"/>.</summary>
    public IReadOnlyList<IDomainEvent> DomainEvents =>
        _steps.SelectMany(s => s.DomainEvents).ToList();

    public void ClearDomainEvents()
    {
        foreach (var step in _steps) step.ClearDomainEvents();
    }

    private CountryPackage() { } // EF Core

    public static CountryPackage CreateFromTemplate(string countryCode, RoadmapTemplate template, string title, Guid createdBy)
    {
        if (!template.IsActive)
            throw new BusinessRuleValidationException("Cannot create a package from an inactive roadmap template.");
        if (template.Steps.Count == 0)
            throw new BusinessRuleValidationException("The roadmap template has no steps configured.");

        var package = new CountryPackage
        {
            Id = Guid.NewGuid(),
            CountryCode = countryCode,
            RoadmapTemplateId = template.Id,
            Title = title,
            CurrentStepOrder = 1,
            CreatedBy = createdBy,
            CreatedAtUtc = DateTime.UtcNow
        };

        foreach (var stepTemplate in template.Steps.OrderBy(s => s.StepOrder))
        {
            package._steps.Add(ApprovalStep.CreateFromTemplate(package.Id, stepTemplate));
        }

        return package;
    }

    public ApprovalStep GetStep(int stepOrder) =>
        _steps.SingleOrDefault(s => s.StepOrder == stepOrder)
        ?? throw new NotFoundException(nameof(ApprovalStep), stepOrder);

    public ApprovalStep GetCurrentStep() => GetStep(CurrentStepOrder);

    /// <summary>Advances the package's pointer once the current step completes. Idempotent-safe to call more than
    /// once at the tail: it never advances past the last step.</summary>
    public void AdvanceIfCurrentStepCompleted()
    {
        var current = GetCurrentStep();
        if (current.Status == StepStatus.Completed && CurrentStepOrder < _steps.Count)
        {
            CurrentStepOrder++;
        }
    }

    /// <summary>Derived, never stored (see docs/ARCHITECTURE.md §2.3): InProgress while any step is active,
    /// ReturnedForRevision when the current step is in that state, Completed once every step is Completed.</summary>
    public string Status
    {
        get
        {
            if (_steps.Any(s => s.Status == StepStatus.ReturnedForRevision)) return "ReturnedForRevision";
            if (_steps.All(s => s.Status == StepStatus.Completed)) return "Completed";
            return "InProgress";
        }
    }
}
