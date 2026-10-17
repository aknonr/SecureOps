# Team work log

Short claim register for collaborators. It does **not** replace the owner's coordination file outside the repository (merge order, migration/ADR numbers); the owner copies anything that needs a number or a merge slot there. Add a line before you start, update it when you push, delete it after merge (GitHub history keeps the record). One line per task; no real names or internal data.

## Working

| Date | Who (GitHub handle) | Branch | Issue | Files / area touched | Status |
|---|---|---|---|---|---|
| 2026-10-14 | nurseliii | docs/mac-first-verification-20261014 | #62 | docs/team/WORK-LOG.md (docs only, no code) | bitti (#65) |
| 2026-10-14 | nurseliii | fix/ui-access-screens-responsive-20261014 | #67 | src/SecureOps.Ui/Pages/{Sessions,AccessUsers,AccessRequests}.razor(.css), tests/SecureOps.Tests.Unit (UI), tests/browser/access-screens-responsive.cjs, src/SecureOps.Ui/README.md, docs/26-ui-backend-contract-gaps.md | bitti (#68) |
| 2026-10-17 | nurseliii | claude/server-local-access-proposal-20261017-639187 | #80 | docs/contracts/server-local-access-proposal.md (new), docs/26-ui-backend-contract-gaps.md (G-43 only); docs only, no code | PR #81 açık |
| 2026-10-17 | nurseliii | claude/ui-access-dialog-detail-20261017-c9857e | #79 | src/SecureOps.Ui/Shared/Components/AccessActionDialog.razor(.css), Pages/AccessUserDetail.razor(.css), wwwroot/js/so-focus.js, tests (UI render, tests/browser/access-dialog-detail.cjs), src/SecureOps.Ui/README.md, docs/26 (G-42) | PR açık |

## Number requests

Ask here and wait for the owner to write the number back. Never use an unconfirmed number.

| Date | Kind (migration / ADR / gap id) | Requested by | Purpose | Number (owner fills) |
|---|---|---|---|---|
| 2026-10-14 | gap id | nurseliii | #67: paged access lists (`users/page`, `requests/page`) are SQL-only, 503 on InMemory; new row in `docs/26` | G-40 verildi |
