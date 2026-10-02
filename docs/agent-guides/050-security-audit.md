# 050 — Security and Audit

The hard rules are in `AGENTS.md`; the full model is `docs/05-security-model.md` and `docs/08-audit-model.md`. This guide adds the operational detail.

## Boundaries

- **Target servers.** Read-only until Phase 8, enforced twice: by the JEA endpoint and by code that refuses to build write commands. Allow-list: JEA section of `docs/05-security-model.md` (changing it needs an ADR).
- **Public AI.** No production data — alarm payloads, hostnames, logs, user identifiers, internal application or people names — to public LLM endpoints (`api.openai.com`, `api.anthropic.com`, `generativelanguage.googleapis.com`, `api.cohere.ai`, any other). Product AI is Phase 7, self-hosted (`080-ai-rag-future-phase.md`).
- **Audit store.** Append-only, enforced by `INSTEAD OF UPDATE, DELETE` triggers in `sql/schema/`; retention at least 36 months, configurable only upward. State-changing actions and privileged reads write audit events; failures to audit fail the action.
- **Audit framing.** Records what was done, when and by which process — not who performed best. No "operator performance", leaderboards or operator comparisons in UI, reports or names.

## Identity and access

- Authentication (Windows/Negotiate, or OIDC per ADR-0016/0017) establishes a principal only. Access comes from persisted approval → role bundle → capability (ADR-0010, ADR-0022); authorization is evaluated server-side on every request.
- Use the capability policies in `SecureOps.Shared.Auth.Policies` (and module policy classes); never inline group or role-name checks. UI visibility is a courtesy, never a boundary.
- Sessions are server-governed with idle and absolute limits (ADR-0014). Monitoring webhooks use HMAC-signed shared secrets; service-to-service uses Windows auth or mTLS — no static API keys or custom schemes.
- Rate limits (`ApiRateLimits`, `RateLimiting` config): a per-actor global limit plus named per-operation policies. Give every new sensitive or expensive endpoint a named policy; limits are validated at startup.

## Secrets and data

- Credentials come from PAM or a secret store at runtime; connection strings prefer integrated authentication. Nothing secret in source, config committed to the repo, logs or exception text.
- Network: internal only; Worker → servers over WinRM HTTPS (5986) with Kerberos; SQL over TLS; no application traffic to the public internet.
- Before data reaches any AI layer or external log sink, mask hostnames (stable hash), usernames (role), IPs (subnet), user paths and connection fragments — at the data layer, before serialization.

## When implementing

Check the change against these boundaries first; if it might cross one, stop and raise it with the owner. Add tests proving forbidden operations fail and that audit is written.
