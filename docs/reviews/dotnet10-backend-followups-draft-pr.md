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
- F1 approved, implementation and isolated SQL rollback verification pending.
- F3-F6 remain open; concurrency semantics, concrete-provider gates and dispatcher cleanup
  are not changed by this work.

F2 focused evidence: 32 unit and 32 hosted integration tests pass, zero skips, SDK 10.0.401.
Earlier full gates: zero-warning Release build, 1,725 unit + 317 integration passed,
104 opt-in integration skips, repository-wide format clean. Final follow-up gates will
replace these historical totals after F1.

No browser/provider acceptance is claimed. No Playwright installation/run, merge, deployment,
IIS or corporate database operation. Protected layout, navigation, theme and Pages are untouched.
Detailed evidence and remaining gates: `docs/reviews/dotnet10-backend-followups-20261005.md`.
