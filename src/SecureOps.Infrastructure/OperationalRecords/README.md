# Operational Records and Jira Integration

This Codex-owned backend module contains source/Jira/requester interfaces, fail-closed classification, preview mapping, durable workflow orchestration, InMemory and SQL repositories, and fake local adapters. It never executes PowerShell.

Default providers are `OperationalRecords:SourceProvider=Fake`, `OperationalRecords:RepositoryProvider=InMemory`, and `Jira:Provider=Fake`. Only the repository can currently select `SqlServer`, using `ConnectionStrings:SecureOpsDb`. Unsupported live providers fail startup validation.

The classifier intentionally returns `NeedsManualReview`; approved business rules and a manual-classification contract are deferred. Requester resolution is exact only. The Jira key is persisted before source close, and unknown Jira outcomes block automatic recreation.

See `docs/22-operational-record-jira-workflow.md` for configuration, state transitions, authorization, DBA prerequisites, and TEST validation.
