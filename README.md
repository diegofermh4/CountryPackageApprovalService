# Country Package Approval Service

A REST API that runs a Country Package through its fixed approval roadmap - four steps alternating
**Decision** (Editor submits a document, a named Reviewer approves or returns it) and **Information**
(Editor submits, distribution completes the step immediately) - first at Country level, then at Regional
level. Built as the take-home for the World Bank Group ITS Country Engagement Solutions Architect
pre-interview case.

This README covers running and testing the code. For the design itself - data model, component diagram,
service integrations, RBAC approach, and the Azure target architecture this exercise's InMemory/local-disk
substitutions are standing in for - see **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** and the diagrams in
**[docs/diagrams/](docs/diagrams/CountryPackageApprovalService-Architecture.drawio)**.

## Project layout

```
CountryPackageApprovalService.Domain/          Rich domain model. Zero dependencies beyond the BCL.
CountryPackageApprovalService.Application/     Use cases, DTOs, repository/service interfaces. No EF Core.
CountryPackageApprovalService.Infrastructure/  EF Core InMemory, repositories, outbox, seed data, DI wiring.
Country PackageAPI/                            ASP.NET Core Web API: auth, authorization, controllers, Swagger.
CountryPackageApprovalService.Tests/           xUnit: Domain unit tests, an Infrastructure concurrency test,
                                                and WebApplicationFactory integration tests through the real
                                                Api pipeline.
docs/                                          ARCHITECTURE.md and the draw.io source diagrams.
```

Dependencies point inward (Api → Application/Infrastructure → Domain; Infrastructure → Application interfaces,
never the reverse). Domain and Application do not reference EF Core, ASP.NET Core, or any other framework -
persistence and transport are both swappable without touching business logic. See ARCHITECTURE.md §3 for the
full rationale.

## Running it

Prerequisites: **.NET 10 SDK**.

```bash
cd "Country PackageAPI"                     # solution root (next to Country PackageAPI.slnx)
dotnet restore
dotnet build
dotnet run --project "Country PackageAPI"
```

The console output prints the URL (default `http://localhost:5290`). In Development, Swagger UI is served at
the app root (`/swagger`) and opens automatically via `launchSettings.json`.

Run the tests:

```bash
dotnet test
```

The database is EF Core's InMemory provider - there is nothing to install or migrate, and fictional seed data
(see below) is (re)loaded automatically on every startup, since the store only lives for the process's
lifetime.

## Authentication: the `X-User-Id` header

There is no real identity provider wired up for this exercise. Every request must carry an `X-User-Id` header
set to one of the seeded users' GUIDs below; `DevHeaderAuthenticationHandler` resolves it to a `ClaimsPrincipal`
the same way a real OIDC handler would resolve a validated bearer token. **Nothing downstream of that handler -
authorization, controllers, Application, Domain - reads the header directly or knows it exists**; it all works
purely off `ClaimsPrincipal`/`Guid`, so swapping in Microsoft Entra ID (docs/ARCHITECTURE.md §6.3) touches only
`Program.cs`'s scheme registration.

Call `GET /api/v1/test-users` (no auth required) at any time to fetch this same list live, with each user's
current role grants.

| User            | Id                                     | Country role grants                          |
|-----------------|-----------------------------------------|-----------------------------------------------|
| Ana Petrova     | `11111111-0000-0000-0000-000000000001` | RUR Country Editor (Country + Regional)        |
| Marcus Ionescu  | `11111111-0000-0000-0000-000000000002` | RUR Country Reviewer (Country level only)      |
| Elena Kova      | `11111111-0000-0000-0000-000000000003` | RUR Country Reviewer (Regional level only)     |
| Noah Bergman    | `11111111-0000-0000-0000-000000000004` | SOL Country Editor (Country + Regional)        |
| Priya Shah      | `11111111-0000-0000-0000-000000000005` | SOL Country Reviewer (Country + Regional)      |
| Diego Reyes     | `11111111-0000-0000-0000-000000000006` | none - exercises the "authenticated, zero clearance" 403 path |

Countries seeded: `RUR` (Ruritania), `SOL` (Solantis), `VEG` (Vega, no users granted against it). All fictional,
per the brief.

In Swagger UI, click **Authorize**, paste a user id into the `DevHeader` value, and every request from the UI
carries that identity until you change it.

## Walking through the workflow

`Country PackageAPI/Country PackageAPI.http` has the full sequence as ready-to-run requests (create → upload →
submit → approve, through both Decision steps and both Information steps, plus a few RBAC failure examples) -
open it in an editor with REST Client support, or just follow the same sequence in Swagger UI:

1. `POST /api/v1/country-packages` as an Editor - creates the four-step roadmap instance for a country.
2. `POST /api/v1/country-packages/{id}/steps/{stepOrder}/document` - Editor attaches a document to a Decision step.
3. `POST /api/v1/country-packages/{id}/steps/{stepOrder}/submit` - Editor submits, naming the Reviewer (Decision)
   or recipient (Information - completes immediately).
4. `POST /api/v1/country-packages/{id}/steps/{stepOrder}/decision` - the named Reviewer approves or returns the
   step (a comment is required to return it).
5. `GET /api/v1/country-packages/{id}` and `GET /api/v1/country-packages/{id}/audit-log` to inspect state and history.

## Notable design decisions

Full rationale for each of these is in ARCHITECTURE.md; this is the short version.

- **Rich domain model, not an anemic one.** Every state transition (`Submit`, `Approve`, `Return`,
  `AttachDocument`) is a guarded method on `ApprovalStep` itself, not logic scattered across a service. Package
  `Status` is derived from its steps, never stored, so it cannot drift out of sync.
- **RBAC is re-read, never cached on the token.** `UserCountryRole` (role × country × org level) is the single
  source of truth. The Api layer runs a coarse resource-based check (`CountryRoleAuthorizationHandler`) before
  calling Application; Application re-checks the *named approver's* clearance at both submission and decision
  time, since it can change in between; Domain enforces "only the named approver may act" as a last line of
  defense. Three layers, each catching what the others structurally cannot. In the Azure target architecture
  this sits alongside a coarse Entra ID App Role check (docs/ARCHITECTURE.md §4.1, §6.3) - not implemented here,
  since it needs a real Entra ID tenant to be meaningful.
- **Idempotency-Key on both step-transition endpoints.** A retried `submit`/`decision` request with the same
  key returns the original result instead of re-executing (and instead of a spurious 409 from hitting a state
  guard on the replay).
- **Document snapshot immutability.** Once a Decision step is approved it locks (`IsLocked`); every version
  ever attached to it stays exactly as uploaded. A return-for-revision keeps the prior version and appends a
  new one rather than replacing it.
- **Optimistic concurrency** via an EF Core `RowVersion` token on `CountryPackage` and `ApprovalStep`, so two
  callers racing to act on the same package get a `409` instead of a silently lost update.
- **Transactional outbox**, not a direct publish. Domain events are written to an `OutboxMessages` table in the
  same `SaveChanges` transaction as the state change that raised them, then dispatched (logged here; Azure
  Service Bus in the target architecture) by a background poller - a subscriber outage never blocks an approval.

## Testing strategy

- **Domain unit tests** (`Tests/Domain/`) exercise `ApprovalStep`'s state machine, `CountryPackage`'s derived
  status/advancement, and `User`'s RBAC checks directly - no database, no HTTP, fast and deterministic.
- **An Infrastructure test** (`Tests/Infrastructure/ConcurrencyTests.cs`) drives two independent `DbContext`
  instances against the same row to force a genuine optimistic-concurrency conflict and confirm it surfaces as
  `ConcurrencyConflictException` - deliberately not an HTTP-level test, since racing two real requests
  in-process is flaky by nature.
- **Integration tests** (`Tests/Integration/`), via `WebApplicationFactory<Program>`, run the full stack -
  authentication, authorization, controllers, Application, Infrastructure - for the happy path (all four steps
  to completion, with audit trail assertions), RBAC (401/403/422 across role, country, and org-level
  boundaries), idempotency-key replay, document-lock immutability, and the return-for-revision version history.

## Out of scope / explicitly deferred

- **Real authentication.** See "Authentication" above - swapping in Entra ID is a `Program.cs`-only change by
  design; the full App Registration / App Role / Security Group design is in ARCHITECTURE.md §6.3.
- **RBAC administration** (granting/revoking `UserCountryRole`s at runtime) - the brief's core operations are
  about the approval workflow itself; roles are seeded fixtures here.
- **Notification/CPF-CPIA-reporting integrations** - covered as illustrative, out-of-scope Azure Functions in
  the architecture diagrams and doc, not implemented.
- **A real document store and relational database** - `LocalDiskDocumentStore` and EF Core InMemory stand in
  for Azure Blob Storage and Azure SQL; both are isolated behind interfaces the rest of the solution doesn't
  know are being swapped (ARCHITECTURE.md §6.4, §9).
- **Infrastructure as Code.** No Terraform in this repo - the take-home ships application code only. The
  module-based Terraform strategy (private-by-default endpoints, no public network access, least-privilege
  Managed Identities baked into each module) that provisions the Azure target architecture is documented in
  ARCHITECTURE.md §6.8.
