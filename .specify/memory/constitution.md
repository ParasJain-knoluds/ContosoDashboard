<!--
Sync Impact Report
- Version change: (none, template) → 1.0.0
- Modified principles: n/a (initial ratification, all principles newly defined)
- Added sections: Core Principles (I-V), Additional Constraints, Development Workflow, Governance
- Removed sections: none
- Templates requiring updates: .specify/templates/plan-template.md (⚠ verify Constitution Check gates
  reference these principles), .specify/templates/spec-template.md (✅ no direct dependency),
  .specify/templates/tasks-template.md (✅ no direct dependency)
- Follow-up TODOs: TODO(RATIFICATION_DATE) — original ratification date unknown; set to today's date
  as the effective adoption date since no prior constitution existed.
-->

# ContosoDashboard Constitution

## Core Principles

### I. Training-Purpose Transparency
ContosoDashboard exists solely for teaching Spec-Driven Development; it is NOT a production
system. Every feature, doc, and PR MUST clearly preserve this framing: no claims of
production-readiness, no removal of the training disclaimers in the README, and no introduction
of real external service integrations (cloud, payment, third-party APIs) that would break the
offline, self-contained nature of the training environment. Rationale: learners and instructors
rely on the repo staying reproducible and safe to run without external accounts or costs.

### II. Secure-by-Default Development (NON-NEGOTIABLE)
Even though authentication is mocked, all code MUST follow secure coding practices consistent
with OWASP Top 10 guidance: enforce `[Authorize]` on protected pages, validate and scope all
data access to the authenticated user (no IDOR), never trust client-supplied identifiers for
authorization decisions, and keep security headers (CSP, X-Frame-Options, etc.) intact. New
features MUST NOT weaken existing defense-in-depth (middleware, page-level, and service-level
checks). Rationale: the project explicitly teaches good security habits despite mock auth, and
regressions here undermine its educational value.

### III. Spec-Driven Change Process
No non-trivial feature work proceeds without a spec, plan, and task breakdown produced via the
Spec Kit workflow (`/speckit.specify` → `/speckit.plan` → `/speckit.tasks` → `/speckit.implement`).
Direct ad-hoc implementation of multi-step features without these artifacts is disallowed except
for trivial fixes (typos, formatting, small bug fixes under ~10 lines). Rationale: the repository's
purpose is to demonstrate and practice SDD; skipping it defeats the training goal.

### IV. Consistent, Testable Architecture
Code MUST follow the existing Blazor Server + ASP.NET Core layering already established in the
repo: `Models/` for data entities, `Services/` for business logic (injected via DI), `Pages/` and
`Shared/` for UI, `Data/` for persistence via `ApplicationDbContext`. New services MUST be
interface-driven where practical and independently testable. Business logic MUST NOT be embedded
directly in `.razor` code-behind when it can live in a `Services/` class. Rationale: keeps the
codebase approachable for learners and enables meaningful contract/unit testing.

### V. Simplicity and Documented Limitations
Prefer the simplest solution that satisfies the spec; avoid speculative abstractions, extra
libraries, or infrastructure not required by the current feature. Known limitations (mock auth,
offline-only, no cloud integration) MUST remain explicit in documentation rather than silently
patched over. Rationale: YAGNI keeps the training codebase readable; honest documentation of
limitations is itself part of the lesson.

## Additional Constraints

- Technology stack is fixed to ASP.NET Core (Blazor Server) targeting the .NET version already
  configured in `ContosoDashboard.csproj`; do not introduce alternate front-end frameworks.
- No new external/cloud service dependencies (databases, auth providers, APIs) may be added;
  the app must remain runnable fully offline for training availability.
- Mock authentication (`CustomAuthenticationStateProvider`, cookie-based sign-in via
  `Login.cshtml`) must remain the sole auth mechanism unless a spec explicitly proposes and
  justifies replacing it.

## Development Workflow

- All feature work follows the Spec Kit lifecycle: specify → clarify (if needed) → plan → tasks →
  implement, with `/speckit.analyze` recommended before implementation on non-trivial features.
- Every PR touching `Services/` or `Pages/*.razor.cs` that changes access control MUST be reviewed
  against Principle II (Secure-by-Default Development) explicitly in the PR description.
- Build must pass (`dotnet build`) before a task is considered complete.

## Governance

This constitution supersedes ad-hoc practices for this repository. Amendments require:
1. A documented rationale for the change (what problem it solves).
2. An update to this file following the Sync Impact Report format at the top of this document.
3. A version bump per semantic versioning: MAJOR for incompatible principle removals/redefinitions,
   MINOR for new principles or materially expanded guidance, PATCH for clarifications/wording fixes.

All PRs and feature plans MUST verify compliance with these principles (see Constitution Check
sections in `.specify/templates/plan-template.md`). Complexity or deviations MUST be justified in
the plan's Complexity Tracking section. Use this constitution as the top-level authority; where it
is silent, defer to repository conventions documented in `README.md`.

**Version**: 1.0.0 | **Ratified**: TODO(RATIFICATION_DATE): original adoption date unknown | **Last Amended**: 2026-09-13
