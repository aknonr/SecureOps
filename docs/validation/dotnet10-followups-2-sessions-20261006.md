# F3 concurrent session termination, 2026-10-06

## Verdict

The first committed termination owns the timestamp, reason and terminal audit.
Single-session losers append no event and reread the exact persisted terminal state.
Logout/revoke return that state through the existing Ended response; validation denies
using the winning reason. Missing, active, reasonless or failed rereads fail closed.
SQL and InMemory apply the same winner policy. F4 remains OPEN in ADR-0014 Amendment 4.

## Evidence

Branch `codex/dotnet10-followups-2`, Windows, SDK 10.0.401, .NET 10, Release.
Private root: `C:\SecureOpsBuild\validation\dotnet10-followups-2-20261006`.
Fresh database: `SecureOps_ResourcesV1_F3RacesF2_20261006` on
`(localdb)\SecureOpsResourcesV1`, provisioned through the guarded 001-028 harness.

The new integration file `Sql/ResourceSqlTests.SessionConcurrency.cs` executes real
InMemory and SQL repositories. Two parallel tasks rendezvous immediately before
persistence so both single-session operations have Active snapshots, then contend
inside the real repository. SQL operations use separate repository instances and
connections. Fifteen pairs cover logout, administrative revoke, idle and absolute
validation, access-version change, access-disable batch and expiry sweep. Every pair
checks persisted terminal state, single matching terminal audit and returned state/error.
Eight simultaneous direct repository contenders separately check one winner and one
audit in SQL, atomic InMemory and non-atomic InMemory. Twelve reread cases prove fail-closed
behaviour for three service paths and four unavailable/invalid reread states.
Four further SQL/InMemory cases cover a logout/revoke whose first read already observes
the committed winner, returning its actual state without another event.

Reproduction command (explicit guarded connection in the process environment):
`dotnet test tests/SecureOps.Tests.Integration/SecureOps.Tests.Integration.csproj -c Release --no-build`
with filter `FullyQualifiedName~Sessions_Concurrent|FullyQualifiedName~Sessions_Losing`,
TRX logger and private results directory. Opt-in attribute forwards caller source/line
and SQL configuration passes the existing LocalDB/prefix/integrated-authentication guard.

The corrected test suite ran against the production files from commit `322311e`
before the F3 change: `f3-red-clean.trx` has 34 failed, 11 passed, zero skipped.
Failures include actual SQL duplicate audits and returned reason/timestamp differing
from the committed row. The same 45 cases pass after the fix (`f3-green.trx`), zero skipped;
the four later already-ended cases run in the final full Resource regression.
Thirty-three cases are parallel races (16 SQL, 17 InMemory); twelve are reread failures.

The first diagnostic `f3-red.trx` had 29 failures/4 passes but is NOT the clean SQL
regression proof: existing non-JSON synthetic audit rows caused the measurement query
to fail. The query now guards JSON parsing and restricts actor/session; no product
audit rows were rewritten. The corrected red run used a separate fresh database.

The final broader rerun uses NEW `SecureOps_ResourcesV1_F3FinalF2_20261006`, all 001-028
migrations, filter `FullyQualifiedName~ResourceSqlTests` and `f3-resource-final.trx`:
105 passed, zero failed/skipped (71 SQL opt-ins and 34 ordinary cases). This includes the earlier
transaction failure/cancellation/second-insert rollback and append-only regressions.
The required solution build/test/format/diff gates run after the F3 code commit;
their exact results are reported in the final task report.

## Blockers

No F3 blocker remains. Already-running request cancellation/Touch boundaries (F4),
browser journeys, published payloads, corporate authentication and IIS remain unverified.

## Minimal Safe Next Step

Owner reviews the local commit and handles push. F4 requires its own scoped decision/work.

## Risks

No schema/grant/migration/API-shape/UI change; no target server or corporate/provider
call. SQL checks the update count and conditionally inserts audit in the existing
transaction. InMemory checks active state under its existing gate before invoking
audit. Existing non-atomic InMemory batch-prefix limitations in Amendment 3 remain.
No audit UPDATE/DELETE or disabled append-only protection; no reason precedence policy.
