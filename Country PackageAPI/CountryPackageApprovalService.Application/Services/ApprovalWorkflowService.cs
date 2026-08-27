using CountryPackageApprovalService.Application.Dtos;
using CountryPackageApprovalService.Application.Interfaces;
using CountryPackageApprovalService.Domain;
using CountryPackageApprovalService.Domain.Exceptions;

namespace CountryPackageApprovalService.Application.Services;

public sealed class ApprovalWorkflowService : IApprovalWorkflowService
{
    private readonly ICountryPackageRepository _packages;
    private readonly IRoadmapTemplateRepository _templates;
    private readonly ICountryRepository _countries;
    private readonly IUserRepository _users;
    private readonly IAuditLogRepository _auditLog;
    private readonly IDocumentStore _documentStore;
    private readonly IOutboxWriter _outbox;
    private readonly IIdempotencyStore _idempotency;
    private readonly IUnitOfWork _unitOfWork;

    public ApprovalWorkflowService(
        ICountryPackageRepository packages,
        IRoadmapTemplateRepository templates,
        ICountryRepository countries,
        IUserRepository users,
        IAuditLogRepository auditLog,
        IDocumentStore documentStore,
        IOutboxWriter outbox,
        IIdempotencyStore idempotency,
        IUnitOfWork unitOfWork)
    {
        _packages = packages;
        _templates = templates;
        _countries = countries;
        _users = users;
        _auditLog = auditLog;
        _documentStore = documentStore;
        _outbox = outbox;
        _idempotency = idempotency;
        _unitOfWork = unitOfWork;
    }

    public async Task<CountryPackageDto> CreateRoadmapAsync(CreateRoadmapRequest request, Guid createdBy, CancellationToken ct)
    {
        if (!await _countries.ExistsAsync(request.CountryCode, ct))
            throw new NotFoundException(nameof(Country), request.CountryCode);

        var template = await _templates.GetActiveAsync(ct)
            ?? throw new BusinessRuleValidationException("No active roadmap template is configured.");

        var package = CountryPackage.CreateFromTemplate(request.CountryCode, template, request.Title, createdBy);
        await _packages.AddAsync(package, ct);

        _auditLog.Add(new AuditLogEntry(package.Id, null, createdBy, "RoadmapCreated",
            $"{{\"countryCode\":\"{request.CountryCode}\",\"title\":\"{request.Title}\"}}"));

        DispatchDomainEvents(package);
        await _unitOfWork.SaveChangesAsync(ct);

        return DtoMapper.ToDto(package);
    }

    public async Task<CountryPackageDto> GetPackageAsync(Guid packageId, CancellationToken ct)
    {
        var package = await GetPackageOrThrowAsync(packageId, ct);
        return DtoMapper.ToDto(package);
    }

    public async Task<IReadOnlyList<AuditLogEntryDto>> GetAuditTrailAsync(Guid packageId, CancellationToken ct)
    {
        await GetPackageOrThrowAsync(packageId, ct); // 404 if the package itself doesn't exist
        var entries = await _auditLog.GetForPackageAsync(packageId, ct);
        return entries.Select(DtoMapper.ToDto).ToList();
    }

    public async Task<DocumentVersionDto> UploadDocumentAsync(
        Guid packageId, int stepOrder, Guid uploadedBy,
        Stream content, string fileName, string contentType, CancellationToken ct)
    {
        var package = await GetPackageOrThrowAsync(packageId, ct);
        var step = package.GetStep(stepOrder);

        // Cheap pre-check before we write any bytes to storage.
        if (!step.CanAcceptDocument)
        {
            if (step.IsLocked)
                throw new StepLockedException($"Step {stepOrder} ('{step.Name}') is locked; its approved document snapshot cannot be replaced.");
            throw new InvalidStepStateException($"Step {stepOrder} ('{step.Name}') does not accept documents while in status '{step.Status}'.");
        }

        // Storage write happens before the DB commit (docs/ARCHITECTURE.md §3.3 "Partial failures"): if the
        // DB commit below fails, the blob is simply never referenced and is swept by a retention job later -
        // it never corrupts state, it just wastes storage transiently.
        var stored = await _documentStore.SaveAsync(package.Id, step.Id, content, fileName, contentType, ct);

        var document = step.AttachDocument(uploadedBy, fileName, stored.Uri, contentType, stored.SizeBytes, stored.Checksum);

        _auditLog.Add(new AuditLogEntry(package.Id, step.Id, uploadedBy, "DocumentUploaded",
            $"{{\"fileName\":\"{fileName}\",\"version\":{document.VersionNumber}}}"));

        DispatchDomainEvents(package);
        await _unitOfWork.SaveChangesAsync(ct);

        return DtoMapper.ToDto(document);
    }

    public async Task<ApprovalStepDto> SubmitStepAsync(
        Guid packageId, int stepOrder, Guid submittedBy,
        SubmitStepRequest request, string? idempotencyKey, CancellationToken ct)
    {
        var cacheKey = BuildIdempotencyKey("submit", packageId, stepOrder, idempotencyKey);
        if (cacheKey is not null && _idempotency.TryGetResponse<ApprovalStepDto>(cacheKey, out var cached) && cached is not null)
            return cached;

        var package = await GetPackageOrThrowAsync(packageId, ct);
        var step = package.GetStep(stepOrder);

        var target = await _users.GetByIdAsync(request.ApproverOrRecipientUserId, ct)
            ?? throw new BusinessRuleValidationException($"User '{request.ApproverOrRecipientUserId}' does not exist.");

        // Decision steps route to a named Reviewer - verify they currently hold clearance for this country
        // and org level before we let the Editor name them (docs/ARCHITECTURE.md §4.2, enforcement point
        // "Submit a step"). Information steps just need a real recipient - no clearance requirement.
        if (step.StepType == StepType.Decision &&
            !target.HasClearance(package.CountryCode, UserRole.CountryReviewer, step.OrgLevel))
        {
            throw new BusinessRuleValidationException(
                $"User '{request.ApproverOrRecipientUserId}' does not hold Country Reviewer clearance for " +
                $"country '{package.CountryCode}' at org level '{step.OrgLevel}', and cannot be named as approver.");
        }

        step.Submit(submittedBy, request.ApproverOrRecipientUserId);
        package.AdvanceIfCurrentStepCompleted(); // no-op unless this was a self-completing Information step

        var action = step.StepType == StepType.Decision ? "StepSubmitted" : "StepDistributed";
        _auditLog.Add(new AuditLogEntry(package.Id, step.Id, submittedBy, action,
            $"{{\"approverOrRecipientId\":\"{request.ApproverOrRecipientUserId}\"}}"));

        DispatchDomainEvents(package);
        await _unitOfWork.SaveChangesAsync(ct);

        var dto = DtoMapper.ToDto(step);
        if (cacheKey is not null) _idempotency.StoreResponse(cacheKey, dto);
        return dto;
    }

    public async Task<ApprovalStepDto> DecideStepAsync(
        Guid packageId, int stepOrder, Guid deciderId,
        StepDecisionRequest request, string? idempotencyKey, CancellationToken ct)
    {
        var cacheKey = BuildIdempotencyKey("decide", packageId, stepOrder, idempotencyKey);
        if (cacheKey is not null && _idempotency.TryGetResponse<ApprovalStepDto>(cacheKey, out var cached) && cached is not null)
            return cached;

        var package = await GetPackageOrThrowAsync(packageId, ct);
        var step = package.GetStep(stepOrder);

        var decider = await _users.GetByIdAsync(deciderId, ct)
            ?? throw new UnauthorizedStepActionException("Caller is not a recognized user.");

        // Re-checked here (not just by the API's authorization handler) because clearance can have changed
        // between submission and decision - the exact staleness scenario discussed in docs/ARCHITECTURE.md §4.3.
        if (!decider.HasClearance(package.CountryCode, UserRole.CountryReviewer, step.OrgLevel))
        {
            throw new UnauthorizedStepActionException(
                $"Caller no longer holds Country Reviewer clearance for country '{package.CountryCode}' at org level '{step.OrgLevel}'.");
        }

        string action;
        if (request.Decision == StepDecision.Approve)
        {
            step.Approve(deciderId, request.Comment);
            package.AdvanceIfCurrentStepCompleted();
            action = "StepApproved";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Comment))
                throw new InvalidStepStateException("A comment is required when returning a step for revision.");
            step.Return(deciderId, request.Comment);
            action = "StepReturnedForRevision";
        }

        _auditLog.Add(new AuditLogEntry(package.Id, step.Id, deciderId, action,
            request.Comment is null ? null : $"{{\"comment\":{System.Text.Json.JsonSerializer.Serialize(request.Comment)}}}"));

        DispatchDomainEvents(package);
        await _unitOfWork.SaveChangesAsync(ct);

        var dto = DtoMapper.ToDto(step);
        if (cacheKey is not null) _idempotency.StoreResponse(cacheKey, dto);
        return dto;
    }

    private async Task<CountryPackage> GetPackageOrThrowAsync(Guid packageId, CancellationToken ct) =>
        await _packages.GetByIdAsync(packageId, ct) ?? throw new NotFoundException(nameof(CountryPackage), packageId);

    private void DispatchDomainEvents(CountryPackage package)
    {
        foreach (var evt in package.DomainEvents)
            _outbox.Enqueue(evt);
        package.ClearDomainEvents();
    }

    private static string? BuildIdempotencyKey(string operation, Guid packageId, int stepOrder, string? rawKey) =>
        string.IsNullOrWhiteSpace(rawKey) ? null : $"{operation}:{packageId}:{stepOrder}:{rawKey}";
}
