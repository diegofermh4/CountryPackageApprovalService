using Country_PackageAPI.Auth;
using Country_PackageAPI.Authorization;
using Country_PackageAPI.Swagger;
using CountryPackageApprovalService.Application.Dtos;
using CountryPackageApprovalService.Application.Services;
using CountryPackageApprovalService.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Country_PackageAPI.Controllers;

/// <summary>
/// The Country Package Approval Service's core operations (docs/ARCHITECTURE.md §5). Every action here is a
/// thin adapter: bind input, run the coarse-grained resource-based authorization check for the target country
/// (and, once a step's org level is known, that level too), then hand off to
/// <see cref="IApprovalWorkflowService"/>. No business rule, persistence, or cross-aggregate check lives here -
/// see that interface's remarks for exactly where the authorization responsibility splits between this
/// controller, the Domain, and the Application layer.
/// </summary>
[ApiController]
[Route("api/v1/country-packages")]
[Authorize]
[Produces("application/json")]
public sealed class CountryPackagesController : ControllerBase
{
    private readonly IApprovalWorkflowService _workflow;
    private readonly IAuthorizationService _authorization;

    public CountryPackagesController(IApprovalWorkflowService workflow, IAuthorizationService authorization)
    {
        _workflow = workflow;
        _authorization = authorization;
    }

    /// <summary>Country Editor: create a package's roadmap instance from the currently active roadmap template.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CountryPackageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateRoadmap([FromBody] CreateRoadmapRequest request, CancellationToken ct)
    {
        var auth = await AuthorizeCountryRoleAsync(UserRole.CountryEditor, request.CountryCode);
        if (!auth.Succeeded) return Forbid();

        var package = await _workflow.CreateRoadmapAsync(request, User.RequireUserId(), ct);
        return CreatedAtAction(nameof(GetPackage), new { packageId = package.Id }, package);
    }

    /// <summary>Read a package with its full roadmap instance - every step and each step's document versions.</summary>
    [HttpGet("{packageId:guid}")]
    [ProducesResponseType(typeof(CountryPackageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CountryPackageDto>> GetPackage(Guid packageId, CancellationToken ct) =>
        Ok(await _workflow.GetPackageAsync(packageId, ct));

    /// <summary>Read the full, append-only audit trail for a package.</summary>
    [HttpGet("{packageId:guid}/audit-log")]
    [ProducesResponseType(typeof(IReadOnlyList<AuditLogEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AuditLogEntryDto>>> GetAuditTrail(Guid packageId, CancellationToken ct) =>
        Ok(await _workflow.GetAuditTrailAsync(packageId, ct));

    /// <summary>Country Editor: attach a new document version to a Decision step. Rejected once the step is
    /// locked (already approved) or otherwise not in a status that accepts a new version.</summary>
    [HttpPost("{packageId:guid}/steps/{stepOrder:int}/document")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(DocumentVersionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentVersionDto>> UploadDocument(
        Guid packageId, int stepOrder, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Problem(title: "Bad Request", detail: "A non-empty file is required.", statusCode: StatusCodes.Status400BadRequest);

        // Loaded once here to authorize against the package's own country, and again inside the workflow
        // service to perform the mutation - see IApprovalWorkflowService's remarks on why the two checks
        // are deliberately not collapsed into one shared load across the Api/Application boundary.
        var package = await _workflow.GetPackageAsync(packageId, ct);
        var auth = await AuthorizeCountryRoleAsync(UserRole.CountryEditor, package.CountryCode);
        if (!auth.Succeeded) return Forbid();

        await using var stream = file.OpenReadStream();
        var document = await _workflow.UploadDocumentAsync(
            packageId, stepOrder, User.RequireUserId(), stream, file.FileName, file.ContentType, ct);

        return Ok(document);
    }

    /// <summary>Country Editor: submit a step. For a Decision step this names the Reviewer who must act on it;
    /// for an Information step this names the recipient and completes the step immediately - no reviewer action.</summary>
    [HttpPost("{packageId:guid}/steps/{stepOrder:int}/submit")]
    [IdempotentAction]
    [ProducesResponseType(typeof(ApprovalStepDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApprovalStepDto>> SubmitStep(
        Guid packageId, int stepOrder, [FromBody] SubmitStepRequest request, CancellationToken ct)
    {
        var package = await _workflow.GetPackageAsync(packageId, ct);
        var auth = await AuthorizeCountryRoleAsync(UserRole.CountryEditor, package.CountryCode);
        if (!auth.Succeeded) return Forbid();

        var idempotencyKey = ReadIdempotencyKey();
        return Ok(await _workflow.SubmitStepAsync(packageId, stepOrder, User.RequireUserId(), request, idempotencyKey, ct));
    }

    /// <summary>Country Reviewer: approve or return a pending Decision step. Only the step's named approver may
    /// act, and only while currently holding Reviewer clearance for the step's country and org level - both
    /// re-checked at the moment of decision, since either can have changed since submission (docs/ARCHITECTURE.md §4.3).</summary>
    [HttpPost("{packageId:guid}/steps/{stepOrder:int}/decision")]
    [IdempotentAction]
    [ProducesResponseType(typeof(ApprovalStepDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApprovalStepDto>> DecideStep(
        Guid packageId, int stepOrder, [FromBody] StepDecisionRequest request, CancellationToken ct)
    {
        var package = await _workflow.GetPackageAsync(packageId, ct);
        var step = package.Steps.FirstOrDefault(s => s.StepOrder == stepOrder);
        if (step is null) return NotFound();

        var stepOrgLevel = Enum.Parse<OrgLevel>(step.OrgLevel);
        var auth = await AuthorizeCountryRoleAsync(UserRole.CountryReviewer, package.CountryCode, stepOrgLevel);
        if (!auth.Succeeded) return Forbid();

        var idempotencyKey = ReadIdempotencyKey();
        return Ok(await _workflow.DecideStepAsync(packageId, stepOrder, User.RequireUserId(), request, idempotencyKey, ct));
    }

    private Task<AuthorizationResult> AuthorizeCountryRoleAsync(UserRole role, string countryCode, OrgLevel? stepOrgLevel = null) =>
        _authorization.AuthorizeAsync(User, new CountryPackageResource(countryCode, stepOrgLevel), new CountryRoleRequirement(role));

    private string? ReadIdempotencyKey() =>
        Request.Headers.TryGetValue("Idempotency-Key", out var value) ? value.ToString() : null;
}
