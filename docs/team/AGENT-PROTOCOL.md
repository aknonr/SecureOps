# Agent protocol — working through GitHub issues

How a contributor's AI agent (e.g. Claude Code on a Mac) and the owner's coordinator agent work together without
talking to each other directly. The shared channel is GitHub: issues, PRs and their comments. Everything is recorded and
nobody waits on a chat that the other side cannot see.

**Public repository.** Issues, PRs and comments are public. Synthetic data only: no real names, servers, accounts, mail
text or organisation details — ever. Internal details stay in the owner's private notes and mail.

## Roles

| Who | Does |
|---|---|
| Owner | Creates or approves issues, decides scope, merges (the `master` ruleset requires a PR and approval). |
| Coordinator agent (owner's machine, Windows) | Writes issue text on request, answers `soru` comments, runs Windows-only checks (LocalDB SQL, IIS, JEA, packaging) and posts the result on the PR, recommends merge. Never merges without the owner. |
| Contributor | Starts one agent session per issue, reviews what the agent wrote before pushing. |
| Contributor's agent | Follows the loop below. |

## The loop (contributor's agent)

1. **Read** the issue: `gh issue view <N> --repo aknonr/SecureOps --comments`. If scope or "done when" is unclear, stop and
   ask (step 6) instead of guessing.
2. **Plan comment** on the issue (short): files you will change, tests you will run, what you will not touch.
3. **Branch** from fresh `master`: `git switch master && git pull && git switch -c <branch>`; one issue = one branch = one PR.
   Bring later `master` changes in with `git merge origin/master` (no rebase, no force-push).
4. **Work and verify** per `CONTRIBUTING.md`: build, full unit suite, `dotnet format --verify-no-changes`. Run tests with the
   .NET 8 runtime (no roll-forward). List failing tests by name; do not change a test to make it pass on a Mac.
5. **PR**: `gh pr create --fill` using the PR template, `Closes #<N>` in the body. The report goes in the PR body:
   what changed, how verified (where and with which SDK/runtime), not verified, open questions, next step.
6. **Questions / blocked**: comment on the issue, add the label `soru`, and stop that part of the work. Do not work around
   a missing decision.
7. **Needs Windows**: add the label `windows-dogrulama` to the PR; the coordinator runs those checks and comments.
8. **Never**: merge, push to `master`, change repository settings, rebase or force-push, touch files outside the issue
   scope, or put internal data anywhere in the repo or on GitHub.

## Labels

| Label | Meaning |
|---|---|
| `gorev` | A task issue (created from the template). |
| `soru` | Agent or contributor is blocked on a question; the coordinator or owner answers in a comment. |
| `windows-dogrulama` | PR needs Windows-only verification by the coordinator. |

## Coordinator side (owner's machine)

The coordinator checks open `soru` issues and `windows-dogrulama` PRs (`gh issue list --label soru`,
`gh pr list --label windows-dogrulama`), answers in comments, runs the Windows checks on a clean copy merged with current
`master`, posts exact results, and tells the owner whether the PR is ready to merge. Migration and ADR numbers are handed
out by the owner/coordinator only (record them in `docs/team/WORK-LOG.md`).

## Pano

İşlerin tek ekrandaki görünümü: [SecureOps — işler](https://github.com/users/aknonr/projects/2) (GitHub Project; sahibe ait, özel).
Üç orkestra (SA, TH, Ekip) ve sahip aynı panoya bakar.

- **Status:** Sırada → Çalışıyor → İncelemede → Bitti.
- **Orkestra:** işin sahibi (SA, TH, Ekip, Sahip). **İki orkestra aynı kartı almaz**: kart başkasındaysa önce issue'ya yorum yaz.
- **Ortam:** Mac yapılabilir / Windows gerekir / Kurumsal TEST/PROD.
- İşe başlarken kartı **Çalışıyor**'a, PR açınca **İncelemede**'ye taşı. Bitti'yi merge'den sonra sahip (ya da koordinatör) koyar.
- Kart başlığı ve issue gövdesi genel/sentetik kalır; kurum içi ayrıntı panoya yazılmaz.
- Panoya yalnız sahibin hesabı yazabilir; yetkin yoksa kart taşıma isteğini issue yorumuna yaz.
---

**Türkçe özet:** Ortak kanal GitHub. Görev = issue (`gorev`). Arkadaşın ajanı issue'yu okur, plan yorumu yazar, dal açar,
işi yapar, PR açar ve raporu PR'a yazar. Takılınca `soru` etiketiyle issue'ya yazar ve durur. Windows testi gerekiyorsa PR'a
`windows-dogrulama` koyar; sahibin koordinatörü koşar ve sonucu yazar. Merge yalnız sahip. Repo herkese açık: yalnız
sentetik veri.
