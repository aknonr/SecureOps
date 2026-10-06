# Decisions Log

Approved owner decisions here and in the archive remain binding unless explicitly amended or superseded. Dated test/deployment claims are historical evidence, not current verification.
[Pre-October 2026 decisions and handoffs](archive/agent-context-20261006/decisions-before-202610.md) are preserved verbatim; search that archive when the task depends on earlier scope or decisions.

## 2026-10-06 - Concurrent session termination (ADR-0014 Amendment 4)

Owner-approved F3: first committed termination owns the reason/timestamp and terminal audit.
Concurrent single-session losers write no audit and reread that state; logout/revoke return it,
validation denies using its winning reason, and unverifiable rereads fail closed. SQL and
InMemory share the policy. F4 already-running request/Touch boundaries remain open and outside
this change. Local parallel SQL/InMemory evidence is in
`docs/validation/dotnet10-followups-2-sessions-20261006.md`; no corporate or UI change.

## 2026-10-03 - OR request type: text suggestion, one-click confirm (ADR-0018 Amendment 1)

Owner decision: the operator should not start from an empty "operator declaration" select. The API suggests
one supported request type from explicit title/description words and shows the words; the operator confirms or
changes it. Conflicting or absent words give no suggestion. The suggestion stays outside SDM evaluation input and
never grants eligibility; the confirmed type is still the operator declaration checked by pilot policy and mapping.

## 2026-10-03 - .NET 10 Windows gates and package refresh (PR #8)

First Windows run of the migration branch: SDK pin moved to 10.0.401 (same 10.0.12 runtime), Release build,
tests, format and release-packaging dry run pass. Packages refreshed per ADR-0001 Amendment 2 "Package refresh".
Owner then approved the Swashbuckle 10 OpenAPI snapshot change and removal of the unused JsonSchema.Net.

## 2026-10-02 - Owner direction for the next work (session management, redesigns)

- Session policy becomes admin-adjustable within fixed lower and upper bounds (idle and absolute), audited.
- When an administrator ends someone's session, that person is signed out: today the API session ends but the UI
  cookie survives and the next request silently starts a new API session. The UI must drop its cookie and session
  store on `SessionRevoked`, and the API must not silently restart a session for that browser.
- ADRs must not block redesign: Jira/SDM, In Use, per-module permission sections and page layouts may be redesigned
  on .NET 10, amending or superseding their ADRs in the same change.
- Next steps run in local Claude Code on Windows, which can execute the Windows-only gates.

## 2026-10-02 - .NET 10 migration (ADR-0001 Amendment 2), done by Claude by owner decision

The owner assigned the backend .NET 10 migration to Claude for this change (scoped exception to the default
Codex ownership). Branch `claude/dotnet10-backend-migration`: SDK 10.0.112 pinned, `net10.0`, C# 14,
analyzer level 10.0; unused EF Core/Polly/OpenApi packages removed; OIDC PAR kept off by default; per-actor
global and access-administration rate limits added. Windows/IIS, Negotiate, DPAPI and PowerShell runspace gates
still need a Windows run before release.

## 2026-10-01 - Data access: Dapper and numbered SQL scripts retained

Owner decision recorded as ADR-0001 Amendment 1: Dapper with parameterized SQL and numbered, DBA-reviewed scripts
in `sql/schema/` and `sql/migrations/` is the approved data-access approach; EF Core adoption is no longer planned.
The original 2026-05 decision text is preserved in the ADR. Tasks that required an EF Core `DbContext` (P1-05,
P1-T05) are marked superseded, not implemented. No package, repository, SQL or product behaviour changed; the
unused EF Core package references are left for a separate code change.

## 2026-10-01 - Agent guidance rewritten for current models

Owner decision: `AGENTS.md`, `CLAUDE.md` and `docs/agent-guides/` keep every hard rule and the
Codex/Claude ownership split, but drop scaffolding written for weaker models (a 10-document
mandatory reading order before any output, generic code samples, duplicated rule lists, fixed
plan/confirm thresholds). Reading is now routed by task; each hard rule states its reason.
Statements that contradicted the code were corrected: capability-based authorization (ADR-0010,
ADR-0022) instead of AD-group roles, Dapper-only data access (settled by the owner the same day, see the entry above), `HtmlRenderer`
render tests instead of bUnit, pinned C# 12 / SDK, and a single canonical JEA allow-list in
`docs/05-security-model.md`. No architectural decision changed. Code is treated as the observed implementation; it never overrides a hard rule or an approved decision.
