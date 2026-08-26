using CountryPackageApprovalService.Domain;

namespace CountryPackageApprovalService.Domain.Events;

/// <summary>
/// Marker for events raised by aggregates during a use case. The Application layer reads
/// <c>CountryPackage.DomainEvents</c> after a successful operation and hands them to <c>IOutboxWriter</c>,
/// which persists them in the same DB transaction as the state change (transactional outbox - see
/// docs/ARCHITECTURE.md §3.3). A background dispatcher then "publishes" them (Service Bus in the
/// Azure target architecture; logged in this exercise).
/// </summary>
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
