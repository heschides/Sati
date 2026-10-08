# Sati — Decision index and supersession

The [dated record index](docs/decisions/README.md) preserves every original second-level decision
section and its reasoning under stable `DEC-` IDs. [The full original](docs/archive/2026-10-08/DECISIONS.md)
is byte-preserved. Dated observations are not current deployment facts. Retained records must be
interpreted with the explicit later amendments below, not “newest paragraph wins” by implication.

[Decisions added after consolidation](docs/decisions/current/README.md) use a separate registered
current-decision collection. New decisions never change the immutable import's count or reasoning.

## Governing boundaries

The authoritative assistant/product constraints remain in [AGENTS.md](AGENTS.md). Architecture
ownership is in [ARCHITECTURE.md](ARCHITECTURE.md), runtime policy/runbooks in their topic owners,
current observed environment facts in [DATABASE_ENVIRONMENTS.md](DATABASE_ENVIRONMENTS.md), and
activation/evidence status in [the readiness registry](docs/readiness/README.md).

The October 8 bounded note-worker failure policy is recorded in
[DEC-0222](docs/decisions/current/2026-10-08-DEC-0222.md), with its rejected alternatives and
verification limits. It does not change business-rule, route or persistence ownership.

The connector-owned HTTP exchange deadline and client registration's timer policy are recorded
in [DEC-0223](docs/decisions/current/2026-10-08-DEC-0223.md). Existing upload uncertainty and
coordination policy remain in force; the decision establishes no whole-pass or live-service bound.

The note-worker agency discovery policy is recorded in
[DEC-0224](docs/decisions/current/2026-10-08-DEC-0224.md): named 100-agency keyset pages,
a captured finite key range and bounded observed-growth recheck. This bounds per-query
materialization without establishing frozen membership, cache capacity or a fair wait.

The completion-cache local-date/storage lifecycle is recorded in
[DEC-0225](docs/decisions/current/2026-10-08-DEC-0225.md). Rotation under the existing run gate
retires earlier-day storage while preserving current-day retries/skips; current-day capacity,
measured memory, provisioning invalidation and fair-wait bounds remain open.

The shared billing export/replay gate's preservation of caller-authoritative residual compliance
errors is recorded in [DEC-0226](docs/decisions/current/2026-10-08-DEC-0226.md). Matching stored
exception provenance cannot waive remaining obligations. Service-date policy, exact-obligation
exceptions, Admin recovery and immutable transport/correction rules retain their existing owners.

The API request exception boundary is recorded in
[DEC-0227](docs/decisions/current/2026-10-08-DEC-0227.md). It contains raw escaping exceptions
before framework observability while retaining generic responses, safe correlation and best-effort
incidents. Independent sink redaction and the incident execution-scope defect remain separate.

## Explicit supersession

| Earlier statement | Later authority | Scope of supersession |
|---|---|---|
| Historical broad API logging assurance and raw-exception request handler | [DEC-0227](docs/decisions/current/2026-10-08-DEC-0227.md), [October 8 API audit correction](API_SECURITY_AUDIT.md#october-8--escaping-request-exception-boundary-and-logging-scope-correction) | Escaping request failures are contained with bounded safe fields. The earlier assurance does not cover all enabled framework/provider sinks; desktop curated-envelope policy remains. |
| September 1 form identity keyed by DueDate; generated “born complete/in force” assumptions | September 14 annual-compliance decision; [current attestation design](NOTE_FORM_ATTESTATION_DESIGN.md) | Annual identity is target-identified and completion requires actual evidence. Historical migration compatibility is retained; current rows are not grouped by equal deadlines. |
| September 28 rejection of completing an unopened PCP in one save | October 6 explicit annual-progress decision in the [record index](docs/decisions/README.md) | A confirmed actual opening/completion choice may occur in one atomic save; no automatic inferred completion. |
| Early person-only holds and single-Admin release limitation | October 6 source update in [governance runbook](RECORDS_GOVERNANCE_RUNBOOK.md) | Scoped holds/independent release now exist in source; complete runtime retention/recovery remains gated. Old hold evidence is retained. |
| Original assessment submission/review and save-before-submit limitation | October 7 immutable-review decision and [assessment runbook](ASSESSMENT_REVIEW_RUNBOOK.md) | Exact saved revision/hash, immutable cycles and independent review govern; approval/signature/external acceptance remain separate. |
| September 26 clearinghouse migration/coordination described as not implemented | Later release records in [archived agenda](docs/archive/2026-10-08/AGENDA.md) and [sandbox runbook](CLAIMMD_SANDBOX_RUNBOOK.md) | Migration/coordination source status is updated; vendor acceptance and uncertain-send reconciliation gates remain. |
| October 3 serverless allowance used as the no-wake-ping rationale | October 8 observed inventory in [DATABASE_ENVIRONMENTS.md](DATABASE_ENVIRONMENTS.md) | Compute observation supersedes serverless as current fact; D2 no wake ping/no idle polling is preserved, not reversed. |

These entries state only supported amendments. Other historical contradictions/open dispositions
remain explicitly unreviewed until evidence is attached; this migration does not invent approval
or mark them resolved. Preserve D1–D4 and the exact DATT limitations.

## Adding or changing a decision

Add one dated record with an unused stable ID, context, choice, rejected alternatives, status,
source/evidence and explicit `supersedes`/`supersededBy` links where relevant. Update the record
index and machine-readable registry. Amend current topic owners and affected backlog/readiness
rows in the same change. Do not overwrite historical reasoning or copy deployment state here.
