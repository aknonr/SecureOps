# 090 — Testing and Quality

Details and project layout: `tests/README.md`. Full checklist: `docs/13-definition-of-done.md`.

## Stack

xUnit v3 (`xunit.v3.mtp-off`, run through VSTest so `dotnet test --filter/--logger/--collect` keep working), FluentAssertions 7 (8.x is commercially licensed; do not upgrade without an owner decision), NSubstitute. Custom `Fact`/`Theory` attributes must forward `[CallerFilePath]`/`[CallerLineNumber]` to the base constructor. Blazor components are render-tested with `HtmlRenderer` (no bUnit). SQL and process-level acceptance tests are opt-in (they skip unless their environment variable is set) and run only against isolated local databases — never corporate ones. Browser journeys: `tests/browser/*.cjs` (Playwright, loopback hosts, synthetic data only).

## Commands

```bash
dotnet build SecureOps.sln                                   # 0 warnings, 0 errors (warnings are errors)
dotnet test tests/SecureOps.Tests.Unit/SecureOps.Tests.Unit.csproj
dotnet test SecureOps.sln
dotnet format --verify-no-changes                            # repository-wide: required gate
```

`global.json` pins the SDK with roll-forward disabled; if that SDK is unavailable, say so. A run with a substituted SDK or language version is diagnostic evidence only and must be labelled as such.

The repository-wide format check is a required gate; a run scoped with `--include` (or limited to `style`/`analyzers`) is partial evidence. Judge it on a Windows (CRLF) checkout: a Linux LF checkout reports `ENDOFLINE` on every file because `.editorconfig` asks for CRLF.

## Expectations

- New or changed behaviour has tests; security-sensitive paths have tests proving forbidden operations fail and audit is written. Test behaviour, not lines.
- Names: `Method_Condition_ExpectedResult`. No sleeps waiting for state, no shared mutable state, no real external services, no assertions on log text.
- Done means: builds clean, relevant tests pass, repository-wide format check passes (or the failure is reported as an open gate), docs and (if a decision changed) ADRs updated, public APIs documented, diff reviewable (aim < 400 changed lines; split above ~1000).
- Commits: meaningful subject, area/phase prefix such as `[UI]`, `[Docs]`, `[Phase 1]`; never break the build.

## Reporting

State exactly which commands ran, where, and with which results; separate pre-existing failures (compare with the base branch) from new ones; list what could not be verified (Windows/IIS, corporate TEST, native zoom, screen readers). Never claim a pass that did not run.
