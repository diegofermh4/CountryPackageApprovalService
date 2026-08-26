namespace CountryPackageApprovalService.Domain;

/// <summary>The two roles from the brief. A user holds exactly one role, scoped per country (see <see cref="UserCountryRole"/>).</summary>
public enum UserRole
{
    CountryEditor,
    CountryReviewer
}

/// <summary>
/// Organizational level. On a <see cref="UserCountryRole"/>, <see cref="Both"/> means the user is cleared at both
/// levels for that country. On an <see cref="ApprovalStep"/> / <see cref="RoadmapStepTemplate"/> the level is always
/// <see cref="Country"/> or <see cref="Regional"/> - a step is never "Both".
/// </summary>
public enum OrgLevel
{
    Country,
    Regional,
    Both
}

/// <summary>The two step shapes described in the brief - every step in the roadmap is one of these.</summary>
public enum StepType
{
    /// <summary>Editor submits a document + names an approver; the Reviewer approves or returns it.</summary>
    Decision,

    /// <summary>Editor names a recipient and submits; submission itself completes the step. No reviewer action.</summary>
    Information
}

/// <summary>Lifecycle of a single <see cref="ApprovalStep"/>. See docs/ARCHITECTURE.md §2.3 for the full state diagram.</summary>
public enum StepStatus
{
    NotStarted,
    PendingApproval,
    Completed,
    ReturnedForRevision
}

/// <summary>The two actions a Country Reviewer can take on a pending Decision step.</summary>
public enum StepDecision
{
    Approve,
    ReturnForRevision
}
