using CountryPackageApprovalService.Domain;
using Microsoft.AspNetCore.Authorization;

namespace Country_PackageAPI.Authorization;

/// <summary>
/// "Does the caller hold <see cref="Role"/> for the resource's country (and, when specified, org level)?"
/// The coarse-grained, resource-based check the API layer is expected to run before invoking Application
/// (see <c>IApprovalWorkflowService</c>'s remarks and docs/ARCHITECTURE.md §4.2). It is deliberately coarse:
/// step-instance-specific rules ("only the named approver may decide this step") stay in the Domain, and the
/// "does the person being *named* as approver currently hold clearance" check stays in Application, because
/// neither can be expressed against the caller's own principal alone.
/// </summary>
public sealed class CountryRoleRequirement : IAuthorizationRequirement
{
    public UserRole Role { get; }
    public CountryRoleRequirement(UserRole role) => Role = role;
}

/// <summary>The resource a <see cref="CountryRoleRequirement"/> is evaluated against. <see cref="StepOrgLevel"/>
/// is null for actions that are not scoped to one specific step (e.g. creating a roadmap) - in that case the
/// handler accepts a grant at any org level for the country; when it is set (submit/decide on a known step),
/// the grant must cover that exact level.</summary>
public sealed record CountryPackageResource(string CountryCode, OrgLevel? StepOrgLevel = null);

/// <summary>
/// Resolves the caller's seeded <see cref="User"/> from the authenticated principal's id and re-checks their
/// current role/country/org-level clearance against <see cref="UserCountryRole"/> - never trusts anything
/// about roles from the principal itself, because the dev-header handler (like Entra ID in production) never
/// puts role claims on the principal in the first place (docs/ARCHITECTURE.md §4.1).
/// </summary>
public sealed class CountryRoleAuthorizationHandler : AuthorizationHandler<CountryRoleRequirement, CountryPackageResource>
{
    private readonly CountryPackageApprovalService.Application.Interfaces.IUserRepository _users;

    public CountryRoleAuthorizationHandler(CountryPackageApprovalService.Application.Interfaces.IUserRepository users) => _users = users;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, CountryRoleRequirement requirement, CountryPackageResource resource)
    {
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userId, out var id)) return;

        // AuthorizationHandlerContext carries no CancellationToken of its own; this lookup is a single
        // in-memory-provider read keyed by primary key, so CancellationToken.None is an acceptable trade-off
        // here rather than threading HttpContext.RequestAborted through the authorization pipeline.
        var user = await _users.GetByIdAsync(id, CancellationToken.None);
        if (user is null) return;

        var cleared = resource.StepOrgLevel is { } orgLevel
            ? user.HasClearance(resource.CountryCode, requirement.Role, orgLevel)
            : user.HasAnyClearance(resource.CountryCode, requirement.Role);

        if (cleared) context.Succeed(requirement);
    }
}
