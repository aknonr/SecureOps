# Agent context reduction, 2026-10-06

## Verdict

Core files are shorter with all eleven hard rules, ownership, approval persistence,
source-of-truth precedence and fail-closed semantics retained. Detailed context and
dated history remain discoverable under `docs/archive/agent-context-20261006/`.
No owner-approval rule was relaxed and no architectural/security decision changed.

Two Task 3 commits: core reduction first (`b0509af`), then history archival and this report.
The first includes verbatim core snapshots so that commit loses no information or links.
The second moves decision history and preserves the full prior test README separately.

## Evidence

Method: lines exclude the final empty newline; approximate tokens = ceiling(characters / 4),
including CRLF. These are entrypoint/context figures, not a model-specific tokenizer or total
repository size. Archival preserves text; it does not aim to delete repository knowledge.

| File / group | Before lines | After lines | Before approximate tokens | After approximate tokens |
|---|---:|---:|---:|---:|
| AGENTS.md | 85 | 43 | 2,368 | 1,243 |
| CLAUDE.md | 29 | 16 | 772 | 417 |
| Core total | 114 | 59 | 3,140 | 1,660 |
| docs/decisions-log.md | 261 | 63 | 5,433 | 1,216 |
| tests/README.md | 298 | 78 | 4,992 | 1,503 |
| Four edited entrypoints | 673 | 200 | 13,565 | 4,379 |
| docs/agent-guides/*.md (unchanged) | 211 | 211 | 4,862 | 4,862 |
| src/SecureOps.Ui/README.md (excluded) | 2,460 | 2,460 | 44,271 | 44,271 |

Core approximate context falls 47%; the four edited entrypoints fall 68%.
Decision-log initial task measurement was 252 lines / 5,283 tokens; Task 2's F3 entry
added 9 lines / 150 tokens before the Task 3 measurement shown above.

Growth came from repeated project/stack/reference detail in core files, combined
decision and acceptance handoffs, and accumulated feature/browser test journals.
The area guides are already short (9-31 lines each) after the earlier approved rewrite;
they stay unchanged. Current canonical activation/release/contract registers are large
but already loaded on demand; this task avoids relocating active evidence or contract authority.
The UI's 2,460-line historical journal is the largest named candidate, but is master-protected.

Instruction changes: core prose is condensed; project background, rule rationales,
full reference map and operator vocabulary now have one-line archive pointers.
Task-routed reading through the guide index replaces the duplicate core reference table.
Claude keeps its UI quality bar, honest states, server authorization, storage rule,
local HTTPS/demo/browser constraints, scope and reporting convention. Current stack,
open integration decisions, CONTOSO naming and the scoped Service Accounts exception stay visible.
Decision log retains all October/current owner entries; older approved entries still bind
from the archive unless explicitly amended/superseded. Dated acceptance remains historical.

Lossless checks passed: archived AGENTS and CLAUDE Git blob hashes match `520b07d`;
archived test README hash matches `8b891fb`; all 16 decision headings remain in order
and the complete moved decision tail is verbatim. Local Markdown targets resolve,
all touched files use CRLF, and `git diff --check` passes. No tests reference the edited
guidance paths. Task 3 is documentation-only; build/tests/format were not repeated for it.

Post-code-commit gates (Windows, exact SDK 10.0.401):

| Commit | Release build | Solution tests, Release --no-build | Repository-wide format | Diff check |
|---|---|---|---|---|
| 322311e (Task 1) | 0 warnings/errors | 1,769 unit + 332 integration passed; 110 opt-ins skipped | Passed | Passed |
| 8b891fb (Task 2) | 0 warnings/errors | 1,769 unit + 363 integration passed; 113 opt-in results skipped | Passed | Passed |

Exact commands: `dotnet build SecureOps.sln -c Release`,
`dotnet test SecureOps.sln -c Release --no-build`,
`dotnet format SecureOps.sln --verify-no-changes`, `git diff --check`.
Separate opt-in evidence is in the adjacent Task 1/2 reports; skipped results above are not passes.
Disabled SQL Theory attributes report one skipped result per method rather than enumerating
all rows; the enabled full Resource run executes those rows and passes all 105 cases.

## Blockers

### Simplify After Master Merge

Exclusion computed with `git diff --name-only $(git merge-base origin/master HEAD) origin/master`.
Remote master was verified at `50c052835c3adf8d37e4e8c6419f9b8cf138ead4`; UI base remained `520b07d`.
The following master-changed documentation paths were left untouched:

- `contracts/README.md`
- `docs/adr/ADR-0024-service-account-usage-discovery.md`
- `docs/adr/ADR-0026-one-time-service-accounts-scope-bootstrap.md`
- `docs/adr/ADR-0027-operator-run-usage-scan-import.md`
- `docs/contracts/secureops-api-v1.openapi.json`
- `docs/service-accounts/DBA-029-030-TR.md`
- `docs/service-accounts/HANDOFF.md`
- `docs/service-accounts/PROGRESS-HISTORY.md`
- `docs/service-accounts/PROGRESS.md`
- `docs/service-accounts/README.md`
- `docs/service-accounts/SPEC.md`
- `docs/service-accounts/TEAM-ROLE-SETUP-TR.md`
- `docs/service-accounts/USAGE-SCAN-TR.md`
- `docs/service-accounts/WINDOWS-ACCEPTANCE.md`
- `scripts/README.md`
- `scripts/jea/proposed/README.md`
- `sql/README.md`
- `src/SecureOps.Ui/README.md`

The OpenAPI contract and active ADR/spec content need ownership review, not automatic
archival. Service Accounts and UI work remain Claude-owned. Exclusion checks cover all
master-changed paths, not just this documentation list, and have no overlap with Task 3 edits.

## Minimal Safe Next Step

Owner reviews/pushes the local branch. After the master integration, coordinate with Claude
to split the UI README's historical journal while retaining current commands and ownership.

## Risks

Archived authority must remain distinct from current implementation and dated evidence.
Pointers state that moving text grants no approval or current acceptance. No source, UI,
Service Accounts path, provider, corporate database, TEST, IIS or deployment change in Task 3.
The main checkout was only read initially; all edits/commits were in the isolated worktree.
