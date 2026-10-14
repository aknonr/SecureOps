# Team work log

Short claim register for collaborators. It does **not** replace the owner's coordination file outside the repository (merge order, migration/ADR numbers); the owner copies anything that needs a number or a merge slot there. Add a line before you start, update it when you push, delete it after merge (GitHub history keeps the record). One line per task; no real names or internal data.

## Working

| Date | Who (GitHub handle) | Branch | Issue | Files / area touched | Status |
|---|---|---|---|---|---|
| 2026-10-14 | nurseliii | docs/mac-first-verification-20261014 | #62 | docs/team/WORK-LOG.md (docs only, no code) | bitti (#65) |
| 2026-10-14 | nurseliii | fix/ui-access-screens-responsive-20261014 | #67 | src/SecureOps.Ui/Pages/{Sessions,AccessUsers,AccessRequests}.razor(.css), tests/SecureOps.Tests.Unit (UI), tests/browser/access-screens-responsive.cjs, src/SecureOps.Ui/README.md, docs/26-ui-backend-contract-gaps.md | çalışıyor |

## Number requests

Ask here and wait for the owner to write the number back. Never use an unconfirmed number.

| Date | Kind (migration / ADR / gap id) | Requested by | Purpose | Number (owner fills) |
|---|---|---|---|---|
| 2026-10-14 | gap id | nurseliii | #67: paged access lists (`users/page`, `requests/page`) are SQL-only, 503 on InMemory; new row in `docs/26` is marked `G-?` until the owner writes the number | |
