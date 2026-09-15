# Tasks: Document Upload and Management

**Input**: Design documents from `/specs/001-document-upload-management/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/service-contracts.md](./contracts/service-contracts.md), [quickstart.md](./quickstart.md)

**Tests**: Not included — the feature specification did not request tests or a TDD approach, and the repository has no existing automated test project (per plan.md Technical Context). Validation is via `dotnet build` and the manual scenarios in quickstart.md.

**Organization**: Tasks are grouped by user story (from spec.md) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5)
- All paths are relative to the repository root (`d:\ContosoDashboard`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Minimal configuration needed before any feature code is written

- [x] T001 Add a `DocumentStorage:BasePath` setting (e.g., `"AppData/uploads"`) to ContosoDashboard/appsettings.json and ContosoDashboard/appsettings.Development.json
- [x] T002 [P] Add `AppData/uploads/` to `.gitignore` (create/update repo root `.gitignore`) so locally stored uploaded files are never committed

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Data model, storage/scan abstractions, and service scaffolding that every user story depends on

**⚠️ CRITICAL**: No user story work (Phase 3+) can begin until this phase is complete

- [x] T003 [P] Create `Document` entity in ContosoDashboard/Models/Document.cs with fields `DocumentId`, `Title` (required, max 255), `Description` (optional, max 2000), `Category` (required, max 50, one of the fixed six values), `Tags`, `ProjectId` (nullable FK), `UploadedByUserId`, `UploadedDate`, `FileSizeBytes`, `FileType` (max 255), `FilePath` (max 500), `OriginalFileName` (max 255), `ScanStatus` (max 20, default `"Scanning"`), `LastModifiedDate` per [data-model.md](./data-model.md)
- [x] T004 [P] Create `DocumentShare` entity in ContosoDashboard/Models/DocumentShare.cs with `DocumentShareId`, `DocumentId`, `SharedByUserId`, `SharedWithUserId` (nullable), `SharedWithProjectId` (nullable), `SharedDate` per [data-model.md](./data-model.md)
- [x] T005 [P] Create `DocumentActivityLog` entity in ContosoDashboard/Models/DocumentActivityLog.cs with `DocumentActivityLogId`, `DocumentId`, `UserId`, `ActionType` (max 20: `Upload`/`Download`/`Delete`/`Share`), `Timestamp` per [data-model.md](./data-model.md)
- [x] T006 Add `DbSet<Document>`, `DbSet<DocumentShare>`, `DbSet<DocumentActivityLog>` plus relationships (`OnDelete: Restrict`/`Cascade` as specified) and indexes (`UploadedByUserId`, `ProjectId`, `Category`, `SharedWithUserId`, `SharedWithProjectId`, `DocumentId`, `UserId`, `Timestamp`) to ContosoDashboard/Data/ApplicationDbContext.cs (depends on: T003, T004, T005)
- [x] T007 Generate and apply an EF Core migration for the new `Documents`, `DocumentShares`, `DocumentActivityLogs` tables (depends on: T006)
- [x] T008 [P] Create `IFileStorageService` interface in ContosoDashboard/Services/IFileStorageService.cs with `UploadAsync`, `DeleteAsync`, `DownloadAsync`, `GetUrlAsync` per [contracts/service-contracts.md](./contracts/service-contracts.md)
- [x] T009 Implement `LocalFileStorageService` in ContosoDashboard/Services/LocalFileStorageService.cs using `System.IO`, generating `{userId}/{projectId|"personal"}/{guid}.{ext}` paths under the configured `DocumentStorage:BasePath` outside `wwwroot`, never using caller-supplied filenames (depends on: T001, T008)
- [x] T010 [P] Create `IVirusScanner` interface in ContosoDashboard/Services/IVirusScanner.cs with `ScanAsync(Stream)` per [contracts/service-contracts.md](./contracts/service-contracts.md)
- [x] T011 Implement `StubVirusScanner` in ContosoDashboard/Services/StubVirusScanner.cs that always returns clean, with a one-line comment stating it is a training placeholder for a real AV engine (depends on: T010)
- [x] T012 [P] Create `IScanQueue` interface in ContosoDashboard/Services/IScanQueue.cs with `EnqueueAsync(int documentId)` / `DequeueAsync()` per [contracts/service-contracts.md](./contracts/service-contracts.md)
- [x] T013 Implement `ScanQueue` in ContosoDashboard/Services/ScanQueue.cs backed by a bounded `System.Threading.Channels.Channel<int>`, with a one-line comment stating it stands in for a future Azure Queue Storage queue (depends on: T012)
- [x] T014 Implement `VirusScanBackgroundService` (`BackgroundService`) in ContosoDashboard/Services/VirusScanBackgroundService.cs: dequeue document IDs, download the file via `IFileStorageService`, call `IVirusScanner.ScanAsync`, set `ScanStatus` to `Available` (clean) or delete the file + row and flag for notification (infected), log a `DocumentActivityLog` entry; comment noting it stands in for an Azure Functions Queue Storage trigger (depends on: T003, T009, T011, T013)
- [x] T015 Register `IFileStorageService`→`LocalFileStorageService`, `IVirusScanner`→`StubVirusScanner`, `IScanQueue`→`ScanQueue` (singletons) and `AddHostedService<VirusScanBackgroundService>` in ContosoDashboard/Program.cs, binding the `DocumentStorage` options (depends on: T009, T011, T013, T014)
- [x] T016 Create `DocumentService` skeleton in ContosoDashboard/Services/DocumentService.cs: constructor DI of `ApplicationDbContext`, `IFileStorageService`, `IScanQueue`, `NotificationService`; add private authorization helpers (`IsUploaderAsync`, `IsProjectMemberAsync`, `IsProjectManagerForAsync`, `IsAdministrator`, `IsTeamLeadOnSharedProjectAsync`) reused by all user stories (depends on: T006, T008, T010, T012)

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 - Upload and Categorize a Document (Priority: P1) 🎯 MVP

**Goal**: An employee can upload a supported file with required metadata and see it land in "My Documents" with a "Scanning" status that later becomes available.

**Independent Test**: Upload a valid PDF under 25 MB with a title and category; confirm it appears in "My Documents" as "Scanning" and later transitions to "Available"; confirm an oversized/unsupported file is rejected with no document created.

- [x] T017 [US1] Implement upload validation in ContosoDashboard/Services/DocumentService.cs: reject if `FileSizeBytes` > 26,214,400 bytes or extension/content-type not in the allow-list (PDF, DOC/DOCX, XLS/XLSX, PPT/PPTX, TXT, JPEG, PNG); reject if `Category` is not one of the six fixed values; reject if more than 10 tags or any tag exceeds 50 characters (depends on: T016)
- [x] T018 [US1] Implement `DocumentService.UploadAsync`: run validation (T017) → `IFileStorageService.UploadAsync` → create `Document` row with `ScanStatus = "Scanning"` → `IScanQueue.EnqueueAsync(documentId)` → write a `DocumentActivityLog` (`ActionType = "Upload"`) → return the created `Document` (depends on: T017)
- [x] T019 [P] [US1] Create ContosoDashboard/Pages/Documents.razor with an upload form (file picker using the `@key`/`MemoryStream` pattern from the stakeholder doc, title, description, category dropdown, project dropdown, tag input) and an upload progress indicator
- [x] T020 [US1] Wire the inline `@code` implementation in Documents.razor to `DocumentService.UploadAsync`, showing success/error messages and a "Scanning"/"Available" status badge per document row; this repository uses inline Blazor page code rather than separate code-behind files (depends on: T018, T019)
- [x] T021 [US1] Add client-side + server-side enforcement of the category fixed-list and the 10-tag/50-character tag limits in Documents.razor and DocumentService (depends on: T020)
- [x] T022 [US1] Add a "Documents" nav link/route to ContosoDashboard/Shared/NavMenu.razor pointing at the new page (depends on: T019)

**Checkpoint**: User Story 1 is fully functional and independently testable (upload → Scanning → Available).

---

## Phase 4: User Story 2 - Browse, Search, and Access Documents (Priority: P2)

**Goal**: Users can list/sort/filter their own and project documents, search across permitted documents, and download/preview files they can access.

**Independent Test**: With a few seeded documents (owned and project-associated, all `Available`), verify "My Documents" sort/filter, a project's document view, a cross-field search limited to permitted results, and in-browser preview of a PDF/image.

- [x] T023 [US2] Implement `DocumentService.GetMyDocumentsAsync(userId, filter)` in ContosoDashboard/Services/DocumentService.cs: return the caller's documents (any `ScanStatus`) sortable by title/uploadDate/category/fileSize and filterable by category/project/date range (depends on: T016)
- [x] T024 [US2] Implement `DocumentService.GetProjectDocumentsAsync(projectId, userId)` in ContosoDashboard/Services/DocumentService.cs: verify project membership, return only `ScanStatus == "Available"` documents for non-uploaders (depends on: T016)
- [x] T025 [US2] Implement `DocumentService.SearchAsync(query, userId)` in ContosoDashboard/Services/DocumentService.cs: match title/description/tags/uploader name/project, scoped to documents the caller may access, targeting sub-2-second response at training scale (depends on: T016)
- [x] T026 [US2] Extend inline Documents.razor with a "My Documents" list and search box wired to T023/T025; this repository uses inline `@code` rather than a code-behind file (depends on: T023, T025)
- [x] T027 [US2] Add a project documents section to ContosoDashboard/Pages/ProjectDetails.razor wired to T024, visible to project members (depends on: T024)
- [x] T028 [US2] Create an authorized download/preview handler: ContosoDashboard/Pages/DocumentDownload.cshtml and DocumentDownload.cshtml.cs — `[Authorize]`, checks caller authorization and `ScanStatus == "Available"` (uploader-only otherwise), streams bytes with `Content-Disposition: attachment` (download) or `inline` for PDF/JPEG/PNG (preview), logs `DocumentActivityLog` (`ActionType = "Download"`) (depends on: T016)
- [x] T029 [US2] Wire download/preview buttons in Documents.razor and the ProjectDetails.razor documents section to the T028 endpoint (depends on: T026, T027, T028)

**Checkpoint**: User Story 2 is fully functional and independently testable alongside US1.

---

## Phase 5: User Story 3 - Manage and Share Documents (Priority: P3)

**Goal**: Document owners can edit metadata, replace files, delete documents (or a Project Manager deletes any project document), and share documents with users/project teams with notifications.

**Independent Test**: As an owner, edit a document's metadata, replace its file, share it with another user (who is notified and sees it under "Shared with Me"), then delete it and confirm it disappears everywhere, including the recipient's shared view.

- [x] T030 [US3] Implement `DocumentService.UpdateMetadataAsync(documentId, request, userId)` in ContosoDashboard/Services/DocumentService.cs: owner-only edit of title/description/category/tags, updates `LastModifiedDate` (depends on: T016)
- [x] T031 [US3] Implement `DocumentService.ReplaceFileAsync(documentId, stream, extension, contentType, userId)` in ContosoDashboard/Services/DocumentService.cs: owner-only; save new file via `IFileStorageService.UploadAsync` → update `Document` row (`FilePath`, `FileSizeBytes`, `FileType`, `LastModifiedDate`) → delete old file via `IFileStorageService.DeleteAsync` (save-then-delete ordering) (depends on: T016)
- [x] T032 [US3] Implement `DocumentService.DeleteAsync(documentId, userId)` in ContosoDashboard/Services/DocumentService.cs: allow the uploader or a Project Manager of the document's project; permanently delete the file, `Document` row, related `DocumentShare` rows, and `DocumentActivityLog` rows (depends on: T016)
- [x] T033 [US3] Implement `DocumentService.ShareAsync(documentId, request, userId)` in ContosoDashboard/Services/DocumentService.cs: owner-only; require exactly one of an individual user or a project (team) target; create `DocumentShare` row(s); reject if the document is not yet `Available` (depends on: T016)
- [x] T034 [US3] Add a "Shared with Me" section to ContosoDashboard/Pages/Documents.razor listing documents shared with the current user (individually or via project-team share) (depends on: T033)
- [x] T035 [US3] Add edit-metadata, replace-file, delete, and share dialogs/actions to inline Documents.razor, wired to T030–T033 (depends on: T030, T031, T032, T033)
- [x] T036 [US3] Extend ContosoDashboard/Services/NotificationService.cs (and `Notification` usage) to send an in-app notification to each recipient when `ShareAsync` succeeds (depends on: T033)

**Checkpoint**: User Story 3 is fully functional and independently testable alongside US1/US2.

---

## Phase 6: User Story 4 - Task and Dashboard Integration (Priority: P4)

**Goal**: Documents can be attached to/uploaded from a task detail page (auto-associated with the task's project), and the dashboard shows recent documents and a document count.

**Independent Test**: From a task detail page, upload a new document or attach an existing one and confirm it is associated with the task's project; then confirm the dashboard home page shows the 5 most recent uploads and an accurate document count.

- [x] T037 [US4] Add document attach/upload UI to ContosoDashboard/Pages/Tasks.razor: uploading from this page calls `DocumentService.UploadAsync` with the task's `ProjectId`; attaching an existing document links it to the task's project if not already associated (depends on: T018, T033)
- [x] T038 [US4] Add a "Recent Documents" widget (last 5 uploads for the current user) and a document count summary card to ContosoDashboard/Pages/Index.razor, backed by `DocumentService.GetMyDocumentsAsync` (depends on: T023)
- [x] T039 [US4] Notify project members when a new project document becomes `Available` in VirusScanBackgroundService (depends on: T018)

**Checkpoint**: User Story 4 is fully functional and independently testable alongside prior stories.

---

## Phase 7: User Story 5 - Admin Reporting and Audit (Priority: P5)

**Goal**: Administrators can review activity logs and generate reports on document types, uploaders, and access patterns.

**Independent Test**: As an Administrator, open the reporting view and confirm it shows most-uploaded document types, most active uploaders, and access pattern data consistent with the seeded `DocumentActivityLog` entries.

- [x] T040 [US5] Verify and complete `DocumentActivityLog` writes for all four action types (`Upload` in T018, `Download` in T028, `Delete` in T032, `Share` in T033) in ContosoDashboard/Services/DocumentService.cs (depends on: T018, T028, T032, T033)
- [x] T041 [US5] Implement admin report queries (most-uploaded document types, most active uploaders, document access patterns) in ContosoDashboard/Services/DocumentService.cs (or a dedicated method group), restricted to Administrator callers (depends on: T040)
- [x] T042 [US5] Add an Administrator-only reporting section/page surfacing the T041 reports (depends on: T041)

**Checkpoint**: User Story 5 is fully functional and independently testable alongside prior stories.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [x] T043 [P] Run `dotnet build` from ContosoDashboard/ and resolve any warnings/errors introduced by this feature
- [x] T044 Walk through all scenarios in [quickstart.md](./quickstart.md) end-to-end and fix any discrepancies found

---

## Dependencies & Execution Order

- **Setup (Phase 1)** → **Foundational (Phase 2)**: must complete before any user story phase begins.
- **User Story 1 (Phase 3)**: depends only on Foundational; no dependency on other stories. This is the MVP.
- **User Story 2 (Phase 4)**: depends only on Foundational (reads documents created by any upload path); can be built/tested in parallel with US1 once Foundational is done, though US1 is needed to have real data to browse.
- **User Story 3 (Phase 5)**: depends only on Foundational; needs existing documents (from US1) to operate on.
- **User Story 4 (Phase 6)**: depends on Foundational, T018 (upload), and T023/T033 (list/share) for attach and recent-documents widget.
- **User Story 5 (Phase 7)**: depends on Foundational and the activity-log write points added in US1–US3 (T018, T028, T032, T033).
- **Polish (Phase 8)**: after all desired user stories are complete.

## Parallel Execution Examples

- Within Foundational: T003, T004, T005 (separate model files) can run in parallel; then T008, T010, T012 (separate interface files) can run in parallel.
- Within US1: T019 (Documents.razor markup) can be built in parallel with T017/T018 (DocumentService logic) since they are different files; T020 then integrates both.
- Across stories: once Foundational is done, one developer can work Phase 3 (US1) while another works Phase 4 (US2), since `DocumentService.cs` additions in each phase touch different methods — coordinate merges since it's a shared file.

## Implementation Strategy

- **MVP first**: Complete Phase 1 → Phase 2 → Phase 3 (US1) and stop there for a demonstrable increment — users can upload and categorize documents with async scanning.
- **Incremental delivery**: Add Phase 4 (US2) next for browse/search/download value, then Phase 5 (US3) for management/sharing, then Phase 6 (US4) for task/dashboard integration, then Phase 7 (US5) for admin reporting — each phase is independently testable and shippable per the Independent Test criteria above.
- **Suggested MVP scope**: Phase 1 + Phase 2 + Phase 3 (User Story 1 only).
- [x] T045 Verify final build, migration state, and implementation checklist.