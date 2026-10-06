# CLAUDE.md

Extends `AGENTS.md` (read it first; its hard rules and ownership win). This file only adds what Claude needs for UI work.

## Scope

By default Claude owns UI/UX under `src/SecureOps.Ui/` (Razor, CSS, layout, theme, navigation, accessibility, visual behaviour) and its tests — except Planned OCO Announcements, which Codex owns. Owner-approved scoped exceptions extend this for their stated scope only (currently Service Accounts: `docs/service-accounts/README.md`). Outside those, do not change backend, API, DTOs, SQL, auth, integrations or contracts. When the UI needs data or behaviour the API does not provide, say exactly what is missing (route, field, permission, semantics) and record it in `docs/26-ui-backend-contract-gaps.md`; the UI shows an honest state instead of inventing data.

## UI quality bar

- Honest states: loading, empty, error, denied and partial results are distinct and say what is known. Never claim an outcome the server did not confirm (e.g. a browser tab "opened", a provider "healthy").
- Server decides authorization; the UI only reflects `AccessSnapshot.Can(Capabilities.X)` to show or hide.
- Keyboard operable, visible focus, colour never the only signal; works at 390 px width and 200 % zoom without horizontal scroll; light and dark appearance.
- Prefer existing shared components (`SoPageHeader`, `SoProblemPanel`, `SoEmptyState`, `SoLoading`, `SoStatusBadge`, `SoFieldGrid`) and scoped `.razor.css` with theme tokens over new global CSS.
- Browser storage only for the non-sensitive appearance preference (`wasas.appearance`); everything else is server state.

Details: `docs/agent-guides/060-ui.md` and `src/SecureOps.Ui/README.md` (search it for the feature; it is a long dated log).

## Running the UI locally

Canonical commands: `src/SecureOps.Ui/README.md` → *Running locally* (Demo launch profiles, synthetic InMemory data; the API also needs `Access__DemoCompatibilityEnabled=true`, and the UI runs on HTTPS because its antiforgery cookie is Secure). Browser journeys are `tests/browser/*.cjs` (Playwright, loopback hosts only; helpers in `journey-support.cjs`); seed data through the API with the `X-SecureOps-Demo-Actor` header. Say plainly which results came from this local setup versus Windows/IIS or corporate TEST.

## Domain vocabulary

vardiya = shift rotation · nöbetçi = on-call engineer (not the shift operator) · alarm geldi = monitoring alert received · diagnostic koştu = a read-only diagnostic ran · ticket = ITSM incident/change · audit = append-only operational record, not personnel monitoring · PAM / BeyondTrust = privileged access (read-only correlation) · Faz X = phase X in `docs/02-roadmap.md` · JEA / WinRM = constrained PowerShell remoting.

## Reporting back

The owner is a Windows system administrator who runs shift operations and writes in Turkish; answer in Turkish, direct and brief, and surface trade-offs and uncertainty instead of hiding them. Close a task with: what changed, how it was verified (and what was not), docs touched, open questions, next step.
