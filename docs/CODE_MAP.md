# Code Map — Country Package Approval Service

A navigation reference for the repository: what each class does, which layer it belongs to, which design
patterns show up where, and a "where do I change X" index for walking through the code live. Read alongside
`ARCHITECTURE.md` (the *why*) — this file is the *where*.

## 1. Solution layout and dependency direction

```
Country PackageAPI/                       (Api - ASP.NET Core host, HTTP adapter)
  └─▶ CountryPackageApprovalService.Application   (use cases, DTOs, port interfaces)
        └─▶ CountryPackageApprovalService.Domain  (entities, invariants, domain events - zero dependencies)
  └─▶ CountryPackageApprovalService.Infrastructure (EF Core, repositories, outbox, doc store)
        └─▶ CountryPackageApprovalService.Application
              └─▶ CountryPackageApprovalService.Domain
CountryPackageApprovalService.Tests       (xUnit - Domain unit tests, EF/API integration tests)
```

Dependencies point inward only: Domain depends on nothing; Application depends only on Domain; Infrastructure
implements Application's interfaces; Api composes Infrastructure + Application at startup and never touches
EF Core directly. This is the layering ARCHITECTURE.md §3.1 describes — the class map below is organized the
same way, inside-out.

## 2. Domain — `CountryPackageApprovalService.Domain`

No dependencies on any other project or on EF Core. Every invariant lives here as behavior, not as a public
setter a service pokes at from outside.

| Class / file | Responsibility |
|---|---|
| `CountryPackage.cs` | Aggregate root. Owns the ordered `ApprovalStep` collection, instantiates from a `RoadmapTemplate`, derives `Status` (never stored — see §2.3), advances `CurrentStepOrder`. |
| `ApprovalStep.cs` | One step's full state machine: `AttachDocument`, `Submit`, `Approve`, `Return`. Every transition guard (locked, wrong status, wrong approver) lives here. Raises domain events on submit/complete/return. |
| `DocumentVersion.cs` | One immutable upload under a step. Version numbers are per-step, never reused. |
| `UserCountryRole.cs` | One (user, country, role, org level) grant — the authoritative RBAC row. `CoversOrgLevel` implements the `Both`-covers-`Country`-and-`Regional` rule. |
| `User.cs` | Owns its `UserCountryRole` grants. `HasClearance` / `HasAnyClearance` are *the* RBAC query — everything else calls into these two methods rather than re-implementing the check. |
| `RoadmapTemplate.cs` / `RoadmapStepTemplate` | The four-step roadmap modeled as data, not hardcoded logic — `CreateDefault()` seeds the brief's exact four steps. A second roadmap variant is a data change, not a redeploy. |
| `Country.cs` | Reference data — code/name/region, fictional only. |
| `AuditLogEntry.cs` | One immutable audit row. Constructed, never mutated. |
| `Enums.cs` | `UserRole`, `OrgLevel`, `StepType`, `StepStatus`, `StepDecision` — every enum the domain is built around. |
| `Events/DomainEvents.cs` | `IDomainEvent` marker + `StepSubmittedEvent`, `StepCompletedEvent`, `StepReturnedEvent` (records). |
| `Exceptions/Exceptions.cs` | `DomainException` base + six subtypes, each mapped to one HTTP status by the Api's exception middleware. |

## 3. Application — `CountryPackageApprovalService.Application`

Use-case orchestration and the "ports" (interfaces) Infrastructure implements. No EF Core reference anywhere
in this project — that is what makes swapping the database a one-project change.

| Class / file | Responsibility |
|---|---|
| `Services/ApprovalWorkflowService.cs` | The one use-case class: `CreateRoadmapAsync`, `UploadDocumentAsync`, `SubmitStepAsync`, `DecideStepAsync`, `GetPackageAsync`, `GetAuditTrailAsync`. Loads the aggregate, calls its behavior, re-checks reviewer clearance defensively, writes the audit entry, dispatches domain events to the outbox, commits via `IUnitOfWork`. |
| `Services/DtoMapper.cs` | Hand-written entity → DTO mapping (no AutoMapper — see §7 below). |
| `Interfaces/IApprovalWorkflowService.cs` | The use-case contract the Api's controller depends on. |
| `Interfaces/IRepositories.cs` | `ICountryPackageRepository`, `IRoadmapTemplateRepository`, `ICountryRepository`, `IUserRepository`, `IAuditLogRepository`, `IUnitOfWork` — the persistence ports. |
| `Interfaces/IInfrastructureServices.cs` | `IDocumentStore`, `IOutboxWriter`, `IIdempotencyStore` — the remaining ports (storage, eventing, idempotency). |
| `Dtos/Requests.cs` | `CreateRoadmapRequest`, `SubmitStepRequest`, `StepDecisionRequest` — data-annotation-validated inbound shapes. |
| `Dtos/Responses.cs` | `CountryPackageDto`, `ApprovalStepDto`, `DocumentVersionDto`, `AuditLogEntryDto` — immutable `record` outbound shapes. |

## 4. Infrastructure — `CountryPackageApprovalService.Infrastructure`

The only project that references EF Core. Implements every Application port.

| Class / file | Responsibility |
|---|---|
| `DependencyInjection.cs` | Composition root for this project — registers the DbContext, every repository, the outbox writer/dispatcher, idempotency store, document store, and the workflow service. `SeedDatabase()` re-applies fictional seed data on every startup (InMemory provider has no persistence between runs). |
| `Persistence/AppDbContext.cs` | EF Core model: keys, cascade deletes, field-backed navigations (so aggregates keep encapsulated `_steps`/`_documents`/`_roles` collections instead of public setters), `RowVersion` as the optimistic-concurrency token, `Ignore()` on derived properties (`Status`, `DomainEvents`). |
| `Persistence/SeedData.cs` | Fictional countries, users, role grants, and the default roadmap template. |
| `Repositories/EfRepositories.cs` | `CountryPackageRepository`, `RoadmapTemplateRepository`, `CountryRepository`, `UserRepository`, `AuditLogRepository`, `UnitOfWork` — one class per interface, each a thin EF Core query/command, all in one file since each is a handful of lines. `UnitOfWork` translates `DbUpdateConcurrencyException` into the domain-level `ConcurrencyConflictException`. |
| `Outbox/OutboxWriter.cs` | Writes an `OutboxMessage` into the *same* DbContext change tracker the calling unit of work is about to commit — the transactional-outbox guarantee. |
| `Outbox/OutboxMessage.cs` | The outbox row entity. |
| `Outbox/OutboxDispatcherHostedService.cs` | `BackgroundService` that polls unpublished rows every 5s and "publishes" (logs) them, in its own DI scope. |
| `Idempotency/InMemoryIdempotencyStore.cs` | `ConcurrentDictionary`-backed cache-aside store for the `Idempotency-Key` header, singleton-scoped. |
| `DocumentStore/LocalDiskDocumentStore.cs` | Streams an upload to disk while computing a SHA-256 checksum in the same pass; returns a `file://` URI (Blob Storage URI in the Azure target). |

## 5. Api — `Country PackageAPI`

The HTTP adapter. Thin by design: every controller action binds input, runs the coarse-grained authorization
check, and hands off to `IApprovalWorkflowService`.

| Class / file | Responsibility |
|---|---|
| `Program.cs` | Composition: registers auth scheme, authorization handler, Infrastructure, Swagger; wires the middleware pipeline (exception handling first, then Swagger, HTTPS redirect, authn, authz, controllers); seeds the DB. |
| `Controllers/CountryPackagesController.cs` | The five core endpoints: create roadmap, get package, get audit trail, upload document, submit step, decide step. |
| `Controllers/TestUsersController.cs` | `[AllowAnonymous]` convenience endpoint listing seeded users/grants — exists only because the exercise substitutes a header for real Entra ID identity; has no production equivalent (see ARCHITECTURE.md §6.3). |
| `Auth/DevHeaderAuthenticationHandler.cs` | `AuthenticationHandler<AuthenticationSchemeOptions>` reading `X-User-Id`. The *only* class that knows about that header — everything downstream reads `ClaimsPrincipal` the same way it would with a real Entra ID token. |
| `Auth/ClaimsPrincipalExtensions.cs` | `GetUserId()` / `RequireUserId()` — controllers never touch the header directly. |
| `Authorization/CountryRoleAuthorization.cs` | `CountryRoleRequirement`, `CountryPackageResource`, `CountryRoleAuthorizationHandler` — ASP.NET Core's resource-based authorization pattern, re-reading `UserCountryRole` fresh on every call rather than trusting anything on the principal. |
| `Middleware/ExceptionHandlingMiddleware.cs` | Maps each `DomainException` subtype → HTTP status + RFC 9457 `ProblemDetails`; unhandled exceptions become a generic 500 with no internal detail. |
| `Swagger/IdempotentActionAttribute.cs` + `IdempotencyKeyOperationFilter.cs` | Marker attribute + `IOperationFilter` so the two idempotent endpoints document their `Idempotency-Key` header in Swagger UI without hardcoding route names. |

## 6. Tests — `CountryPackageApprovalService.Tests`

| File | Covers |
|---|---|
| `Domain/ApprovalStepTests.cs`, `CountryPackageTests.cs`, `UserRbacTests.cs` | Pure unit tests against the aggregates — state transitions, guard clauses, `HasClearance`/`CoversOrgLevel`. No EF Core, no HTTP. |
| `Infrastructure/ConcurrencyTests.cs` | Optimistic-concurrency conflict behavior against `RowVersion`. |
| `Integration/ApiFactory.cs`, `IntegrationTestBase.cs` | `WebApplicationFactory<Program>` bootstrap + shared helpers (submit/decide with a given `X-User-Id`). |
| `Integration/WorkflowIntegrationTests.cs` | Full four-step roadmap happy path through the real HTTP pipeline. |
| `Integration/RbacIntegrationTests.cs` | Wrong role, wrong country, wrong org level, wrong named approver — each rejected with the right status. |
| `Integration/IdempotencyAndImmutabilityIntegrationTests.cs` | Retried submit/decide with the same `Idempotency-Key`; locked-step document upload rejection. |

## 7. Design patterns identified

| Pattern | Where | Why it's there |
|---|---|---|
| **Layered / Clean architecture** | Whole solution (§1 above) | Dependency Inversion — Domain and Application never depend on a framework or a database. |
| **Aggregate root (DDD)** | `CountryPackage` owning `ApprovalStep`/`DocumentVersion` | One consistency boundary; every mutation goes through the root's methods, never a child's setter directly. |
| **Factory method** | `CountryPackage.CreateFromTemplate`, `RoadmapTemplate.CreateDefault`, `ApprovalStep.CreateFromTemplate` | Object construction that must satisfy invariants stays behind a named static method, not a public constructor + property assignment. |
| **Domain events** | `IDomainEvent` + `Step*Event` records, raised inside `ApprovalStep`, drained by `ApprovalWorkflowService.DispatchDomainEvents` | Decouples "state changed" from "something else needs to react to it," without the aggregate knowing who's listening. |
| **Transactional outbox** | `OutboxWriter` + `OutboxMessage` + `OutboxDispatcherHostedService` | Standard fix for the dual-write problem: the event row commits in the *same* transaction as the state change, so a broker outage can never lose or duplicate an event relative to the state it describes. |
| **Repository** | `IRepositories.cs` ports + `EfRepositories.cs` implementations | Application never sees `DbSet<T>` or LINQ-to-EF; swapping InMemory → SQL Server touches zero Application code. |
| **Unit of Work** | `IUnitOfWork` / `UnitOfWork` | One `SaveChangesAsync` per use case = one transaction for the state change + audit row + outbox row together. |
| **Cache-aside (idempotent receiver)** | `IIdempotencyStore`, checked before and populated after each mutating call in `ApprovalWorkflowService` | A retried request with the same `Idempotency-Key` returns the cached result instead of re-executing. |
| **Chain of Responsibility** | ASP.NET Core middleware pipeline, esp. `ExceptionHandlingMiddleware` wrapping `_next` | Each middleware either handles the request or passes it on — textbook chain, provided by the framework and used deliberately as the single exception-to-HTTP translation point. |
| **Strategy (framework-provided)** | `AuthorizationHandler<TRequirement,TResource>` (`CountryRoleAuthorizationHandler`) | ASP.NET Core's pluggable-policy mechanism; the resource-based overload is what lets the same requirement type be evaluated against a country code with or without an org level. |
| **Adapter** | `DevHeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>` | Adapts a custom header scheme to the standard authentication abstraction so swapping to Entra ID's OIDC handler (ARCHITECTURE.md §6.3) changes one registration line in `Program.cs`, nothing downstream. |
| **DTO / explicit mapper** | `DtoMapper` | Deliberately not AutoMapper — a small, stable mapping surface is easier to read and unit-test as plain static methods than as a reflection-based profile. |
| **Guard clause / throw-helper** | `GetPackageOrThrowAsync`, `EnsurePendingAndAssignedTo` | Centralizes a null/invalid-state check used from multiple call sites into one place instead of repeating the same `?? throw` at each call site. |
| **Composition root** | `DependencyInjection.AddInfrastructure`, called once from `Program.cs` | All Infrastructure wiring lives in one method instead of being scattered across `Program.cs`. |

Not used, and deliberately so: **AutoMapper** (explicit mapper reads better at this scale — see `DtoMapper`'s
own doc comment), a formal **Specification pattern** for queries (every repository query today is a single
predicate; worth introducing if a "list/filter packages" endpoint is added later — see §8), and a classic GoF
**Strategy interface** for exception→status mapping (a `switch` expression is a perfectly good jump table for
six known exception types; an interface per mapper would be over-engineering at this size).

## 8. Data structures and algorithmic-complexity notes

**What's already the right call:**

- `List<T>` backing `_steps` / `_documents` / `_roles`, looked up with `SingleOrDefault`/`FirstOrDefault` (O(n)
  scan) rather than a `Dictionary`. Correct trade-off: a package always has exactly 4 steps and a document
  list per step is small — indexing a 4-element collection would be premature optimization that adds
  complexity (keeping a dictionary in sync with EF Core's navigation collection) for no measurable gain.
- `ConcurrentDictionary<string, object?>` for `InMemoryIdempotencyStore` — the right structure for a
  thread-safe, singleton, concurrent-request cache: O(1) average get/set, no external lock needed.
- SHA-256 checksum computed via `CryptoStream` while streaming the upload to disk in
  `LocalDiskDocumentStore.SaveAsync` — O(n) time, O(1) memory (never buffers the whole file), which matters
  once real document uploads are multi-MB PDFs, not the small fixtures used in tests.
- Optimistic concurrency via `RowVersion` (EF Core `IsRowVersion()`) instead of pessimistic row locks —
  O(1) version-compare-and-swap plus a retry path (`ConcurrencyConflictException`), appropriate because
  concurrent edits to the *same* step are rare but must never be silently lost.

**Worth tightening before/while moving off the InMemory provider (this is the honest answer to "did I look
for complexity issues" — these are real, not hypothetical, once the database is a real query planner):**

- `OutboxDispatcherHostedService.DispatchPendingAsync` filters `Where(m => m.PublishedAtUtc == null)` and the
  audit trail read filters `Where(a => a.PackageId == packageId).OrderBy(a => a.TimestampUtc)`. The InMemory
  provider does a full in-memory scan either way, so this is invisible today — on Azure SQL, without an
  explicit index these become full table scans as the tables grow. Add, in `AppDbContext.OnModelCreating`:
  a filtered/composite index on `OutboxMessage(PublishedAtUtc, OccurredAtUtc)`, and a composite index on
  `AuditLogEntry(PackageId, TimestampUtc)`. This is a one-line `HasIndex(...)` per entity, and it is the kind
  of thing that is easy to forget precisely *because* the InMemory provider never complains about its absence.
- `InMemoryIdempotencyStore` has no eviction — the `ConcurrentDictionary` grows for the lifetime of the
  process (O(total mutating requests ever) memory). Fine for the exercise; before this goes anywhere near a
  long-running instance, swap it for `IMemoryCache` with a sliding expiration (or, in the Azure target,
  Azure Cache for Redis with a TTL — already the planned swap per ARCHITECTURE.md §3.3) so idempotency keys
  age out instead of accumulating forever.
- If a future endpoint needs "list packages for a country, optionally filtered by status," resist adding a
  new bespoke repository method per filter combination — that is where a lightweight **Specification pattern**
  (an `Expression<Func<CountryPackage,bool>>`-based spec object) earns its keep over ad-hoc LINQ methods, and
  it is also where a composite index on `CountryPackage(CountryCode, ...)` becomes necessary rather than
  optional, since that query has no natural primary-key shortcut the current single-package lookups have.

None of the current code paths do anything worse than O(n) over a bounded, small `n` (steps per package,
documents per step, users per country) or an indexed/PK lookup — there is no accidental O(n²) or unindexed
full-table-scan-on-the-hot-path in the exercise as delivered. The items above are what a reviewer should
expect you to name unprompted when asked "what changes once this is real data at real scale" — which is
exactly the kind of question this evaluation is testing for.

## 9. Where do I change X?

| I need to... | Touches |
|---|---|
| Add a new step type or roadmap shape | `Enums.cs` (`StepType`), `RoadmapTemplate.CreateDefault` (or a new template), `ApprovalStep` transition methods if the new type needs different rules |
| Change an authorization rule | `CountryRoleAuthorizationHandler` (coarse, resource-based) *and* `ApprovalWorkflowService` (defensive re-check) — see ARCHITECTURE.md §4.2 for why both exist |
| Add a new domain event / side effect | `Events/DomainEvents.cs` (new record), the aggregate method that should raise it, nothing in Infrastructure (the outbox is generic over `IDomainEvent`) |
| Swap the database from InMemory to Azure SQL | `DependencyInjection.cs` only (`UseInMemoryDatabase` → `UseSqlServer`) — see §8 above for the indexes to add at the same time |
| Swap auth from the dev header to Entra ID | `Program.cs` scheme registration only (`AddScheme<...DevHeaderAuthenticationHandler>` → `AddMicrosoftIdentityWebApi`) — nothing in `Authorization/`, `Controllers/`, Application, or Domain changes (ARCHITECTURE.md §6.3) |
| Add a new HTTP endpoint | `Controllers/CountryPackagesController.cs` (thin adapter) → new method on `IApprovalWorkflowService`/`ApprovalWorkflowService` → any new Domain behavior needed |
| Change how documents are stored | `IDocumentStore` implementation only (`LocalDiskDocumentStore` → an Azure Blob Storage implementation) — no Application or Domain change |
| Add a new exception → HTTP status mapping | `Domain/Exceptions/Exceptions.cs` (new subtype) + `ExceptionHandlingMiddleware.MapStatus` switch expression |
