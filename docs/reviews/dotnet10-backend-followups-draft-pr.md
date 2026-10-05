# Draft PR: Session v2 and .NET 10 backend follow-ups

Base: `claude/dotnet10-ui-mudblazor9`, not master. Head: `codex/dotnet10-backend-followups`.
Local handoff only; no push or PR creation is authorized for the current implementation pass.

Administrative revocation ends UI authentication, clears server-held API cookies and OIDC
tokens, and blocks further calls from the terminal correlation. Terminal API handles remain
on rejection so later requests cannot silently create another application session.

Separate commits cover Session v2, behavior-preserving access/In Use pattern modernization,
xUnit1051 enforcement, the backend review, and the approved F2/F1 follow-ups.

- F2 resolved: missing UI process state rejects/deletes the old cookie and requires explicit
  sign-in. Hosted cache/store-loss tests leave the API session-start count unchanged.
- F1 resolved: logout, revoke, expiry and terminal batches commit state and append-only audit
  together. Actual LocalDB tests prove update/audit rollback, second-insert rollback and cancellation.
- F3-F6 remain open; concurrency semantics, concrete-provider gates and dispatcher cleanup
  are not changed by this work.

Final gates on SDK 10.0.401: zero-warning Release build, 1,752 unit + 324 integration passed,
repository-wide format clean and `git diff --check` clean. The ordinary suite skipped 110 opt-ins;
56 were subsequently run by the fresh guarded Resource SQL harness, all passing with zero skips
and migrations 001-027 verified. The remaining 54 opt-ins were not enabled. Focused totals overlap.

InMemory/File or queued audit cannot share an atomic transaction and fails closed for termination;
defaults were not changed. Supported local acceptance is InMemory/InMemory or SQL with the same
SQL audit table. Initial session-start compensation and F3/F4 concurrency policy remain unchanged.

No browser/provider acceptance is claimed. No Playwright installation/run, merge, deployment,
IIS or corporate database operation. Protected layout, navigation, theme and Pages are untouched.
Detailed evidence and remaining gates: `docs/reviews/dotnet10-backend-followups-20261005.md`.
