# Service Accounts Module — Sanitized Business Specification

Source: the owner-supplied implementation plan and data-entry note (report cutoff 2026-09-19).
Those originals contain real account, person and organization data and are **not** committed.
This file keeps only the rules and aggregate acceptance expectations. Person and account names
are replaced by roles or synthetic labels. Aggregate counts are reconciliation controls for the
one historical migration package, never production constants.

## Scope of the first delivery

Import preview and approved merge, account list, account detail timeline, team-scoped
authorization, request/action/communication/finding records, handover and gMSA-transition
tracking, and weekly/manager reporting. The module manages plans, external references and
evidence. It never deletes AD accounts, rotates passwords, converts to gMSA, scans servers,
collects secrets or analyses mail with AI.

Account owner team, executing team, consuming team, follow-up person, contact person and the
coordinator are different roles and must never be collapsed into one "responsible" field.
Periodic coordination lists are risk/observation lists, not full AD inventories: an account
absent from a newer list is **not** deleted, fixed or out of scope. History is kept and only a
"not seen in this source batch" observation is added — and only when the importer declares the
list **complete** for named organizations (children included, optionally one domain) inside
their scope, with a dated source. Unknown (default) or partial coverage never implies absence.
A complete-list declaration that is contradicted by rows outside its population infers nothing.

## Business rules (implemented unchanged)

1. "Responsible assigned" is only an assignment; it does not mean work started or finished.
2. The expected action comes from a controlled vocabulary; free text is kept separately and
   never drives automatic completion.
3. Plan, reported action and verification are three separate stages. A mail reply or a source
   password timestamp does not change any of them by itself.
4. Verifying an action updates the same action identity; it is not counted as a second
   performed action. The change is kept in audit.
5. `Intermediate step` keeps the account open (password change is usually one).
   `Account closure` record kind alone does not create a closure.
6. A verified closure needs result Verified, closure record kind, actual action date,
   verification date, verifier and evidence. The verification date cannot precede the action
   date. A deletion closure additionally needs an OR number.
7. OR, OCO and Jira are distinct record types; none substitutes for another. OR is a service
   request/operation record, OCO a change record; the application's own IDs are independent.
8. A performed-action report may be counted as a *report* even with incomplete evidence; it is
   not a verified closure. An undated performed report is never spread into a week by guess.
9. Adding an action never closes the related request automatically. The user explicitly closes
   it; the server checks the completion conditions of the expected work.
10. Account handover and identity transition are separate. Handover acceptance does not mean
    gMSA suitability or implementation. An account is never considered gMSA-suitable just
    because MS SQL is mentioned; authentication, service type, dependencies and DBA/application
    approval are required.
11. Blank source cells never erase existing ownership, person, OR, OCO, plan or notes. Clearing
    is a separate, reasoned user action.
12. Source observation timestamps differ from action times. "Last password change" is an AD
    observation, not evidence that a person performed an action.
13. An account can have several open requests; each is shown separately. The list shows open
    request count and nearest target date so one summary does not hide others.
14. A general team mail is a real communication even without an account name; no fake account
    is created. One mail may link to many accounts and counts once in physical mail totals.
15. A finding is not an action. An unreachable server or a scan without a match does not mean
    "unused / may be deleted".

## Roles (business semantics; access is granted through persisted application access)

| Role | Read | Write / decide |
|---|---|---|
| Coordinator | Authorized directorate scope | Import preview/commit, assignment, requests, reports, ownership conflict resolution |
| Team lead | Own team scope and incoming handovers | Assign people within own team, plans, handover accept/reject |
| Team member | Own team / assigned work | Plan and action reports, mail/finding/evidence; cannot change another team's ownership |
| Verifier | Authorized verification scope | Action verification and closure checks |
| Manager | Authorized reporting scope | Read-only summary, detail, Excel/PDF export |
| System administrator | Settings | Scope grants and dictionaries; cannot silently change business results |

Authorization applies to API, exports, attachments, import commit and background jobs, not only
buttons. Knowing an account name must not reveal an out-of-scope record.

Visibility is not authority. Organization-level scope or the confirmed owner team is
**responsible** for an account (account attributes, ownership, new/other requests, closing,
findings, verification). A team that sees an account only because an open request targets it (or
a handover is proposed to it) is a **participant**: it may update its own open request (dates,
notes, follow-up; not retarget it or change its action), report actions linked to it, attach
evidence to it and record mails, and nothing else. Participation ends when the request closes.

Manually created accounts start with a provisional identity like imported ones; a typed or later
filled domain does not confirm it. No new user/password
store is created; team membership is established through an authorized administration screen.

## Import semantics

Source kinds: coordination list (Book1 headers), DBA handover (DBA headers), a team return file
with not-yet-seen headers (generic bounded mapping only), and the legacy tracking workbook / JSON
migration package. The user enters the source report date and scope; upload time is never the
source date. Files are validated for type, signature, size, decompression, rows and cells;
formulas, macros and external links are never executed or refreshed.

Preview classes: new / existing-observation update / same / conflict / invalid / not seen in
this batch. Old and proposed values are shown side by side. Source observation fields become
new observations; an owner team from the source fills an empty confirmed assignment only with
authorized approval and conflicts go to a decision; manual person/OR/OCO/plan/action/evidence
fields are never overwritten; absence is only a not-present observation, inferred solely from a
validated complete-list declaration (preview shows the declared population and any rows outside it).

The commit revalidates preview version, file hash, row decisions and row versions, and writes
batch state, observations, approved changes, audit and outbox in one short transaction. A
change after preview returns 409 and requires a new preview. Replaying the same file, profile
and mapping creates no new accounts, requests or mails. An older source observation never
moves the latest observation backwards. Request creation is idempotent per account, source
semantic key and action type.

Evidenced headers:

| Source | Headers |
|---|---|
| Coordination list | Kullanıcı Adı; Son Parola Değişiklik Zamanı; AD veya LDAP Son Oturum Açma Zamanı; AD Son Oturum Açma Zamanı; Organizasyon; Grup Direktorlugu; Yorum |
| DBA handover | Kullanıcı Adı; Ekip; Kullanan_Ekip; WASAS_Devir |

Only rows with the handover flag `OK` enter the handover cohort; a blank flag does not mean the
account definitely stays with the source team.

## Metric definitions (one implementation)

| Metric | Definition |
|---|---|
| Unique accounts | Distinct AccountId in scope, not import rows |
| Accounts with assigned person | Distinct AccountId with a current confirmed person assignment |
| Unique responsible persons | Distinct PersonId in those assignments |
| Performed-action reports | Non-void Performed/Verified actions; counted once per action identity |
| Weekly actions | ActualAt local business date in [Monday, next Monday) Europe/Istanbul; after asOf excluded |
| Verified closures | Distinct AccountId passing closure rules; week by verification date |
| Mails | Valid incoming/outgoing, non-draft distinct CommunicationId; N links are not N mails |
| Dated open plans | Open requests with a valid plan range; request count and distinct account count labelled separately |
| Awaiting date | Open request without a valid start/end or with undetermined action |
| Overdue | Open request whose end precedes the report date (calendar days, shown) |
| Handover reported / accepted | Proposed cohort / records with accepted date and authorized acceptance evidence |
| gMSA pending | gMSA-targeted cohort without a completed gMSA action, with suitability breakdown |
| Team workload | Owner team and request target team shown as separate columns |
| Open findings | Open/in-review findings; never counted as closure |

Weekly reconciliation: in-period + earlier + unknown date + after-asOf/excluded equals the
selected total. Events after the cutoff never enter a past report. A late historical event
updates the live report; a sent snapshot never changes. The organization filter applies equally
to summary, detail, export and job queries.

## Reminders

In-app notifications and coordinator message drafts only. Automatic mail requires a configured
corporate sender, recipient scope and period; this specification is not mail approval. Rule
periods are parameters; business days need the corporate holiday calendar, and no SLA is
invented. Multi-instance claim/lease, an outbox unique on (request, rule, due date, channel),
bounded retry and a visible dead-letter list are required.

## Acceptance scenarios (aggregate expectations from the historical package)

1. After the first migration: 390 unique accounts; the coordination list has 358 rows. The
   historical 357-existing / 1-new split needs the unavailable previous baseline; all earlier
   accounts are preserved.
2. Re-importing the same files does not increase account/request/mail counts; an older source
   observation does not move the latest observation back.
3. Handover flag OK gives exactly 81 accounts; the other 277 rows do not enter the cohort.
   Without real acceptance / gMSA evidence those counters stay zero.
4. A Linux cohort of ten accounts: eight dated password plans between 30 Sep and 30 Dec 2026,
   two closure reviews only; completed password/deletion totals do not increase.
5. Proposed ownership for four accounts of one team and one more account remains
   proposed/awaiting confirmation; mail senders do not become owners.
6. Case/Turkish-letter variants of the same verified person map to one person; two people with
   the same name but different UPNs remain two people.
7. The same account name in two domains yields two AccountIds; SID/domain conflicts are never
   merged automatically.
8. Existing owner/OR/OCO/plan survive a blank new source value; ownership conflicts go to an
   audited decision.
9. Several open requests per account are all visible; closing one does not close another;
   account totals do not change.
10. Monday 00:00 and next-Monday boundaries, a timed Sunday event and the cutoff day are
    tested; undated actions are counted separately; no fake 1900-01-01 is produced.
11. Performed→Verified keeps the same action count; deletion without OR cannot be a verified
    closure; verification before the action date is rejected.
12. One mail linked to ten accounts counts as one physical mail and ten links; a repeated
    provider message ID is the same mail; two distinct same-subject real mails are both kept.
13. Findings and failed/missing scans do not increase completed work; missing scope/evidence is
    visible.
14. Unauthorized team access to account/API/export/attachment is refused; another team's
    record is not updated; report filters are applied on the backend.
15. Two users updating the same rowversion: one succeeds, the other gets 409 and the first
    change survives. Running the same commit/job twice yields one result.
16. Imports with invalid rows are visible with partial-approval rules; a transaction failure
    creates no half account/activity; file/row/formula-injection tests exist.
17. A sent weekly snapshot is unchanged when reopened; the live report places a late action in
    the correct week.
18. The XLSX export opens in desktop Microsoft Excel without a repair prompt; formula/
    shared-formula XML and dates are checked; PDF and XLSX reconcile to the same snapshot.

Legacy ownership reconciliation control: the main account inputs name a responsible person for
40 accounts using 7 distinct name labels. The legacy combined view reached 53 accounts / 9
people by taking a request follow-up person for 13 otherwise unassigned accounts. A follow-up
person is not the owner: the module keeps that projection only as a labelled legacy figure and
routes ownership and identity mapping to review.
