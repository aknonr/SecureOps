# SecureOps.Ui

Blazor Server + MudBlazor web UI. Hosted on IIS in-process. Phase 2 deliverable.

## Responsibilities

- Render pages for shift engineers, leads, admins, auditors.
- Windows Authentication via Active Directory.
- AD-group-based authorization via policies in `SecureOps.Shared.Auth.Policies`.
- Real-time alert updates via the built-in Blazor SignalR hub.
- Call the API for data — never DB directly.
- Enforce the "audit is not surveillance" framing throughout.

## Forbidden in this project

See `.cursor/rules/060-ui-rules.mdc` for the full list. Highlights:

- No `localStorage` / `sessionStorage`.
- No leaderboards or per-operator comparison widgets.
- No direct DbContext injection in `.razor` files.
- No direct PowerShell invocation.
- No custom CSS conflicting with MudBlazor theme.

## Pages (Phase 2 target)

- `/` — Dashboard
- `/alerts` — Alert list with filtering
- `/alerts/{id:guid}` — Alert detail
- `/diagnostics` — Diagnostic job list
- `/diagnostics/{id:guid}` — Diagnostic result viewer
- `/servers` — Server inventory
- `/audit` — Audit query (Auditor/Admin only, meta-audited)
- `/admin/config` — Admin configuration

## Phase

Phase 1 produces a minimal status page only. Full UI is Phase 2.
