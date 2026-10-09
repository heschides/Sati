# Desktop presentation and coordination

**Status:** current architecture reference, October 8, 2026. Detailed rules remain with the linked canonical owners.

Implemented: constructor injection, awaited navigation/workflow writes, LatestRequestTracker/account publication guards, coalesced date refresh with dirty-editor deferral and serialized personal saves. JournalDocument/JournalFlowDocument own portable marked personal content and its client adapter. Personal checklist totals do not control clinical completion or billable units.

Presentation privacy/shielding cannot grant authority or retract released data. Durable bounded breadcrumbs support crash investigation; they do not establish a stack-overflow cause. Workload projection and query pressure fixes remain measurable backlog items.

Local case-note drafting uses current-contact facts and the shared `CaseNoteDraftRules` validator.
Initial and repair prompts distinguish the JSON follow-up body from the renderer's section label
and require the supplied system no-follow-up object to remain exact. Facts and the safe baseline
use compact JSON without dropping keys, values or quoted string content; compact output instructions
target whitespace outside quoted strings. System and user prompts consistently permit either
a valid compact JSON plan or the exact unquoted `USE_SAFE_BASELINE` signal when uncertain, as
[DEC-0042](../decisions/records/2026-08-22-DEC-0042.md) already allows. This corrects an existing
JSON-only instruction conflict without forcing deferral. Recognition, parsing, validation,
warnings, two attempts, budgets, consumer/reset behavior and assertions are unchanged; human
comparison and explicit acceptance remain required. The final consistent-protocol model gate
completed all three original synthetic scenarios without warnings on this device; it does not
establish which raw response form produced a rendered baseline or a permanent native fix.
Earlier native cancellation and parse failures remain recorded, with cause/deadline unproved.
The inspected nonstreaming SDK path supplies a token to `Task.Run`; that alone does not establish
immediate interruption once native work is scheduled or begun. Formal native cancellation/device
evidence remains open under SATI-CLI-001. [The drafting standard](../../AI_CASE_NOTE_RULES.md)
owns style guidance; [current working release evidence](../readiness/work-evidence.md#2026-10-08--datt-local-model-prompt-gate-repair)
records actual failed baselines and model acceptance, limits and the remaining final release gates.

## Canonical detailed owners

- [DISPLAY_MODES_DESIGN.md](../../DISPLAY_MODES_DESIGN.md)
- [LOGGING_DESIGN.md](../../LOGGING_DESIGN.md)
- [AI_CASE_NOTE_RULES.md](../../AI_CASE_NOTE_RULES.md)
- [DOCUMENT_TEMPLATES.md](../../DOCUMENT_TEMPLATES.md)

Original feature rationale and source references are retained in the
[pre-consolidation architecture snapshot](../archive/2026-10-08/ARCHITECTURE.md).
[Architecture index](../../ARCHITECTURE.md) · [Readiness evidence](../readiness/README.md)
