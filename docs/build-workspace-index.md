# Local Source, Delivery and Evidence Index

Read-only inventory captured 2026-09-30; no cleanup performed. This is an index,
not a second requirements register. Full path/branch/HEAD/status snapshot:
`C:\SecureOpsBuild\validation\sa-followup-20260930\workspace-inventory.json`.

| Location under C:\SecureOpsBuild | Role / preservation |
|---|---|
| `secure-ops-sa-pinned-integration-20260929` | Active combined source; `feature/service-accounts-pinned-integration-20260929`; tested `a457a33d4fb34a6c21e675b81eed5f23b6ad470f`; later docs-only closeout is distinct |
| `secure-ops-sdm-integrated-test-20260928` | Published SDM `e997c5b`; clean; untouched |
| `secure-ops-sdm-target-preflight-20260929` | Target-preflight source, 5 dirty paths retained |
| `secure-ops-service-accounts-integration-20260929` | Original Claude/integration handoff, 127 dirty paths retained; NOT active editor target |
| `secure-ops-combined-test-delivery-20260915` | Historical completion source, 96 dirty paths retained |
| `service-accounts-review-20260929`, `service-accounts-claude-handoff-20260929` | Recovery/review material; original recovered branches remain intact |
| `delivery-review\2026-09-28-sdm-integrated-test\final` | Sealed deda848 SDM candidate: `candidate.json`, `payload-manifest.json`, `supporting-files-manifest.json`, `manifests\Api.sha256`, `Ui.sha256`, `Worker.sha256`; not installed |
| Other `delivery-review` roots | `2026-09-21-consolidated-test`, `2026-09-22-inuse-manual-verification`, `2026-09-22-inuse-wasas-activity`; retain sealed/history artifacts |
| `validation\sa-followup-20260930` | New TRXs, failed-run/deadlock evidence, manifest and worktree inventories; sibling logs retained |
| `validation\sa-pinned-20260930`, `sdm-integrated-20260928`, `sdm-target-preflight-20260929` | Historical b4 tests, sealed SDM local acceptance, private target observations respectively |
| `validation\sccm-diagnostics-20260921`, `diagnostics` | Security-held SCCM package evidence and historical private diagnostics; no execution |
| `release`, `archive`, `staging` | Historical rc6.3-rc6.26 archives/staging; unchanged |
| Remaining registered worktrees | Historical API/directory/platform, UI, In Use, OR-SDM and OCO branches; exact paths/SHAs in snapshot; 22 registered, 20 under this root; one missing path NOT pruned |

Tracked-path inspection found no bin/obj/TestResults/evidence/validation/staging
outputs or DLL/EXE/TRX/bundle/XLSX/certificate artifacts in source. Reviewed 19
delivery manifest files without rebuilding or hashing packages. Only flagged
supporting entries were three intentionally retained synthetic test TRXs in the
sealed SDM supporting manifest, not runtime payloads. Their bounded private-marker
scan found no known target/identity/record markers; this is not a universal secret
scan or new publication approval. Details: `manifest-path-review.json` beside the
inventory. Evidence, worktrees, recovery bundles and packages were not deleted,
moved, pruned, reset or recreated.
