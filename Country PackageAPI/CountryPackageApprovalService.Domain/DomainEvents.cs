using CountryPackageApprovalService.Domain;

namespace CountryPackageApprovalService.Domain.Events;


public interface IDomainEvent
{
    DateTime OccurredAtUtc { get; }
}

public sealed record StepSubmittedEvent(
    Guid PackageId,
    Guid StepId,
    int StepOrder,
    Guid SubmittedBy,
    Guid ApproverOrRecipientId,
    DateTime OccurredAtUtc) : IDomainEvent;

public sealed record StepCompletedEvent(
    Guid PackageId,
    Guid StepId,
    int StepOrder,
    StepType StepType,
    Guid? DecidedBy,
    DateTime OccurredAtUtc) : IDomainEvent;

public sealed record StepReturnedEvent(
    Guid PackageId,
    Guid StepId,
    int StepOrder,
    Guid DecidedBy,
    string Comment,
    DateTime OccurredAtUtc) : IDomainEvent;
