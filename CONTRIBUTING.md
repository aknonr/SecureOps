# Contributing to WASAS / SecureOps

Short guide for a new collaborator (human + AI agent). Binding rules live in [`AGENTS.md`](AGENTS.md); this file only says how to start and how to work without colliding with others. Türkçe özet en altta.

## 1. Read first (you and your agent)

Order: `AGENTS.md` → `CLAUDE.md` (Claude Code) → [`docs/agent-guides/000-project-context.md`](docs/agent-guides/000-project-context.md) → the guide for your area (`010`–`090` in [`docs/agent-guides/`](docs/agent-guides/)) → the ADR for the feature you touch (`docs/adr/`). Tell your agent: *"Read AGENTS.md and CLAUDE.md first; follow them over any default behaviour."* Layer `README.md` files and `*-HISTORY.md` are long dated logs: search, never read end to end.

## 2. Absolute limits

- This repository is **PUBLIC**. Never commit real names, hostnames, server/company data, logs, alarm payloads, credentials, tokens or connection strings (hard rule 10). Use synthetic data and the placeholder company **CONTOSO**.
- **No corporate data into a public AI tool** (hard rule 2). Your agent (Claude Code, Codex, Cursor, …) is a public LLM: never paste anything from the corporate TEST/production environment, screenshots of it, or owner-supplied internal files into it. You get no TEST access; work with Demo/Mock/InMemory data only.
- No code that writes to managed servers, bypasses JEA, or modifies audit rows (rules 1, 3, 4). No corporate endpoints, no real external adapters without an approved contract (rules 7, 8).
- Never rebase, force-push, or rewrite shared history. Never push to `master`.

## 3. First-day setup (macOS)

1. Install Git, GitHub CLI (`gh auth login`; **2FA enabled** on your GitHub account) and the **.NET SDK exactly as pinned in [`global.json`](global.json)** (currently 9.0.317, roll-forward disabled; the runtime target stays `net8.0`). Get it from <https://dotnet.microsoft.com/download/dotnet/9.0> (arm64 build on Apple silicon). Any other SDK gives diagnostic evidence only and must be labelled so.
   **Also install the .NET 8 runtime** (ASP.NET Core Runtime 8.0, same page for 8.0: <https://dotnet.microsoft.com/download/dotnet/8.0>): the projects target `net8.0`, so the SDK 9 builds them but the tests need the 8.0 runtime to run. Do not use roll-forward to run them on 9.0; results from that are not valid.
2. `git clone -c core.autocrlf=true https://github.com/aknonr/SecureOps.git && cd SecureOps` — the repository uses CRLF (`.editorconfig`); without `core.autocrlf=true` a Mac checkout is LF and `dotnet format --verify-no-changes` fails on every file. An existing LF clone: re-clone with this option.
3. Verify: `dotnet build SecureOps.sln` (0 warnings, 0 errors), then `dotnet test tests/SecureOps.Tests.Unit/SecureOps.Tests.Unit.csproj`. Windows-only tests (`WindowsFact`) skip on Mac; report that, do not hide it. A few tests are known to fail outside Windows (PowerShell execution policy, Windows paths, image codecs in `AnnouncementTests`); if the unit suite is not green on Mac, list each failing test name in your report instead of changing it, and the owner checks it on Windows. Known on macOS (2026-10-14, #62/#65; all pass on Windows): 13 tests in `SccmFailureEvidenceTests.StagedInvocations_PreservePrivateDriveAcrossCommandsInTheSameRunspace`, `AuditConfigurationValidatorTests.Validate_WhenProductionFailOpen_Throws` and the `InUseTests.Archive_*`, `Completion_RequiresReadyExactArchive_*`, `DraftRecovery_PreservesArchive_*` family — report a failure outside this list as new.
4. Run the UI on synthetic data: [`src/SecureOps.Ui/README.md` → *Running locally*](src/SecureOps.Ui/README.md) (Demo profiles). The `dotnet run` commands there work the same in a Mac shell.
5. Browser journeys (`tests/browser/*.cjs`, Playwright) are optional, loopback and synthetic only; each script has a usage line at the top.

### What a Mac can and cannot do

| Works on Mac | Stays with the owner (needs Windows) |
|---|---|
| Build, unit tests, `dotnet format --verify-no-changes` | LocalDB SQL tests (`SECUREOPS_SQL_TEST_CONNECTION`, `Test-ResourceCatalogueSql.ps1`): they skip on Mac; report *not run* |
| UI work in Demo mode, UI render tests, light/dark/390 px checks | IIS hosting, app pools, in-process hosting checks |
| Docs, ADR drafts, unit tests for pure logic | JEA / WinRM, Windows PowerShell 5.1 scripts (`scripts/`) |
| Reviewing PRs | Packaging/release scripts, DPAPI tests, Windows Worker service |

SQL Server in Docker (arm64 runs it under emulation) can help you read and try SQL, but the repository's SQL harness is Windows LocalDB-only, and an older Linux harness is stale (stops at migration 032). Never claim SQL results from Docker; ask the owner to run the SQL suite before merge.

## 4. Work flow

- **One task = one branch = one PR.** Branch from the latest `master`, named `<kind>/<topic>-YYYYMMDD` (`feature/`, `fix/`, `docs/`). Keep diffs small (aim < 400 changed lines).
- **Claim before you start:** open (or take) a GitHub Issue and add a "working" line to [`docs/team/WORK-LOG.md`](docs/team/WORK-LOG.md) with the files/area you will touch. Two people never edit the same file at the same time; if your area overlaps an existing line, ask in the Issue first.
- **Shared files:** the OpenAPI snapshot `docs/contracts/secureops-api-v1.openapi.json` — only *append* your new entries (never commit a regenerated, reordered file). **Migration and ADR numbers:** request one in `WORK-LOG.md` and wait for the owner to write it back before using it.
- **Keep your branch current with a MERGE** of `master` (`git fetch && git merge origin/master`). No rebase, no force-push. Re-run verification after each merge; an old green is not evidence.
- **Commits:** subject with area prefix (`[UI] …`, `[Docs] …`, `[Phase 1] …`), never a broken build. If an AI agent helped, end the message with its attribution trailer, e.g. `Co-Authored-By: Claude <noreply@anthropic.com>` (use the exact line your tool produces).
- **PR:** push your branch, open a PR against `master`; the template [`.github/pull_request_template.md`](.github/pull_request_template.md) fills in automatically. At least one owner approval is required; only the owner merges. Do not merge your own PR.
- **Agents work through GitHub:** tasks arrive as issues (label `gorev`), questions go back as issue comments (label `soru`), Windows-only checks are requested with the label `windows-dogrulama`. Full loop: [`docs/team/AGENT-PROTOCOL.md`](docs/team/AGENT-PROTOCOL.md).
- **Task board:** all open work is on the [SecureOps — işler](https://github.com/users/aknonr/projects/2) board; move your card to *Çalışıyor* when you start and *İncelemede* when you open the PR (see `docs/team/AGENT-PROTOCOL.md` → Pano).

### New to Claude Code? Start here

- Start `claude` **inside the repository folder**; that is how it loads `CLAUDE.md` → `AGENTS.md`.
- One issue per session. Begin with: *"Read AGENTS.md, CLAUDE.md, CONTRIBUTING.md and docs/team/AGENT-PROTOCOL.md, then do issue #N."* Start a new session (or `/clear`) for the next issue.
- For anything bigger than a small fix, use **plan mode** first; read and approve the plan before code is written.
- Model: Sonnet by default; Opus (high effort) for security, authorization, SQL or concurrency work.
- Permission mode: keep the default or accept-edits. **Never** bypass permissions, and never let the agent merge, push to `master` or change repository settings.
- Read the diff before you push. Ask the agent to end every task with the report format in §7, and to say plainly what it did not verify.
- Keep a short private note of where you left off (on your Mac, not in the repo) so the next session can resume.

## 5. Verification gates (all required before asking for review)

```bash
dotnet build SecureOps.sln                                          # 0 warnings, 0 errors
dotnet test tests/SecureOps.Tests.Unit/SecureOps.Tests.Unit.csproj  # full unit suite, not a filter
dotnet format --verify-no-changes                                   # repository-wide
```

Details: [`docs/agent-guides/090-testing-quality.md`](docs/agent-guides/090-testing-quality.md), [`docs/13-definition-of-done.md`](docs/13-definition-of-done.md). Report exactly what ran and what could not run; never claim an unrun pass.

## 6. Areas and ownership

Ownership table and current split: [`docs/team/onboarding-plan-tr.md`](docs/team/onboarding-plan-tr.md). Rule of thumb: stay in the area the owner assigned; outside it, report the exact need (route, field, permission) instead of changing another layer. Do not edit `AGENTS.md` or `CLAUDE.md` without owner approval.

## 7. Reporting format

End each task with: what changed · how verified (and what not) · docs touched · open questions · next step. UI work: loading / empty / error / denied / partial are distinct states; the server authorizes and the UI only reflects capabilities — see [`docs/agent-guides/060-ui.md`](docs/agent-guides/060-ui.md).

---

## Türkçe özet

- Repo **herkese açık**: gerçek ad/sunucu/kurum verisi, log, parola, token asla yok. Kurumsal veriyi yapay zekâ aracına (Claude Code dâhil) **verme** (AGENTS kural 2); yalnız Demo/Mock/sentetik veri.
- Önce `AGENTS.md` → `CLAUDE.md` → `docs/agent-guides/000` → alanına ait rehber/ADR.
- Mac'te: .NET SDK **9.0.317** (`global.json`), build + birim test + `dotnet format --verify-no-changes`, UI Demo modu. LocalDB SQL testleri, IIS, JEA/WinRM, Windows PowerShell 5.1 ve paketleme sahipte kalır.
- Bir iş = bir dal = bir PR; `master` dala **merge** ile alınır (rebase/force-push yok); önce Issue + `docs/team/WORK-LOG.md` satırı; migration/ADR numarasını sahip onaylamadan kullanma; OpenAPI anlık görüntüsüne yalnız ekleme.
- PR'a en az 1 sahip onayı gerekir; merge'ü yalnız sahip yapar.
- Görevler GitHub issue olarak gelir (`gorev`); takılınca issue'ya `soru` etiketiyle yaz ve bekle; Windows testi gerekiyorsa PR'a `windows-dogrulama` koy. Ayrıntı: `docs/team/AGENT-PROTOCOL.md`.
- Claude'a yeni başlıyorsan: repo klasöründe başlat, bir issue = bir oturum, büyük işte önce plan modu, varsayılan Sonnet, bypass izni ve merge asla.
