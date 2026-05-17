# 16 — Future Vision

This document captures longer-term product direction that is worth preserving now but is **not committed delivery scope** for Phases 1–6.

It exists to prevent useful ideas from being lost while also keeping the active roadmap disciplined.

## Positioning

SecureOps is first an operations platform: structured alarms, diagnostics, audit, and explainable reports. A conversational assistant becomes valuable only after that foundation exists.

The future direction is a secure, internal assistant that can answer bounded operational questions from approved sources, for example:

- "List applications on server X."
- "Which IIS sites and app pools are hosted on server X?"
- "Which servers run application Y across production and non-production environments?"
- "Show the latest known IIS site, app pool, and binding metadata for server X."

Those are not all AI problems. Some should be solved deterministically through inventory, reporting, and queryable metadata before any LLM is introduced.

## Candidate Future Surfaces

### Browser Experience

A browser UI is the most natural baseline because SecureOps already has a Blazor Server application, authorization model, and audit trail. If a future assistant exists, the browser surface should remain the first place to prove:

- data quality,
- permission boundaries,
- answer traceability,
- and operational usefulness.

### Teams Experience

A Teams-facing assistant is a plausible later surface for operators who already work from chat during shifts. It should be considered an access channel, not the system of record:

- answers must still come from SecureOps-owned data and approved integrations,
- authorization must remain equivalent to the browser experience,
- and responses must link back to durable records rather than becoming an unaudited side channel.

## Capability Layers

| Capability | Earliest sensible phase | Why |
|---|---|---|
| Inventory-backed answers such as "list applications on server X" | Phase 6+ | Requires stable inventory, normalized metadata, and reporting surfaces; AI is optional |
| IIS site / app pool / binding metadata exploration | Phase 6+ | Depends on structured collection and ownership of the underlying data model |
| Cross-environment inventory questions | Phase 6+ | MVP explicitly focuses on a narrow pilot; cross-environment breadth should wait until the core model is proven |
| Natural-language assistant in browser or Teams | Phase 7+ | Requires the self-hosted AI layer, masking, audit, and stakeholder approval gates |

## Why This Is Not Earlier Scope

The temptation is to start with a chat interface because it is visible. That would be the wrong sequencing here:

1. Without reliable structured inventory, the assistant would answer from incomplete or inconsistent data.
2. Without Phase 6 reporting maturity, many useful questions cannot be answered deterministically or checked against a known truth source.
3. Without Phase 7 security gates, conversational AI would violate the project's own trust model.
4. Without validated operator workflows, surface choice such as Teams versus browser would be guesswork.

For those reasons:

- Phases 1–6 continue to build deterministic platform capability.
- Phase 7 remains the first AI implementation phase.
- This document records direction only; it does not change current scope.

## Open Questions To Revisit In Phase 6

1. **Teams integration:** Is Teams only a notification channel, or is there a justified later need for an assistant surface there?
2. **Browser UI:** Which questions are better answered by direct UI views and filters rather than by chat?
3. **MCP protocol consideration:** If a future assistant needs a standardized tool interface across approved systems, would an MCP-style protocol materially help, or would it add needless abstraction?
4. **Inventory ownership:** Which source becomes authoritative for application-to-server mappings and IIS metadata?
5. **Cross-environment scope:** Which environments are operationally useful to include first after the pilot proves out?

## Relationship To Existing Documents

- `docs/10-ai-rag-strategy.md` remains the implementation strategy for Phase 7 AI/RAG work.
- `docs/02-roadmap.md` remains the committed delivery plan.
- This file is intentionally a future-direction note, not an ADR and not a phase plan.
