namespace CountryPackageApprovalService.Domain;

/// <summary>
/// The pre-defined approval process, modeled as data rather than hardcoded steps (docs/ARCHITECTURE.md §2.2)
/// so a revision or a second package type is a data change, not a redeploy.
/// </summary>
public class RoadmapTemplate
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public int Version { get; private set; }
    public bool IsActive { get; private set; }

    private readonly List<RoadmapStepTemplate> _steps = new();
    public IReadOnlyList<RoadmapStepTemplate> Steps => _steps.AsReadOnly();

    private RoadmapTemplate() { } // EF Core

    public RoadmapTemplate(Guid id, string name, int version, bool isActive)
    {
        Id = id;
        Name = name;
        Version = version;
        IsActive = isActive;
    }

    public RoadmapStepTemplate AddStep(int stepOrder, StepType stepType, OrgLevel orgLevel, string name)
    {
        var step = new RoadmapStepTemplate(Guid.NewGuid(), Id, stepOrder, stepType, orgLevel, name);
        _steps.Add(step);
        return step;
    }

    /// <summary>
    /// The four fixed steps from the brief. This is the one active template used throughout the take-home;
    /// the template/instance split exists so a future variant is data, not code (see docs/ARCHITECTURE.md §2.2).
    /// </summary>
    public static RoadmapTemplate CreateDefault()
    {
        var template = new RoadmapTemplate(Guid.NewGuid(), "Standard Country Package Approval Roadmap", version: 1, isActive: true);
        template.AddStep(1, StepType.Decision, OrgLevel.Country, "Obtain decision from country level management");
        template.AddStep(2, StepType.Information, OrgLevel.Country, "Distribute approved package to country level management");
        template.AddStep(3, StepType.Decision, OrgLevel.Regional, "Obtain decision from regional management");
        template.AddStep(4, StepType.Information, OrgLevel.Regional, "Distribute package to regional level management");
        return template;
    }
}

public class RoadmapStepTemplate
{
    public Guid Id { get; private set; }
    public Guid RoadmapTemplateId { get; private set; }
    public int StepOrder { get; private set; }
    public StepType StepType { get; private set; }
    public OrgLevel OrgLevel { get; private set; }
    public string Name { get; private set; } = default!;

    private RoadmapStepTemplate() { } // EF Core

    internal RoadmapStepTemplate(Guid id, Guid roadmapTemplateId, int stepOrder, StepType stepType, OrgLevel orgLevel, string name)
    {
        Id = id;
        RoadmapTemplateId = roadmapTemplateId;
        StepOrder = stepOrder;
        StepType = stepType;
        OrgLevel = orgLevel;
        Name = name;
    }
}
