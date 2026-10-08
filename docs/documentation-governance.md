# Documentation governance

**Status:** governing documentation rules, October 8, 2026. These rules organize evidence; they
do not change product, legal, release or infrastructure authorization.

## One canonical owner

`documentation-index.json` maps every root Markdown path to one topic owner and role. A canonical
topic appears once. Forwarders keep old paths valid and point to that owner; they carry no separate
rules. New root Markdown or canonical Markdown under `docs/` without a registered owner fails
validation. Registered decision records use their decision registry; immutable archives use the
snapshot registry. Every current canonical page has its local Markdown links checked. `AGENTS.md` is the single
assistant briefing; `CLAUDE.md` remains only a pointer.

Current observed deployment facts live only in `DATABASE_ENVIRONMENTS.md`; other current pages
link there. Readiness/evidence is separate from source ownership, implementation plans and dated
audit findings. Do not call a planned or historical document current or use its observation as
present state. Existing topic policy/runbook documents remain authoritative for their scope.

## Active work and decisions

`AGENDA.md` uses stable SATI IDs. `docs/backlog/legacy-open-items.json` preserves every original
unchecked item under LEG IDs, with original context/text/line and an explicit disposition.
Requires-revalidation is not open-defect proof. Promote/merge/close/supersede only with dated
evidence and links; keep the original ID. Complete release/session records stay in archives.

`DECISIONS.md` indexes retained dated decision records and explicit scoped supersession. Keep
the chosen/rejected reasoning intact. Register new records in docs/decisions/current-decisions.json, separately from the immutable import, with stable IDs; record both the earlier
and later authority. New records require dated evidence paths, a current index entry and unused
IDs across both registries. Documentation reorganization never silently reverses D1–D4 or user decisions.

## Upkeep after significant work

[AGENTS.md](../AGENTS.md#standing-work-and-documentation-upkeep) owns the standing upkeep and
next-work invocation procedure. Follow it after each significant work portion, not only at a
release. Thorough updates cover changed behavior/ownership, status and dependencies, decisions
and rejected alternatives, verification results and limits, remaining blockers, and the next slice.
Update the canonical topic owner first, then the affected index/agenda/evidence links. A meaningful
change to a boundary, guard, workflow or result warrants upkeep even when no product code changed.

The agenda has exactly one **Next eligible item** pointing to exactly one current stable-ID row;
its bounded scope, dependencies, acceptance and authority must be explicit. Revalidate eligibility
before starting and update the pointer before finishing. Archives and LEG imports provide context,
not an automatic next-work queue. Record blocked choices and the next eligible slice honestly.

[The working evidence ledger](readiness/work-evidence.md) owns dated in-progress evidence and
pending verification. Link relevant criterion IDs without changing sealed release scores or
immutable rubrics. A working record must distinguish actual tests/results, proposed tests, source
assertions, runtime/vendor evidence and unknowns. Readiness method and release snapshot procedures
remain the authority for a later reviewed release assessment.

## Historical evidence and compatibility

The migration snapshots are byte-preserved and hash-checked. Old links/source line numbers
describe their capture location/revision. Current docs do not copy giant historical paragraphs.
Git attributes disable line-ending normalization for archive captures and immutable imported
decision records; the multiline legacy page uses fixed LF. Preserve these attributes so a fresh
checkout retains hashes and source-exact reasoning. Whitespace-only cleanup of historical bytes
would destroy that evidence; current canonical documents retain ordinary whitespace checks.
Forward existing root paths before archiving anything cited in code, tests, scripts or operator
error messages. Preserve `WORKSPACE_REFLECTIONS.md`, `DEMO_ACCEPTANCE.md` and code-cited deletion/
duplicate-repair policies. Do not erase unresolved security/audit evidence because newer work exists.

The [migration inventory](documentation-migration.md) records captures, source-reference paths
and disposition. Refer to it before removing a forwarder or retargeting a legacy document path.

## Required checks

Run `scripts/Test-DocumentationStructure.ps1` and `scripts/Test-DocumentationStructureChecks.ps1`.
The gate verifies mapped root and canonical scoped documents, unique owners and scoped paths,
safe repository-relative paths, valid canonical links, brief pointers,
current inventory ownership, stable IDs, historical status and immutable snapshots. Negative
tests mutate only disposable synthetic copies and demonstrate each guard rejects the bad state.
CI and release preflight must run this gate; a broken gate cannot be bypassed by updating an
unrelated test total. Document intentional exceptions in the manifest with their bounded reason.

Update the owner, affected index/backlog/readiness and validator expectation together. Keep
changes limited to documentation and its checks unless the user authorizes product work.
