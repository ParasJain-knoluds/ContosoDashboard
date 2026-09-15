# Quickstart: Document Upload and Management

## Prerequisites

- .NET 9.0 SDK installed
- Repo checked out at `d:\ContosoDashboard`, this feature implemented per [tasks.md](./tasks.md) (once generated/completed)
- No external services required (fully offline)

## Setup

```powershell
cd D:\ContosoDashboard\ContosoDashboard
dotnet build
dotnet run
```

The app starts with SQLite (`ContosoDashboard.db`). This project has no EF Core migration history —
schema is created via `Database.EnsureCreated()` on startup. If `Document`, `DocumentShare`, or
`DocumentActivityLog` schema changes were made, delete the local dev database file so it is
recreated with the new schema on next run:

```powershell
Remove-Item ContosoDashboard.db -ErrorAction SilentlyContinue
```

Log in via `/login` using one of the mock users (e.g., `camille.nicole@contoso.com` — Project Manager).

## Validation Scenarios

### 1. Upload and categorize a document (User Story 1 / P1)

1. Navigate to the Documents page.
2. Choose a PDF under 25 MB, enter a title, select category "Reports".
3. Submit upload.
4. **Expect**: progress indicator shown, success message, document appears in "My Documents" with
   a "Scanning" status and correct title/category/upload date/size/file type (see FR-001–FR-005).
5. Wait briefly (the local background worker processes the queue in-process, typically under a
   couple seconds for a single file).
6. **Expect**: the document's status changes to "Available" without a page reload being required
   (or after a refresh, depending on implementation), and it becomes visible/downloadable to other
   permitted users (e.g., project members) only once "Available" (FR-006, FR-006a).
7. Repeat upload with a file > 25 MB or an unsupported type (e.g., `.exe`).
8. **Expect**: upload rejected with a clear error message; no document record created (FR-002).

### 2. Browse, search, and access documents (User Story 2 / P2)

1. As the uploader, open "My Documents"; sort by upload date, then filter by category.
2. **Expect**: correct sort/filter results (FR-010, FR-011).
3. Upload a document associated with a project you manage; as another project member, open that
   project's document view.
4. **Expect**: the document is visible and downloadable to all project members (FR-012).
5. Search by a tag or the uploader's name from a different (non-project-member) account.
6. **Expect**: no results returned unless the searching user has access (FR-013, FR-028).
7. Preview a PDF or image document in-browser.
8. **Expect**: renders inline without triggering a file download (FR-015).

### 3. Manage and share documents (User Story 3 / P3)

1. As the document owner, edit its title/category/tags.
2. **Expect**: changes reflected immediately everywhere the document is listed (FR-016).
3. Replace the file with a new version.
4. **Expect**: same document record/metadata, new file content served on download (FR-017, FR-018).
5. Share the document with another individual user.
6. **Expect**: recipient gets an in-app notification and sees it under "Shared with Me" (FR-020, FR-021).
7. Share a document with a project's team (per clarified "team" = project members).
8. **Expect**: all current project members can access it via sharing, without duplicate per-member
   rows required in the UI.
9. Delete a document as its owner (with confirmation prompt).
10. **Expect**: document and its file permanently removed from all views, including "Shared with Me"
    for prior recipients (FR-019, Edge Cases).

### 4. Task and dashboard integration

1. Open a task detail page; attach an existing document or upload a new one directly from the task.
2. **Expect**: the document becomes associated with the task's project automatically (FR-022).
3. Return to the dashboard home page.
4. **Expect**: "Recent Documents" widget shows the user's last 5 uploads; a document count appears
   in the summary cards (FR-023).

### 5. Non-functional spot checks

- Upload a ~20–25 MB file and confirm it completes well under 30 seconds on localhost (SC-006).
- With ~50+ seeded documents, confirm "My Documents" loads quickly and a search returns in well
  under 2 seconds (SC-007, SC-008, FR-014).

## Notes

- Malware scanning in this environment runs as an in-process background job (`IScanQueue` +
  `VirusScanBackgroundService`) calling a stub `IVirusScanner` that always reports files clean —
  this is intentional per the Clarifications in [spec.md](./spec.md) and is documented in code as a
  placeholder for a real Azure Functions + Queue Storage pipeline with a real AV engine.
- Documents remain in "Scanning" status (visible only to the uploader) until the background worker
  processes them; do not expect other users to see a newly uploaded document instantly.
- File storage is local disk under `AppData/uploads/...`, outside `wwwroot`; do not expect files to
  be reachable via a direct static URL — they are only served through the authorized download/preview
  endpoint.
