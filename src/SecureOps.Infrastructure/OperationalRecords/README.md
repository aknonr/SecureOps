# Operational Records and Jira Integration

This Codex-owned backend module contains source/Jira/requester interfaces, fail-closed classification, preview mapping, durable workflow orchestration, InMemory and SQL repositories, and fake local adapters. It never executes PowerShell.

Default providers are `OperationalRecords:SourceProvider=Disabled`, `OperationalRecords:RepositoryProvider=InMemory`, and `Jira:Provider=Fake`. `Fake` is an explicit deterministic source available only in Development, Demo, and Test; it performs no external I/O. Only the repository can currently select `SqlServer`, using `ConnectionStrings:SecureOpsDb`. Unsupported live providers and Fake in production-style environments fail startup validation.

The production-style classifier intentionally returns `NeedsManualReview`; approved business rules and a manual-classification contract are deferred. The synthetic source has an exact synthetic-only classifier and requester mapping solely to exercise preview/create/close safely. Requester resolution remains exact. The Jira key is persisted before source close, and unknown Jira outcomes block automatic recreation.

See `docs/22-operational-record-jira-workflow.md` for configuration, state transitions, authorization, DBA prerequisites, and TEST validation.
