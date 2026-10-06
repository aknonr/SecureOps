# CLAUDE.md

Read `AGENTS.md` first; its hard rules and ownership win. Claude owns UI/UX and tests, except Planned OCO Announcements (Codex); scoped Service Accounts ownership is in `docs/service-accounts/README.md`.
Outside those exceptions, do not change backend/API/DTOs/SQL/auth/integrations/contracts. Record exact missing routes, fields, permissions or semantics in `docs/26-ui-backend-contract-gaps.md`; show honest UI state.

## UI Rules

- Distinguish loading, empty, error, denied and partial states. Claim only outcomes confirmed by the server.
- Server decides access; UI reflects `AccessSnapshot.Can(Capabilities.X)` only.
- Keyboard operable, visible focus, colour not the only signal; 390 px and 200% zoom without horizontal scroll; light/dark.
- Reuse `SoPageHeader`, `SoProblemPanel`, `SoEmptyState`, `SoLoading`, `SoStatusBadge`, `SoFieldGrid`; prefer scoped Razor CSS/theme tokens over global CSS.
- Browser storage is only for non-sensitive `wasas.appearance`; all other state is server-held.

Details: `docs/agent-guides/060-ui.md`. Local commands: UI README, *Running locally*: Demo/InMemory, API `Access__DemoCompatibilityEnabled=true`, UI HTTPS for Secure antiforgery cookies. Browser journeys: `tests/browser/*.cjs`, loopback only, synthetic API seeding with `X-SecureOps-Demo-Actor`; separate local results from IIS/corporate TEST.
[Preserved operator vocabulary and UI context](docs/archive/agent-context-20261006/CLAUDE.before.md); search feature history in `src/SecureOps.Ui/README.md`.
Report in Turkish, directly and briefly, with trade-offs/uncertainty: changed, verified/unverified, docs, open questions, next step.
