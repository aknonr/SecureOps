# Draft PR: Session v2 and .NET 10 backend follow-ups

Base: `claude/dotnet10-ui-mudblazor9`, not master. Head: `codex/dotnet10-backend-followups`.
Owner-authorized own-branch push and draft PR only; no merge, auto-merge or deployment.

Administrative revocation ends UI authentication, clears server-held API cookies and OIDC
tokens, and blocks further calls from the terminal correlation. Terminal API handles remain
on rejection so later requests cannot silently create another application session.

Separate commits cover Session v2, behavior-preserving access/In Use pattern modernization,
xUnit1051 enforcement, the backend review, the approved F2/F1 follow-ups, and a new
InMemory non-atomic audit-first correction (no amend or SQL implementation change).

- F2 resolved: missing UI process state rejects/deletes the old cookie and requires explicit
  sign-in. Hosted cache/store-loss tests leave the API session-start count unchanged.
- F1 resolved: SQL logout, revoke, expiry and terminal batches commit state and append-only audit
  together. Actual LocalDB tests prove update/audit rollback, second-insert rollback and cancellation.
  Non-durable InMemory sessions preserve atomic-writer batching; File/Queued writers are now
  awaited first and state is published only after every audit write succeeds. Audit failure
  keeps all affected sessions Active and returns 503 rather than rejecting a supported writer.
- F3-F6 remain open; concurrency semantics, concrete-provider gates and dispatcher cleanup
  are not changed by this work.

Final correction gates on SDK 10.0.401: zero-warning Release build, 1,768 unit + 332 integration passed,
repository-wide format clean and `git diff --check` clean. The ordinary suite skipped 110 opt-ins;
56 were subsequently run by the fresh guarded Resource SQL harness, all passing with zero skips
and migrations 001-027 verified. The remaining 54 opt-ins were not enabled. Focused totals overlap.
Red-before-fix reproduced 17 unit and 8 hosted failures; final focused regressions passed
51 unit + 24 hosted tests. Fresh SQL database: `SecureOps_ResourcesV1_SessionAudit_20261006_61c50e191d3f`.
Actual File sink failures/queue-drain durability were not exercised; non-atomic behavior uses
File-like synthetic writers. Concrete-provider scope (F5) remains open.

InMemory/File or queued audit cannot share an atomic transaction: failed batches can retain an
append-only audit prefix while sessions remain Active, and retry can append duplicate events.
Queued success means enqueue acceptance, not durable sink persistence. SQL has real transaction
atomicity; defaults, initial session-start compensation and F3/F4 concurrency policy are unchanged.

After deployment, every existing user must explicitly sign in once again: old UI cookies
without the browser-correlation claim are rejected, not silently upgraded. Losing process-local
correlation later also requires sign-in. This PR performs no deployment.

No browser/provider acceptance is claimed. No Playwright installation/run, merge, deployment,
IIS or corporate database operation. Protected layout, navigation, theme and Pages are untouched.
Detailed evidence and remaining gates: `docs/reviews/dotnet10-backend-followups-20261005.md`.
