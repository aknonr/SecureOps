# ADR-0005 — AI / RAG as a Later Phase

**Status:** Accepted
**Date:** 2026-05
**Decision makers:** Project owner; final AI rollout subject to security team and management approval

## Context

There is organizational interest in adding AI capabilities to the operations platform. Operators could benefit from:
- Past-incident summaries
- Pattern highlighting
- Shift report drafts
- Runbook-aware suggestions

But:
- AI without structured data hallucinates.
- AI on production data requires masking, audit, and infrastructure decisions.
- Stakeholder trust (Bilgi Güv, Siber Güv) must be earned through earlier phases first.
- A single developer cannot maintain both the operational platform and an AI subsystem at the same time.
- Public AI services (OpenAI, Anthropic, Google) are not acceptable for this data.

## Decision

**AI/RAG is a separate phase (Phase 7), conditional on hard pre-conditions, with separate budget, and self-hosted only.**

### Hard Pre-Conditions

Phase 7 begins only when **all** are true:

1. Phase 1–6 are stable in production (90+ days).
2. At least 6 months of structured audit and diagnostic data accumulated.
3. Bilgi Güvenliği approval of data flow and masking.
4. Siber Güvenlik approval of data flow and infrastructure boundary.
5. Budget approved for GPU server and storage.
6. Data masking pipeline implemented and tested.
7. ADR-0005 amended with concrete model, hardware, and rollout plan.

### Hard Constraints

- **Self-hosted only.** No OpenAI, Anthropic, Google, Cohere, or any cloud LLM endpoint.
- **Data masking mandatory.** No production identifiers reach the LLM unmasked.
- **AI suggests, does not act.** No tool-calling, no state changes via LLM.
- **Full audit.** Every interaction recorded in `audit.AiAuditLog`, same retention as operational audit.
- **RAG grounding.** Every response anchored to retrieved context; no answers from training data alone.

## Alternatives Considered

### Use OpenAI / Anthropic / Google AI in production

Rejected:
- Sensitive operational data must not leave the organization.
- Regulatory and compliance considerations.
- Vendor dependency for a long-lived system.
- Contradicts the existing stance committed to management.

### Skip AI entirely

Rejected:
- The data foundation makes AI assistance feasible.
- Useful narrow use cases exist (summarization, pattern highlighting, draft authoring).
- Self-hosted AI mitigates external-vendor risk.

### Build AI in Phase 1 in parallel with the operational platform

Rejected:
- Single-developer capacity.
- AI without data is a demo, not a tool.
- Confuses the value story for stakeholders.

### Use Microsoft Copilot for Security

Rejected for now:
- Cloud-hosted; data lineage analysis required before any approval.
- Could be reconsidered with explicit ADR if Microsoft can guarantee on-premise / sovereign data path.

## Consequences

### Positive

- Phase 1–6 deliver value independently.
- AI introduction is deliberate and risk-managed.
- Stakeholder approvals earned through visible, demonstrable phases.
- Self-hosted-only stance is clear and defensible.

### Negative

- AI value is deferred by 18+ months.
- Requires GPU hardware procurement.
- Requires sustained masking discipline.

### Neutral

- The architecture supports adding the AI service without restructuring earlier phases.

## Future Decisions (Documented in Subsequent ADRs)

When Phase 7 starts, separate ADRs will record:

- Final model choice (Llama 3.1 / Qwen 2.5 / Mistral / other).
- GPU hardware sizing.
- Vector DB choice (Qdrant vs pgvector).
- Embedding pipeline schedule.
- Operator-facing UI design.

## Implementation Notes

- `.cursor/rules/080-ai-rag-future-phase-rules.mdc` enforces the boundaries for agent-assisted development.
- `docs/10-ai-rag-strategy.md` is the full strategy document.
- `docs/05-security-model.md` defines the masking requirements.

## References

- `docs/10-ai-rag-strategy.md`
- `docs/05-security-model.md`
- `.cursor/rules/080-ai-rag-future-phase-rules.mdc`
