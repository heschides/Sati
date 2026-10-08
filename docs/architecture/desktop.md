# Desktop presentation and coordination

**Status:** current architecture reference, October 8, 2026. Detailed rules remain with the linked canonical owners.

Implemented: constructor injection, awaited navigation/workflow writes, LatestRequestTracker/account publication guards, coalesced date refresh with dirty-editor deferral and serialized personal saves. JournalDocument/JournalFlowDocument own portable marked personal content and its client adapter. Personal checklist totals do not control clinical completion or billable units.

Presentation privacy/shielding cannot grant authority or retract released data. Durable bounded breadcrumbs support crash investigation; they do not establish a stack-overflow cause. Workload projection and query pressure fixes remain measurable backlog items.

## Canonical detailed owners

- [DISPLAY_MODES_DESIGN.md](../../DISPLAY_MODES_DESIGN.md)
- [LOGGING_DESIGN.md](../../LOGGING_DESIGN.md)
- [AI_CASE_NOTE_RULES.md](../../AI_CASE_NOTE_RULES.md)
- [DOCUMENT_TEMPLATES.md](../../DOCUMENT_TEMPLATES.md)

Original feature rationale and source references are retained in the
[pre-consolidation architecture snapshot](../archive/2026-10-08/ARCHITECTURE.md).
[Architecture index](../../ARCHITECTURE.md) · [Readiness evidence](../readiness/README.md)