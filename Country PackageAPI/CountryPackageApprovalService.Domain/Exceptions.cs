namespace CountryPackageApprovalService.Domain.Exceptions;

/// <summary>Base type for every business-rule violation raised by the Domain or Application layers.
/// The API layer's exception-handling middleware maps each subtype to a specific HTTP status / ProblemDetails.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
}

/// <summary>The requested aggregate/entity does not exist. Maps to 404.</summary>
public sealed class NotFoundException : DomainException
{
    public NotFoundException(string entityName, object key)
        : base($"{entityName} '{key}' was not found.") { }
}

/// <summary>An action was attempted while the step/package was not in a state that allows it
/// (e.g. submitting a step that is already PendingApproval, or approving a step that isn't).
/// Also used for missing-precondition input errors (e.g. no document attached). Maps to 409 Conflict.</summary>
public sealed class InvalidStepStateException : DomainException
{
    public InvalidStepStateException(string message) : base(message) { }
}

/// <summary>A write was attempted against a step whose document snapshot is locked (already approved). Maps to 409 Conflict.</summary>
public sealed class StepLockedException : DomainException
{
    public StepLockedException(string message) : base(message) { }
}

/// <summary>The caller is authenticated but is not entitled to perform this specific action -
/// e.g. not the step's named approver, or lacks current role/country/org-level clearance. Maps to 403 Forbidden.
/// This is a defense-in-depth check inside Domain/Application; the same rule is also enforced earlier,
/// before any business logic runs, by the API layer's resource-based authorization handlers (see docs/ARCHITECTURE.md §4).</summary>
public sealed class UnauthorizedStepActionException : DomainException
{
    public UnauthorizedStepActionException(string message) : base(message) { }
}

/// <summary>Two callers raced to mutate the same aggregate; the caller's view was stale. Maps to 409 Conflict.
/// Raised by Infrastructure after catching the EF Core concurrency exception, so Domain/Application never
/// take a dependency on the persistence provider's exception types.</summary>
public sealed class ConcurrencyConflictException : DomainException
{
    public ConcurrencyConflictException(string message) : base(message) { }
}

/// <summary>A request failed a business-rule validation that isn't a simple data-annotation check
/// (e.g. "no active roadmap template is configured"). Maps to 422 Unprocessable Entity.</summary>
public sealed class BusinessRuleValidationException : DomainException
{
    public BusinessRuleValidationException(string message) : base(message) { }
}
