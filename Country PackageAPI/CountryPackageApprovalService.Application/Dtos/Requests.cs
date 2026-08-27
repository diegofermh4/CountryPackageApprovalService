using System.ComponentModel.DataAnnotations;
using CountryPackageApprovalService.Domain;

namespace CountryPackageApprovalService.Application.Dtos;

/// <summary>Country Editor: create a package's roadmap instance from the active template.</summary>
public sealed class CreateRoadmapRequest
{
    [Required, StringLength(10, MinimumLength = 2)]
    public string CountryCode { get; init; } = default!;

    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; init; } = default!;
}

/// <summary>Country Editor: submit a step. For a Decision step this names the approver; for an Information
/// step this names the distribution recipient - same field, per the brief's "consistent interface" per step.</summary>
public sealed class SubmitStepRequest
{
    [Required]
    public Guid ApproverOrRecipientUserId { get; init; }
}

/// <summary>Country Reviewer: act on a pending Decision step.</summary>
public sealed class StepDecisionRequest
{
    [Required]
    public StepDecision Decision { get; init; }

    [StringLength(1000)]
    public string? Comment { get; init; }
}
