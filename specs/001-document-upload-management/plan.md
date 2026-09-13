# Implementation Plan: Document Upload and Management

**Branch**: `001-document-upload-management` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-document-upload-management/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Add document upload, organization, search, sharing, and task/dashboard integration to
ContosoDashboard. Employees upload files (PDF/Office/text/images, ≤25 MB) with metadata
(title, category, optional project/tags), browse/search/filter their own and project documents,
preview/download within permission scope, edit metadata, replace file content, delete, and share
documents with individual users or project teams (recipients notified in-app). Files are stored on
local disk outside `wwwroot` behind an `IFileStorageService` abstraction (GUID-based paths, no
user-supplied names) to keep the door open for a future Azure Blob Storage implementation without
schema or business-logic changes. Malware scanning runs asynchronously as a background job: after
upload, the document is saved with a "Scanning" status and a scan request is enqueued to an
in-process `IScanQueue`; a hosted `VirusScanBackgroundService` (`BackgroundService`) dequeues it and
calls the pluggable `IVirusScanner` (a local always-clean stub for training). This queue+worker shape
is a direct, offline-safe stand-in for a future production design using Azure Functions with a
Queue Storage trigger — no code outside the queue/scanner abstractions would need to change to swap
in that implementation. All new functionality follows the existing Blazor Server + EF Core Sqlite
layered architecture (Models/Services/Pages/Data) and enforces role-based, IDOR-safe authorization
at the service layer.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (matches existing `ContosoDashboard.csproj`)
**Primary Dependencies**: ASP.NET Core Blazor Server, Entity Framework Core 9 (Sqlite provider), existing `CustomAuthenticationStateProvider` mock auth
**Storage**: SQLite via `ApplicationDbContext` (EF Core) for metadata; local filesystem (outside `wwwroot`, e.g. `AppData/uploads/{userId}/{projectId|"personal"}/{guid}.{ext}`) for file content, accessed only through a new `IFileStorageService`
**Testing**: No existing automated test project in the repo; validation is via `dotnet build` and manual quickstart scenarios (see quickstart.md). Adding a formal test project is out of scope unless a future spec introduces one.
**Target Platform**: Self-hosted ASP.NET Core web app (Blazor Server), Windows/local training environment, offline
**Project Type**: Single web application (existing `ContosoDashboard` project — no separate frontend/backend split; Blazor Server renders UI and hosts services in one process)
**Background Processing**: In-process `IScanQueue` (bounded `Channel<T>`-backed queue) + `VirusScanBackgroundService : BackgroundService` registered via `AddHostedService`; documented in code as the offline stand-in for a future Azure Functions Queue Storage trigger — no real Azure Queue/Functions dependency is introduced
**Performance Goals**: Search results ≤2s; document list pages ≤2s for up to 500 documents; preview load ≤3s; upload of a 25 MB file completes ≤30s on a typical local/LAN connection; background scan of a queued document completes within a few seconds under training-scale load
**Constraints**: Must run fully offline (no cloud/external services, including no real Azure Functions/Queue Storage); files must live outside `wwwroot`; no user-supplied filenames in storage paths; must not weaken existing auth/authorization defense-in-depth; DocumentId must be an integer key (consistent with `User`/`Project`); Category stored as text, not an enum ordinal; documents in "Scanning" status must be invisible to everyone but the uploader
**Scale/Scope**: Training-scale data (~tens of users, hundreds of documents per user max per spec's SC-007); single-node deployment, no horizontal scaling requirements

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Training-Purpose Transparency | No new cloud/external services introduced; `IFileStorageService`/`IVirusScanner` stubs and the in-process `IScanQueue`/`VirusScanBackgroundService` keep the app offline while documenting a future Azure Functions + Queue Storage migration path (explicitly chosen over a real Azure dependency per user confirmation). | PASS |
| II. Secure-by-Default Development (NON-NEGOTIABLE) | All document endpoints/pages require `[Authorize]`; access is scoped and authorization-checked at the service layer (IDOR protection per FR-028); files served only via authorized handlers, never static `wwwroot` paths; GUID-based storage paths prevent path traversal. | PASS |
| III. Spec-Driven Change Process | This feature follows specify → clarify → plan → tasks → implement; spec.md and clarifications already completed. | PASS |
| IV. Consistent, Testable Architecture | New `Document`, `DocumentShare`, `DocumentActivityLog` entities go in `Models/`; `DocumentService`, `LocalFileStorageService` (+`IFileStorageService`), `VirusScanStubService` (+`IVirusScanner`) go in `Services/`, injected via DI; UI logic stays in `Pages/`/`Shared/` calling services, not embedding business logic in code-behind. | PASS |
| V. Simplicity and Documented Limitations | No speculative abstractions beyond what FR-list requires (storage/scan interfaces are explicitly required by the spec/stakeholder doc for migration-readiness); mock-scan and offline-only limitations documented in code comments and quickstart. | PASS |

No violations identified. Complexity Tracking table below is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-upload-management/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
ContosoDashboard/
├── Models/
│   ├── Document.cs              # NEW: Document entity (metadata + file path)
│   ├── DocumentShare.cs         # NEW: sharing relationship (user/project recipient)
│   └── DocumentActivityLog.cs   # NEW: audit log entries (upload/download/delete/share)
├── Services/
│   ├── IFileStorageService.cs   # NEW: storage abstraction (UploadAsync/DeleteAsync/DownloadAsync/GetUrlAsync)
│   ├── LocalFileStorageService.cs # NEW: local-disk implementation (System.IO), outside wwwroot
│   ├── IVirusScanner.cs         # NEW: scan abstraction
│   ├── StubVirusScanner.cs      # NEW: always-clean local stub (documented placeholder)
│   ├── IScanQueue.cs            # NEW: in-process queue abstraction (enqueue/dequeue scan requests)
│   ├── ScanQueue.cs             # NEW: Channel<T>-backed implementation; stand-in for Azure Queue Storage
│   ├── VirusScanBackgroundService.cs # NEW: BackgroundService worker; stand-in for an Azure Functions queue trigger
│   └── DocumentService.cs       # NEW: upload/list/search/share/delete orchestration + authorization
├── Pages/
│   ├── Documents.razor          # NEW: "My Documents" + upload + search/filter UI
│   ├── Documents.razor.cs       # NEW: code-behind delegating to DocumentService
│   ├── DocumentDownload.cshtml(.cs) # NEW: Razor Page/controller-style authorized download/preview endpoint
│   ├── ProjectDetails.razor     # MODIFIED: add project documents section
│   ├── Tasks.razor              # MODIFIED: add document attach/upload from task detail
│   └── Index.razor              # MODIFIED: add "Recent Documents" widget + document count card
├── Data/
│   └── ApplicationDbContext.cs  # MODIFIED: add DbSet<Document>, DbSet<DocumentShare>, DbSet<DocumentActivityLog>, relationships/indexes
└── Services/
    └── NotificationService.cs   # MODIFIED (or extended): add document-share / new-project-document notifications
```

**Structure Decision**: Single existing ASP.NET Core Blazor Server project (`ContosoDashboard/`). No
new projects are introduced — new entities go in `Models/`, new business/storage/scan logic goes in
`Services/` behind interfaces, and UI additions extend existing `Pages/`/`Shared/` following the
established layering (Principle IV). File downloads are served through an authorized Razor
Page/minimal endpoint (not static files) since files live outside `wwwroot`.

## Complexity Tracking

*No Constitution Check violations — this section is not applicable.*
