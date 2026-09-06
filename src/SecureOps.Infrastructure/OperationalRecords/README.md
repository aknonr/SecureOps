# Operational Records and Jira Integration

This Codex-owned backend module contains source/Jira/requester interfaces, fail-closed classification, preview mapping, durable workflow orchestration, InMemory and SQL repositories, and fake local adapters. It never executes PowerShell.

Default providers are `OperationalRecords:SourceProvider=Disabled`, `OperationalRecords:RepositoryProvider=InMemory`, and `Jira:Provider=Disabled`. `Fake` is an explicit deterministic source/Jira adapter available only in Development, Demo, and Test; it performs no external I/O. `TuruncuHat` and `Corporate` select typed real-provider adapters, but remain deployment-disabled until the sanitized external contract fixtures in `docs/integrations/turuncu-hat-jira-contract-gaps.md` are approved. Provider configuration fails startup instead of falling back.

The Disabled and TuruncuHat classifiers return `NeedsManualReview`; source-scope membership alone is not a Jira-eligibility rule. Synthetic eligibility remains confined to explicit synthetic providers, and synthetic rows are unavailable through TuruncuHat reads and workflow commands. Requester resolution remains exact. The Jira key is persisted before source close, and unknown Jira outcomes block automatic recreation.

Source refresh cannot reclassify a workflow after it advances beyond initial classification states. Automated failure and concurrency scenarios replace `IJiraClient` only inside the integration-test host; runtime providers expose no failure-injection controls. `FakeJiraClient` returns synthetic `FAKE-*` keys only.

See `docs/22-operational-record-jira-workflow.md` for configuration, state transitions, authorization, DBA prerequisites, and TEST validation.

Corporate refresh uses the pure Domain SDM evaluator (ADR-0018); it does not
resolve Jira users or call external write clients. SQL migration 009 persists
bounded evidence with atomic append-only history/audit and unchanged-input
idempotency. Current corporate evidence remains manual-review-only. Fake and
Simulation classifiers remain isolated compatibility harnesses.
