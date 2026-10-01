# 090 — Testing and Quality Rules (Always Apply)

## Applicability

- **Purpose:** Testing, build, and quality rules. Read every time code changes.
- **Applies to:** Every task; its code and test requirements apply when `src/**/*.cs` or `tests/**/*.cs` is changed.
- **Loading:** Routed explicitly from `AGENTS.md`; do not assume automatic discovery.

## Test Frameworks

- **xUnit** for .NET unit and integration tests.
- **FluentAssertions** for readable assertions.
- **NSubstitute** for mocks (preferred) or Moq.
- **Testcontainers** for SQL Server integration tests (Phase 1+).
- **bUnit** for Blazor component tests.

## Test Project Structure

```
tests/
├── SecureOps.Tests.Unit/
│   ├── Domain/          # Domain entity behavior tests
│   ├── Application/     # Application service tests with mocks
│   └── Shared/          # DTO, contract, utility tests
└── SecureOps.Tests.Integration/
    ├── Api/             # API endpoint integration tests
    ├── Worker/          # Hangfire job integration tests
    ├── Sql/             # Schema, trigger, view tests
    └── Mocks/           # Mock SolarWinds, PAM, Teams, etc.
```

Mirror the source layout: `tests/SecureOps.Tests.Unit/Foo/BarTests.cs` tests `src/SecureOps.Domain/Foo/Bar.cs`.

## Test Naming

```csharp
public sealed class DiagnosticRunnerTests
{
    [Fact]
    public async Task RunForAlert_WhenAlertNotFound_ReturnsNotFoundResult() { }

    [Fact]
    public async Task RunForAlert_WhenServerUnreachable_RetriesAndLogs() { }

    [Theory]
    [InlineData(AlertSeverity.Critical)]
    [InlineData(AlertSeverity.Warning)]
    public async Task RunForAlert_HonorsCancellationToken(AlertSeverity severity) { }
}
```

Pattern: `<MethodName>_<Condition>_<ExpectedResult>`.

## What Must Be Tested

### Always

- Domain entities: business rules, invariants, value object equality.
- Application services: orchestration logic with mocked infrastructure.
- API controllers: routing, authorization policy enforcement, validation.
- SQL triggers: audit append-only enforcement.
- Authorization policies: role-to-permission mapping.

### When Touched

- Any code path that handles a security-sensitive operation.
- Any code path that writes to the audit log.
- Any integration adapter (Solarwinds, PAM, Teams) — test the mock and the real adapter (against a sandbox if available).

### Coverage Targets

- Domain: 90%+ line coverage.
- Application: 80%+ line coverage.
- Infrastructure: 60%+ line coverage (integration-heavy).
- UI: focus on logic in code-behind, not visual.

Do not chase 100%. Test behavior, not lines.

## Forbidden in Tests

- Sleeping or `Thread.Sleep` for "wait for things". Use TestScheduler or `await` on real signals.
- Real external services in unit tests. Use mocks.
- Hard-coded paths to user directories. Use `Path.Combine` with test helpers.
- Tests that share mutable state. Each test sets up and tears down its own state.
- Asserting on log message text — assert on behavior, not log output.

## Definition of Done (per code change)

A change is "done" when:

1. Code compiles: `dotnet build` returns 0 warnings, 0 errors.
2. Tests pass: `dotnet test` returns success.
3. New behavior has new tests.
4. Modified behavior has updated tests.
5. Public APIs have XML doc comments.
6. Relevant `docs/*.md` is updated if behavior changed.
7. If architecture or a key decision changed: an ADR is created or updated.
8. No new analyzer warnings.
9. Changes are reviewable: diff under 400 lines preferred, hard cap 1000.

See `docs/13-definition-of-done.md` for the full checklist.

## Static Analysis

Enable in every `.csproj`:

```xml
<PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

Use `.editorconfig` for style rules (provided at repo root).

## Build Commands

```bash
# Build
dotnet build SecureOps.sln

# Unit tests only
dotnet test tests/SecureOps.Tests.Unit/SecureOps.Tests.Unit.csproj

# All tests
dotnet test SecureOps.sln

# With coverage
dotnet test SecureOps.sln --collect:"XPlat Code Coverage"

# Format check
dotnet format --verify-no-changes
```

## Commit and PR Discipline

Every commit:
- Has a meaningful subject line.
- Mentions the phase or task ID if applicable: `[Phase 1] Add disk diagnostic runner`.
- Does not break the build.

Every PR:
- Has a description matching the Definition of Done checklist.
- Has at least one approval (when more than one developer exists).
- Links to the relevant phase plan task.

## When You Cannot Run Tests

If you are an AI agent and cannot execute `dotnet build` / `dotnet test` in your environment:

- Say so explicitly in your summary.
- List exactly which commands the user should run.
- List the test files that should be checked.
- Do not claim "tests pass" if you did not run them.

## Reference

Read `docs/13-definition-of-done.md` for the full DoD.
