# Active Directory Real-World Compatibility and Group Analysis

## Verified Compatibility Baseline

Controlled TEST evidence proves that the current IIS process identity can read exact normal, PAM-style, and service-shaped AD user objects, health attributes, managers, SPNs, direct `memberOf` backlinks, primary membership through standard AD tooling, exact groups, and explicit group members. One tested security/global group returned the same 12 direct users in SecureOps and `Get-ADGroupMember`.

These observations prove read access, not a corporate account classification. SecureOps returns AD object evidence as `User`; it never infers `Human`, `PAM`, or `ServiceAccount` from names, password policy, or SPNs. Zero SPNs is a successful empty result: `servicePrincipalNames=[]`, `servicePrincipalNameCount=0`, and `servicePrincipalNamesTruncated=false`.

## Membership Semantics

Microsoft documents that `memberOf` contains direct backlinks and excludes the primary group. SecureOps therefore reads server-returned `memberOf` DNs and resolves each exact group, then derives the primary-group SID from the principal's `objectSid` domain SID plus `primaryGroupID`.

Principal membership values expose `membershipKind`:

- `Direct`: an explicit `memberOf` backlink;
- `Primary`: the `primaryGroupID` relationship;
- `Transitive`: a group reached through two or more proven parent edges.

Group-to-member results retain explicit `member` link semantics. They can contain `User`, `Group`, `Computer`, or `Other`, but do not claim to include principals whose only relationship is `primaryGroupID`. Responses state `includesPrimaryGroupMembers=false` or `directMembersIncludePrimaryGroupMembers=false`.

References:

- <https://learn.microsoft.com/en-us/windows/win32/ad/security-properties>
- <https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-ada2/cc24555b-61c7-49a2-9748-167b8ce5a512>
- <https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-ada3/e12954a4-6865-4432-94e6-00c310ca87c0>
- <https://learn.microsoft.com/en-us/dotnet/api/system.directoryservices.accountmanagement.groupprincipal.getmembers>

## Compatibility Corrections

The prior membership graph called `Principal.GetGroups()` for the exact user and for every discovered parent group. The deployed failure was isolated to that AccountManagement graph path; equivalent `memberOf` reads worked. The AD adapter now uses exact server-returned `memberOf` relationships and explicit primary-group resolution. It does not accept raw LDAP, filters, credentials, or caller DNs.

Service evidence previously required both the principal/SPN read and the membership graph to succeed. A graph failure converted valid zero-SPN evidence into provider-unavailable. Principal/SPN evidence is now independently successful. When membership evidence fails, counts/traversal are null and `membershipEvidenceAvailable=false`.

## Group Analysis API

`POST /api/v1/directory/groups/analysis` requires `Identity.Groups.Members.View`. It returns:

- overview with category, scope, description, managed-by evidence, SID/DN, and optional timestamps;
- all explicit direct members;
- direct nested groups separately;
- bounded effective leaf users/computers/other principals;
- server-built nested-group nodes and edges;
- direct and bounded transitive parent groups;
- independent traversal metadata and `isComplete`.

Traversal is deterministic and bounded by timeout, depth, nodes, edges, effective members, provider limits, cycle detection, duplicate suppression, and cancellation. Partial evidence remains HTTP 200 with `isComplete=false`; consumers must not present it as complete.

`POST /api/v1/directory/groups/export` requires `Identity.Groups.Export`, assigned to Admin. `mode` is `DirectMembers` or `EffectiveMembers`; `format` is `Csv`. Export rejects partial results, sorts deterministically, enforces a row ceiling, prefixes spreadsheet formula characters, and audits only target hash, mode, format, row count, and outcome. XLSX is not added because CSV meets the bounded requirement without a new dependency.

## Configuration

- `DirectoryExplorer__MaxEffectiveMembers` (default `250`)
- `DirectoryExplorer__MaxExportRows` (default `500`)
- `RateLimiting__DirectoryGroupAnalysis__PermitLimit` (default `4`)
- `RateLimiting__DirectoryGroupAnalysis__WindowSeconds` (default `60`)
- `RateLimiting__DirectoryGroupExport__PermitLimit` (default `2`)
- `RateLimiting__DirectoryGroupExport__WindowSeconds` (default `60`)

Existing traversal, provider timeout, cache, and single-flight settings still apply. Purpose text is optional and is not part of cache or rate-limit identity.

## Limits of Evidence

Description, managed-by, category, scope, parents, and member composition are directory evidence only. They do not prove use in Windows local groups, ACLs, shares, IIS, services, scheduled tasks, or applications. Resource usage requires a separately approved read-only integration.

No AD write, SQL change, new migration, LDAP credential, or end-user password is introduced.
