# Announcement source repair acceptance, 2026-09-14

## Scope and verdict

Local source acceptance passed; editor/send integration and release readiness are not certified.
Preserved Claude's implementation at `b40679bcd9805beec3c671dea55293ed4d4287b3` on
`feature/planned-oco-source-20260913`, worktree
`C:\SecureOpsBuild\secure-ops-planned-oco-source-20260913`.
The original task baseline remains `38f6941fbcd81b158e89dab16f814f0ae34a2696`.
The user's replacement ceiling is 5,000 total additions plus deletions from that baseline,
including Claude's 1,999 additions, generated OpenAPI, SQL, tests and this documentation.
The delivery commit and final counted diff are reported in the task closeout, not inferred from build metadata.
Claude's source-session final handoff was verified before editing; no unrelated process was stopped.

## Repairs and behavioral evidence

1. Apply previously committed the draft before writing overrides/audit. `SqlAnnouncementStore.SaveAsync`
   now admits the reviewed write inside its existing Serializable transaction and owner application lock.
   Source snapshot identity/owner/captured time and expected override version are checked there;
   draft revision, override version and both `AnnouncementDraftSaved`/`AnnouncementSourceApplied` audits
   commit together. SQL trigger failures at each of these four stages leave all previous values intact.
   Stale draft, stale override and previously applied/older snapshots leave no revision or success audit.
2. Submission previously accepted conflicting input and could strand a committed row before enqueue.
   Owner/draft/key uniqueness now compares ordinal profile and trimmed OCO reference: identical input
   returns the same logical job, different valid input returns an explicit conflict. SQL plus submission
   audit commits first as durable dispatch intent. A 60-second SQL reservation fences acknowledgments;
   enqueue/ack failure keeps the intent recoverable. Tests inject failure before enqueue, after enqueue
   and during SQL acknowledgment, then repeatedly recover. Physical Hangfire duplicates are permitted;
   they cannot create another logical submission, terminal result or automatic application.
3. Running previously had no crash recovery. Claims now have SQL-clock lease, fresh AttemptId and count.
   Completion requires Running plus the current unexpired attempt. Only expired (or legacy null-lease)
   executions can be reclaimed; no global Running reset exists. Startup and minutely Hangfire recovery
   scan at most 100 due intents, on the same dedicated queue. SQL tests cover overlapping attempts,
   obsolete completion and completion-audit rollback. The real Worker was killed during collection,
   restarted, and recovered after its actual 90-second lease; no SQL timestamp reset was used in that journey.
4. Applying source fields previously changed the recipient profile even when recipients were declined.
   `SourceOverrides.Profile` remains the accepted recipient baseline; `AppliedJobId` identifies source
   provenance separately. `ApplyRecipients=false` preserves To/Cc, baseline and manual/removal arrays.
   API acceptance covers decline, subsequent profile switch, explicit review, manual additions/removals
   and To/Cc overlap normalization. Distribution-request recipients do not authorize final delivery.
5. First-dot splitting destroyed dotted dates and fractional seconds. Fixture/shared and Turuncu Hat
   paths now retain bounded original text, including whitespace. Only recognized ISO values with an
   explicit offset establish resolved evidence; local/dotted values remain unresolved. Malformed/reversed
   values are invalid, missing values remain missing; oversize evidence is rejected. No date, offset,
   OCO approval, collection/OCO scope equivalence or restart time is inferred into a draft.

Additional tests exercise missing/empty/ambiguous/partial results, exact SET versus KEY parsing,
duplicate rows/keys, supported collection paging, all 205 services with device provenance, profile
configuration, bounded lookup concurrency/cancellation and authorization revocation before execution.
Final review also added fail-closed `AnnouncementSourceRead` audit for profiles, status and proposal:
the real API rejects all three reads when an injected SQL trigger prevents that required audit.
Turuncu Hat multi-page results remain explicitly incomplete: no unapproved continuation protocol is invented.

## API contract

All paths below are relative to `/api/v1/announcements`. All require authentication, approved persisted
`Announcements.Drafts` capability (`Policies.CanDraftAnnouncements`), both module/source flags and
owner isolation. Capability alone never grants another owner's draft/job. Worker rechecks persisted access.

| Method and path | Request / response |
| --- | --- |
| GET `source/profiles` | 200 `MaintenanceProfileChoice[]`: name, label, configuration state, missing fields; no collection IDs |
| POST `{id}/source/jobs` | `AnnouncementSourceSubmission(profile, ocoReference, submissionKey)` -> 202 `AnnouncementSourceJobStatus` |
| GET `{id}/source/jobs?jobId={jobId}` | 200 `AnnouncementSourceJobStatus`; omit jobId for latest owned job |
| GET `{id}/source/jobs/{jobId}/proposal` | 200 `AnnouncementSourceProposal`, including draftVersion and overrideVersion |
| POST `{id}/source/apply` | `AnnouncementSourceApply(jobId, expectedVersion, fields, applyRecipients, applyAffectedServices, expectedOverrideVersion)` -> 200 `AnnouncementSourceApplyResult` |

Status includes jobId/draftId/profile/OCO/state/terminal/timestamps/errorCode/duplicateOf. States:
Queued, Running, Succeeded, Partial, Failed. Proposal contains `ProposedField[]`, service candidates,
`RecipientDifference` for To/Cc, `SourceCompletenessView`, highPriority, audience and stale flag.
Apply returns new version, appliedFields, recipientsApplied, affectedServicesApplied and skippedFields.
Only Scope, Impact, Checks, Description, AnnouncementDate and OcoReference are accepted in `fields`;
unrequested fields stay unchanged, unavailable values are skipped. No adapter currently proposes
AnnouncementDate. WorkStart/WorkEnd require operator review; restart values are unavailable.
Affected services require v2 template and nonempty proposed services; otherwise they are explicitly skipped.
Unknown JSON members on submission/apply and unknown field names are rejected, not silently ignored.
`audience=DistributionRequest` is not a final-announcement audience or permission to send.

Source ProblemDetails includes code, fields and correlationId. Authentication/model-binding errors use
the existing middleware/ASP.NET validation contract. Source service mappings:

| HTTP | Codes |
| --- | --- |
| 403 | AccessDenied |
| 404 | AnnouncementNotFound, AnnouncementSourceJobNotFound |
| 409 | AnnouncementConflict, AnnouncementSourceStale, AnnouncementSourceOverrideConflict, AnnouncementSourceSubmissionConflict |
| 400 | AnnouncementSourceInvalid, AnnouncementInvalid, AnnouncementIncomplete |
| 503 | AnnouncementsDisabled, AnnouncementSourceDisabled, AnnouncementSourceProfileUnavailable, AnnouncementSourceJobHostUnavailable, AnnouncementSourceIncomplete, AnnouncementSourceUnavailable; other source/draft availability errors |

Terminal job errorCode may also report AnnouncementSourceTimeout, AnnouncementSourceAttemptsExhausted,
AnnouncementSourceSnapshotTooLarge, AccessDenied or an adapter's explicit failure code:
AnnouncementSourceAuthenticationFailed, AnnouncementSourceCollectionMissing,
AnnouncementSourceConfigurationUnavailable, AnnouncementSourceInvalidResponse, AnnouncementSourceRejected,
AnnouncementSourceDisabled or AnnouncementSourceUnavailable. Status GET
still returns 200 for a readable failed job; requesting its absent proposal returns its failure code/503.
OpenAPI and `AnnouncementSourceContracts.cs` are the DTO authorities, not a UI-inferred contract.

## SQL and configuration

Migration 016 is unchanged. Apply immutable 001-016 then additive
`017-announcement-source-recovery.sql`; 017 adds dispatch/lease fields and index without data deletion.
Migration inventory assertions now require all 17 intended scripts, including explicit 016/017 entries.
The existing local harness `scripts/powershell/Test-ResourceCatalogueSql.ps1 -DatabaseSuffix
OcoSource<unique> -IncludeAnnouncementSources` creates a fresh prefixed database and runs 001-017.
Hangfire is a separate provisioning step: the installed 1.8.6 package's `tools/install.sql` creates
schema version 9. Runtime `PrepareSchema=false` is mandatory; true is rejected by composition.

Runtime delta: SELECT/INSERT/UPDATE on SourceJobs and SourceOverrides, SELECT/INSERT on DraftRevisions,
existing audit INSERT and existing persisted access/session permissions. Hangfire needs DML on its
dedicated schema; no runtime CREATE/ALTER/DROP permission. Restricted SQL-user enqueue was executed
with no CREATE TABLE, audit UPDATE/DELETE, draft UPDATE or SourceJobs DELETE permission. The full
local hosts use the test owner's integrated identity, not proof of a corporate service-account grant set.

API and Worker must share one dedicated source DB/Hangfire schema/queue and matching profile configuration.
Do not point this recovery scanner at another task's resources. Queue must be explicit, valid and <=20
characters. Both Announcements:Enabled and AnnouncementSource:Enabled default false; Hangfire:Enabled
is independently required to submit. Fixture adapters are local-only. Production provider and protected
connection/profile values are configuration-owned, never copied into this handoff.
Job timeout clamps to 30-3600 seconds; execution lease is timeout+60 seconds. Maximum source attempts
defaults to 3, clamps to 1-5; the next claim fails exhausted before source reads. Worker count defaults
to 4, clamps to 1-16; device lookup concurrency/timeouts and collection ceilings are bounded separately.

## Executed acceptance

Evidence root: `C:\SecureOpsBuild\source-repair-acceptance-20260914` (retained outside Git).
Fresh databases on the already provisioned `(localdb)\SecureOpsResourcesV1`:
`SecureOps_ResourcesV1_OcoSourceAuditRegression20260914_f91c` (regression) and
`SecureOps_ResourcesV1_OcoSourceAuditHosts20260914_a09d` (separate host journey), each 001-017 plus Hangfire 9.

| Check | Result |
| --- | --- |
| Final Release solution build | 0 warnings/errors, 10.46s |
| Full Release regression, fresh SQL, no-build/no-restore | 1263 unit passed (3.04s TRX wall time); 285 integration passed (9.55s); 1 explicit host opt-in skipped here |
| Separate actual API/Worker acceptance | 1 passed, 113.21s; that skipped opt-in executed separately, no outstanding skipped test |
| Earlier private-constant naming checkpoint | 33 focused unit + 10 focused integration/SQL/OpenAPI passed, 0 failed/skipped |
| OpenAPI supported opt-in generation then normal comparison | Both passed; 541 additions, no deletions; no existing path/schema altered or removed |
| Full format verify, no waiver | Fails on 123 unique existing findings in 58 untouched files; zero task-file findings (`format-audit-closeout/format-report.json`) |

Final host evidence directory: `source-27f9e15734584d6fb50df75cb28be3c2` under that root.
`acceptance.json`, per-process logs, `reviewed-v2.html` and `reviewed-v2.eml` retain the actual journey.
Loopback port 58208, queue `source-a15a4b8d`, API PID 27012; owned hosts are stopped after the test.
Draft `de96e1fd-fe26-42ec-b861-ce2f76c3f8d8` advanced through explicit reviews to version 5.
First job `a8caa7ef-90f4-4bdc-b194-7ec0ce78a44f` produced the persisted snapshot and reviewed v2 export.
Interrupted job `ad475a01-3167-4bfd-92ee-b9df8e493837` recovered with attempt count 2; obsolete completion
and a real duplicate Hangfire invocation left its outcome/draft unchanged. Anonymous/non-capable and
cross-owner requests were denied; another owner's draft stayed unchanged. No mail sender is configured
or invoked by this workflow. Saved preview/MIME were parsed/asserted through existing authenticated APIs.
Final TRX files are in `audit-regression` and `audit-hosts`; earlier successful checkpoints remain retained.

Replay: build Release; provision separate fresh DBs and Hangfire; set guarded
`SECUREOPS_SQL_TEST_CONNECTION` to the synthetic DB. Full regression uses
`dotnet test SecureOps.sln -c Release --no-build --no-restore -m:1 -nodeReuse:false`.
For host acceptance additionally set `SECUREOPS_SOURCE_HOST_ACCEPTANCE=1` and a unique
`SECUREOPS_SOURCE_EVIDENCE`; filter integration tests by `AnnouncementSourceAcceptanceTests`.
The helper owns unique port/queue/fixture/log paths and starts/stops only its child API/Worker processes.
OpenAPI generation uses `SECUREOPS_UPDATE_OPENAPI=1` with the existing
`Test_OpenApiDocument_MatchesCheckedInUiContractSnapshot` test; unset it for the normal comparison.

Earlier failed attempts were not counted as acceptance: 017 initially needed a GO compile boundary
(failed database retained), first host startup exposed missing test DataProtection ApplicationName,
and test-only SQL impersonation required pooling disabled. Those harness defects were fixed and rerun.
The final lease wait is expected bounded recovery latency, not an unexplained hang. No full suite was
repeated for documentation-only edits. The added fail-closed source-read audit required the final
fresh-database regression and API/Worker replay recorded above.

## Integration handoff and remaining gates

Do not merge/cherry-pick into the active editor/send branch as part of this task. Reconcile shared
`AnnouncementService.cs`, `SqlAnnouncementStore.cs`, Infrastructure DependencyInjection, API/Worker
Program composition, Shared source DTO/configuration, domain source models, OpenAPI, ADR-0021,
SQL inventory/harness and migrations 016/017 against the editor branch's actual latest state.
Keep the transactional reviewed-save callback when integrating newer send/history persistence.
UI must carry both proposal versions, show completeness/staleness and recipient differences, and obtain
explicit apply choices; it must not auto-apply after job completion or treat profile recipients as final
delivery approval. Recheck source override provenance when connecting send preparation.
The other worktree's HEAD was `422c940cd642537570f23f1e052dd4e0a01195cb` at the final read-only check;
its migration directory contained 001-015, with no 016/017 collision then. This is not a future conflict
guarantee. Recheck its HEAD, migration inventory and shared-file
diffs immediately before integration; do not reuse that checkout's hosts, databases or evidence.
Corporate SCCM/Turuncu Hat mappings, supported remote paging, timezone evidence and service identity
permissions remain controlled-environment acceptance. UI connection, Outlook/VDI rendering, original
branding and later send-preparation/history integration remain unverified here. No corporate call,
SMTP, deployment, branch deletion or In Use/OR-to-SDM modification was performed. Full repository
format gate remains blocked by untouched baseline findings; this is not complete release readiness.
