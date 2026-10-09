# Decisions added after consolidation

[current-decisions.json](../current-decisions.json) is the canonical registry for new decisions.
It starts separately from the immutable 218-section import. Register each new dated Markdown
record with an unused DEC ID, title, path, status, evidence and explicit supersedes links.
Read the individual registered record for the decision and rejected alternatives.

| ID | Date | Decision | Status |
|---|---|---|---|
| DEC-0219 | 2026-10-08 | [Canonical documentation ownership and preserved history](2026-10-08-DEC-0219.md) | accepted |
| DEC-0220 | 2026-10-08 | [Evidence-based release readiness thermometers](2026-10-08-DEC-0220.md) | accepted |
| DEC-0221 | 2026-10-08 | [Standing documentation upkeep and bounded next-work invocation](2026-10-08-DEC-0221.md) | accepted |
| DEC-0222 | 2026-10-08 | [Recoverable agency faults in the note-abandonment worker](2026-10-08-DEC-0222.md) | accepted |
| DEC-0223 | 2026-10-08 | [One Claim.MD HTTP exchange deadline](2026-10-08-DEC-0223.md) | accepted |
| DEC-0224 | 2026-10-08 | [Finite-range note-worker agency discovery](2026-10-08-DEC-0224.md) | accepted |
| DEC-0225 | 2026-10-08 | [Note-worker completion cache follows the local day](2026-10-08-DEC-0225.md) | accepted |
| DEC-0226 | 2026-10-08 | [Preserve authoritative residual billing export errors](2026-10-08-DEC-0226.md) | accepted |
| DEC-0227 | 2026-10-08 | [Contain escaping API request exceptions](2026-10-08-DEC-0227.md) | accepted |
| DEC-0228 | 2026-10-08 | [Execute incident aggregation once](2026-10-08-DEC-0228.md) | accepted |
| DEC-0229 | 2026-10-08 | [Remove raw exceptions from two health checks](2026-10-08-DEC-0229.md) | accepted |
| DEC-0230 | 2026-10-09 | [Original-claim history, rejection and queued-reservation policy](2026-10-09-DEC-0230.md) | accepted; implementation pending |
| DEC-0231 | 2026-10-09 | [Retained EDI replay includes request kind](2026-10-09-DEC-0231.md) | accepted; source implemented |
| DEC-0232 | 2026-10-09 | [Shared original-claim delivery history admission](2026-10-09-DEC-0232.md) | accepted; sequential source implementation |

Accepted decisions must retain their evidence and earlier rejected reasoning. A change to a
decision creates a successor and supersession link; it does not rewrite the historical import.
[The imported decision index](../README.md) remains available for earlier records.
