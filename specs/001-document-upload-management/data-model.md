# Data Model: Document Upload and Management

## Document

Represents an uploaded file's metadata and pointer to its stored content.

| Field | Type | Rules |
|---|---|---|
| `DocumentId` | `int` (PK, identity) | System-assigned |
| `Title` | `string` | Required, max 255 |
| `Description` | `string?` | Optional, max 2000 |
| `Category` | `string` | Required; one of: `Project Documents`, `Team Resources`, `Personal Files`, `Reports`, `Presentations`, `Other`; max 50 |
| `Tags` | `string?` | Optional; stored as delimited text or normalized child rows (see `DocumentTag` note below); up to 10 tags, 50 chars each |
| `ProjectId` | `int?` (FK → Project) | Optional; set when document is associated with a project |
| `UploadedByUserId` | `int` (FK → User) | Required; captured automatically at upload time |
| `UploadedDate` | `DateTime` | Required; UTC, captured automatically |
| `FileSizeBytes` | `long` | Required; captured automatically; must be ≤ 25 MB (26,214,400 bytes) at upload validation time |
| `FileType` | `string` | Required; MIME type; `[MaxLength(255)]` (accommodates long Office MIME strings) |
| `FilePath` | `string` | Required; GUID-based relative path (`{userId}/{projectId|"personal"}/{guid}.{ext}`); `[MaxLength(500)]`; never derived from user-supplied filename |
| `OriginalFileName` | `string` | Required; the user's original filename, stored for display purposes only (never used to build `FilePath`); max 255 |
| `ScanStatus` | `string` | Required; one of `Scanning`, `Available`, `InfectedRemoved`; defaults to `Scanning` at creation; `[MaxLength(20)]` |
| `LastModifiedDate` | `DateTime` | Updated on metadata edit or file replace |

**Relationships**:
- `Document` → `User` (many-to-one, `UploadedByUserId`), `OnDelete: Restrict`
- `Document` → `Project` (many-to-one, optional, `ProjectId`), `OnDelete: Restrict`
- `Document` → `DocumentShare` (one-to-many)
- `Document` → `DocumentActivityLog` (one-to-many)

**Validation rules** (enforced in `DocumentService`, mirrored via data annotations where practical):
- Reject if `FileSizeBytes` > 25 MB or `FileType`/extension not in the allow-list (PDF, DOCX/XLSX/PPTX
  and legacy DOC/XLS/PPT, TXT, JPEG, PNG).
- `Category` must be one of the fixed six values (validated in service layer; not a DB-level enum
  per stakeholder constraint).
- Tags: max 10 per document, max 50 characters each (from clarify session).

**Indexes**: `UploadedByUserId`, `ProjectId`, `Category` (for filter/search performance).

**Scan lifecycle**: A `Document` is created with `ScanStatus = Scanning` in the same transaction as
its file save (see FR-018). It is visible only to its uploader while `Scanning`. A background worker
(`VirusScanBackgroundService`, see contracts) transitions it to `Available` (visible per normal
authorization rules) or `InfectedRemoved` (file + row deleted, uploader notified) once the scan
completes. No other state transitions occur outside upload, edit, replace, and delete operations.

## DocumentTag (supporting table, optional normalization)

If tags are stored as a normalized child collection rather than a delimited string:

| Field | Type | Rules |
|---|---|---|
| `DocumentTagId` | `int` (PK, identity) | System-assigned |
| `DocumentId` | `int` (FK → Document) | Required |
| `Tag` | `string` | Required, max 50 |

**Relationships**: `Document` (one) → `DocumentTag` (many), cascade delete with parent `Document`.

*(Implementation may choose a simple delimited `Tags` string on `Document` instead if search
performance over hundreds of documents does not require indexed tag lookups — decision left to
`/speckit.tasks`/implementation given training-scale data volumes.)*

## DocumentShare

Represents a document shared with an individual user or a project's team.

| Field | Type | Rules |
|---|---|---|
| `DocumentShareId` | `int` (PK, identity) | System-assigned |
| `DocumentId` | `int` (FK → Document) | Required |
| `SharedByUserId` | `int` (FK → User) | Required; must be the document's uploader (or a Project Manager for that project) |
| `SharedWithUserId` | `int?` (FK → User) | Set when sharing with an individual user |
| `SharedWithProjectId` | `int?` (FK → Project) | Set when sharing with a project's team (all `ProjectMember`s) — exactly one of `SharedWithUserId`/`SharedWithProjectId` must be set |
| `SharedDate` | `DateTime` | Required; UTC, captured automatically |

**Relationships**:
- `DocumentShare` → `Document` (many-to-one), `OnDelete: Cascade` (deleting a document removes shares)
- `DocumentShare` → `User` (`SharedByUserId`, `SharedWithUserId`), `OnDelete: Restrict`
- `DocumentShare` → `Project` (`SharedWithProjectId`), `OnDelete: Restrict`

**Validation rules**: Exactly one of `SharedWithUserId` / `SharedWithProjectId` must be non-null
(enforced in service layer). Sharing with a project's team resolves to all current `ProjectMember`
rows for that project at query time (not duplicated per-member rows).

**Indexes**: `SharedWithUserId`, `SharedWithProjectId`, `DocumentId`.

## DocumentActivityLog

Audit trail of document-related actions.

| Field | Type | Rules |
|---|---|---|
| `DocumentActivityLogId` | `int` (PK, identity) | System-assigned |
| `DocumentId` | `int` (FK → Document) | Required |
| `UserId` | `int` (FK → User) | Required; who performed the action |
| `ActionType` | `string` | Required; one of `Upload`, `Download`, `Delete`, `Share`; max 20 |
| `Timestamp` | `DateTime` | Required; UTC, captured automatically |

**Relationships**: `DocumentActivityLog` → `Document` (many-to-one), `OnDelete: Cascade` (log rows
are removed if the parent document is permanently deleted, consistent with FR-019's "permanent
deletion, no recovery" rule — reporting/aggregates should be computed before deletion if long-term
audit retention across deletions is needed in a future iteration).

**Indexes**: `DocumentId`, `UserId`, `Timestamp` (for admin reporting queries per FR-027).

## State / lifecycle notes

- Documents have no explicit status field — existence in the `Documents` table = active. Deletion
  is a hard delete (per FR-019 and Out of Scope: no soft delete/trash).
- File replacement (FR-017) updates `FilePath`, `FileSizeBytes`, `FileType`, `LastModifiedDate` on
  the existing `Document` row; the prior file on disk is deleted via
  `IFileStorageService.DeleteAsync` after the new file is successfully saved (same orphan-avoidance
  ordering as initial upload: save new file → update DB → delete old file).
