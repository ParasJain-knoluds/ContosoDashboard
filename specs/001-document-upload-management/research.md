# Research: Document Upload and Management

## Decision: File storage location and path pattern

- **Decision**: Store files under `AppData/uploads/{userId}/{projectId|"personal"}/{guid}.{ext}` on
  local disk, outside `wwwroot`. Compute the full path first, then write the file, then insert the
  DB record (path-first ordering avoids orphaned/duplicate-key rows).
- **Rationale**: Stakeholder doc explicitly mandates this exact pattern; keeps files inaccessible
  via static file serving (security), and the path shape is directly reusable as an Azure Blob name
  later.
- **Alternatives considered**: Storing files as DB blobs (rejected — bloats SQLite file, slow for
  25 MB files); storing directly under `wwwroot` (rejected — violates Principle II / FR-008).

## Decision: Storage abstraction

- **Decision**: Define `IFileStorageService` (`UploadAsync`, `DeleteAsync`, `DownloadAsync`,
  `GetUrlAsync`) with a `LocalFileStorageService` implementation using `System.IO`, registered via DI.
- **Rationale**: Matches stakeholder's cloud-migration design; lets a future
  `AzureBlobStorageService` be swapped in via configuration without touching `DocumentService` or UI.
- **Alternatives considered**: Calling `System.IO` directly from `DocumentService` (rejected — couples
  business logic to storage medium, blocks future migration, harder to test).

## Decision: Malware scanning approach

- **Decision** (from clarify session): Define `IVirusScanner` with a `StubVirusScanner` that always
  reports files clean; documented in code as a training placeholder for a real AV engine.
- **Rationale**: App must remain offline with no external dependencies (constitution); a real AV
  engine is out of scope for training, but FR-006 still requires the scanning *step* to exist so
  learners see the pattern and swap point.
- **Alternatives considered**: Skipping the scan step entirely (rejected — spec explicitly requires
  it as a described step in the upload workflow); integrating a real local AV library (rejected —
  adds a heavy dependency inappropriate for a training app, explicitly declined during clarification).

## Decision: Background virus-scan processing model

- **Decision**: Scanning is asynchronous. Upload flow saves the file and creates the `Document` row
  with `ScanStatus = Scanning` immediately, then enqueues a scan request to an in-process
  `IScanQueue` (a bounded `System.Threading.Channels.Channel<T>`-backed queue). A hosted
  `VirusScanBackgroundService : BackgroundService` (registered via `AddHostedService`) dequeues
  requests, calls `IVirusScanner.ScanAsync`, and updates `ScanStatus` to `Available` or
  `InfectedRemoved` (deleting the file + record on infection). Documents in `Scanning` status are
  hidden from everyone but the uploader.
- **Rationale**: The user requested a background-job design modeled on Azure Functions with a Queue
  Storage trigger. A real Azure Functions/Queue Storage dependency would violate the constitution's
  offline-only, no-new-cloud-dependency rule (Principle I, Additional Constraints), so the in-process
  queue + hosted-service worker reproduces the same *shape* (producer enqueues a message, a
  decoupled worker dequeues and processes it asynchronously) without any network dependency. The
  `IScanQueue`/`IVirusScanner` abstractions are the intended swap points for a real Azure Functions
  Queue Storage trigger + AV engine in a production deployment, mirroring the existing
  `IFileStorageService` → `AzureBlobStorageService` migration pattern.
- **Alternatives considered**: Real Azure Functions + Azure Queue Storage (rejected — requires an
  Azure subscription/connection string, breaking offline training use; explicitly declined by the
  user during conflict resolution); synchronous in-request scanning (rejected — no longer matches
  the user's requirement for background/async processing, though it remains the simplest option and
  is what the original clarify session had chosen before this request); a persisted (SQLite-backed)
  queue table instead of an in-memory channel (viable alternative for surviving app restarts,
  deferred as unnecessary complexity for training-scale, short-lived scan queues per Principle V).

## Decision: "Team" definition for sharing & Team Lead visibility

- **Decision** (from clarify session): A "team" = the members of a given `Project` (via existing
  `ProjectMember`). No new `Team` entity is introduced.
- **Rationale**: Reuses existing `ProjectMember` relationships; avoids introducing a parallel
  grouping concept (Department) that isn't consistently populated; matches how the rest of the app
  scopes collaboration (project-centric).
- **Alternatives considered**: Department-based teams (rejected — `Department` on `User` is optional/
  free text, not authoritative for collaboration); supporting both (rejected — added complexity not
  required by any user story).

## Decision: Document ID and Category typing

- **Decision**: `Document.DocumentId` is `int` (identity); `Document.Category` is a `string` column
  (e.g., `[MaxLength(50)]`), values constrained by the app to the fixed six-item list.
- **Rationale**: Stakeholder doc explicitly requires integer keys (consistency with `User`/`Project`)
  and text-based category storage (simplicity) rather than an enum ordinal.
- **Alternatives considered**: GUID keys (rejected — inconsistent with existing schema); enum-backed
  category column (rejected — explicitly ruled out by stakeholder constraints, harder to read in DB).

## Decision: File download/preview delivery mechanism

- **Decision**: Serve file bytes through an authorized Razor Page handler (e.g.,
  `Pages/DocumentDownload.cshtml.cs`) that loads the `Document` record, checks authorization via
  `DocumentService`, then streams the file via `IFileStorageService.DownloadAsync`. Preview reuses
  the same endpoint with `Content-Disposition: inline` for PDFs/images.
- **Rationale**: Files live outside `wwwroot`, so there is no static endpoint; a Razor Page handler
  fits the existing `Login.cshtml`/`Logout.cshtml` pattern already used for non-Blazor-routable
  concerns (FR-008, FR-028, IDOR protection).
- **Alternatives considered**: A minimal API/controller endpoint (viable alternative, rejected only
  because the codebase currently has no `Controllers/` folder or API routing set up — Razor Page
  handler keeps consistent with existing conventions).

## Decision: Notifications for share/new-project-document

- **Decision**: Extend the existing `NotificationService`/`Notification` model with two new
  notification triggers (document shared with me; new document added to my project) rather than
  building a separate notification pipeline.
- **Rationale**: `Notification` entity and `NotificationService` already exist and are wired into the
  UI (`Notifications.razor`); reusing them avoids duplicate infrastructure (Principle V).
- **Alternatives considered**: A dedicated `DocumentNotification` entity (rejected — unnecessary
  duplication of an existing generic mechanism).

## Open questions

None remaining — all `NEEDS CLARIFICATION` items were resolved during the `/speckit.clarify` session
prior to this plan (see spec.md Clarifications section).
