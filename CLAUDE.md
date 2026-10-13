# CLAUDE.md

Read `AGENTS.md` first; its rules and ownership win (Claude: UI, plus Service Accounts end to end; Planned OCO Announcements is Codex's).

## UI

- Honest states: loading, empty, error, denied and partial are distinct. Never claim what the server did not confirm (a tab "opened", a provider "healthy"); never invent data — record the missing route/field/permission in `docs/26-ui-backend-contract-gaps.md`.
- The server authorizes; the UI only reflects `AccessSnapshot.Can(Capabilities.X)`.
- Keyboard operable, visible focus, colour never the only signal; 390 px and 200 % zoom without horizontal scroll; light and dark.
- Reuse `So*` components (`SoPageHeader`, `SoProblemPanel`, `SoEmptyState`, `SoLoading`, `SoStatusBadge`, `SoFieldGrid`) and scoped `.razor.css` with theme tokens over new global CSS.
- Browser storage only for `wasas.appearance`.
- More: `docs/agent-guides/060-ui.md`; local run: `src/SecureOps.Ui/README.md` → *Running locally*. Say which results are local versus Windows/IIS or corporate TEST.

## Vocabulary

vardiya = shift rotation · nöbetçi = on-call engineer (not the shift operator) · alarm geldi = monitoring alert received · diagnostic koştu = a read-only diagnostic ran · ticket = ITSM incident/change · audit = append-only operational record, not personnel monitoring · PAM / BeyondTrust = privileged access (read-only correlation) · Faz X = phase X in `docs/02-roadmap.md` · JEA / WinRM = constrained PowerShell remoting.

## Reporting

The owner is a Windows system administrator running shift operations: answer in Turkish, direct and brief, surfacing trade-offs and uncertainty. Close with: what changed, how verified (and what not), docs touched, open questions, next step.
