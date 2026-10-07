# Records governance — source implementation and activation gates

Prepared October 6, 2026 for feature prompt 03. This is an unreleased source feature.
No runtime database was queried or changed for this work. Migration 127, API publication,
installer publication, approved legal periods and destructive retention are not activated.

## Implemented boundary

Administration has a **Records governance** tab for agency, person and exact-record holds,
one/all record classes, placement/change history, release requests and independent approval
or rejection. The placer and release requester cannot decide the release. A decision uses
the exact pending revision. An amendment cancels a pending request; another request and
independent decision are then necessary. Scope is immutable: broader preservation requires
an additional hold. Operation identities support unchanged retries without another event.

`RecordsGovernanceRules` in Contracts owns permission, transitions, person preservation,
connected dependency preservation, policy validation and the execution gate. The portable
Persistence workflows own append-only evidence, agency locks and atomic writes. Local EF
and HTTP services implement the same interface; API identity/agency comes from the validated
actor, never request-selected agency. Policy definitions are versioned proposals. A blank
duration preserves indefinitely. No duration is seeded and no proposal is treated as approved.

The existing person-hold placement route also registers its hold in the new workspace.
The old release route now **requests** release and returns `IsReleased=false` until approval;
clients must not describe that response as a completed release. Approval synchronizes the
legacy row within the same transaction. Migration imports every active legacy hold with its
original placement actor, time, reason, case reference and issuer. Existing rows, effective
dates and already released history remain untouched. Both registries still check active
legacy rows, even if an import is unavailable. Hold-query failure never means clear.

Both ordinary and test-consumer deletion now acquire the agency preservation lock before
evaluating holds. Agency and linked-person holds block deletion. An exact-record hold whose
person cannot be identified returns `Unavailable` and conservatively blocks consumer deletion.
That conservative scope can be refined only with a reviewed dependency adapter.

## Preview and execution semantics

Each persisted preview names its policy version, preservation epoch, date window, eligible
count/range, held exclusions, unavailable exclusions and dependency count. Candidates are
bounded to 500 inventory records. Preservation traverses both dependency and reverse-reference
edges across Clinical, Billing, Audit, Chat, Documents and Evidence; a hold anywhere in the
connected component preserves it. Missing dependencies, incomplete/duplicate/oversized
inventories and adapter errors refuse execution.

**Runtime currently uses an unavailable store adapter. Counts and dates are unknown, not zero.**
Complete schema-to-store dependency inventories and real backup/object-store reconciliation
are integration gates. They are not fabricated from the database alone. The API execution
route remains `PolicyOnly` regardless of request content; the desktop has no purge command.
The only executable path is the shared engine in explicitly verified private fixtures:
SQLite `:memory:` or `SatiSyntheticPipeline_<guid>` in a private `SatiSqlTests_<guid>` LocalDB
instance. A Boolean fixture claim alone cannot unlock another database. There is no runtime
configuration switch that enables destructive retention.

A batch contains at most 50 prepared candidates. The host owns a serializable transaction.
A no-op concurrency-checked update to the agency state acquires its write lock before hold,
policy or deletion work. Hold placement/release/change and policy publication advance the
preservation epoch. Any intervening change invalidates a prepared plan. Execution checks the
current policy, complete inventory, fingerprints, dependency edges and holds again under
that lock, then deletes and appends its durable checkpoint/receipt atomically. A failed adapter
or save must roll back and dispose the context. Retry from a new context with the unchanged
operation identity; committed retries return their stored batch result without deleting again.
Changed actor/plan/checkpoint replay conflicts. Deadlocks/conflicts require a fresh request or
unchanged retry after refreshing; never resume inside a failed transaction.

The plan freezes the complete bounded inventory graph, not just candidate roots. A new linked
copy or changed linked record invalidates it. Later batches account for roots already removed
by their committed checkpoints and require every remaining prepared record/edge to match.

## Preservation and recovery evidence

The adapter must supply a receipt bound to the plan and current governance epoch, a named
adapter, UTC evidence time, SHA-256 evidence digest, and an opaque UUID resolving through
approved protected recovery storage. The persisted receipt contains no URI, access token,
storage key, local path or clinical narrative. The recovery-reference resolver and its
encryption/access lifecycle are an activation gate, not an implemented external vault.
Backup preservation, encrypted-object preservation and recovery verification must all be
confirmed. Missing, misbound, malformed or future evidence blocks the batch. Backup and
object reconciliation failure therefore cannot be mistaken for completed deletion. Never
treat a database purge as proof all external or backup copies were reconciled. Remote object
deletion requires its own reviewed durable outbox/checkpoint adapter; the current transaction
adapter contract does not authorize remote side effects.

Reasons and legal case evidence belong in protected hold/policy history. General audit events
carry only IDs, revisions and policy/plan metadata. Retention batch evidence retains actor,
operation, checkpoint, count and protected receipt. History is append-only and aggregate/
checkpoint deletion is refused. These safeguards do not constitute regulatory certification.

## Controlled migration 127

`20261007004626_AddRecordsGovernance` creates six additive tables. The guarded SQL checks
the exact 126-migration predecessor or exact 127-migration result, all 56 column shapes and
29 key/index/ownership definitions, and imported active preservation. Partial/untracked schema
is refused. Schema, imported evidence and migration history share one caller-owned transaction.
Down refuses whenever hold/policy/plan/batch evidence exists, including imported legacy holds.
An evidence-bearing downgrade needs a reviewed preservation/recovery plan, not a table drop.

After separate migration/release authorization, review:

1. Confirm the selected Demo identity and reviewed SQL/runner hashes. Coordinate workers and
   reset/baseline changes using the release playbook. The runner takes the exclusive Demo reset
   lock; apply before publishing an API/client that requires these tables.
2. Have the user add any required exact workstation-IP firewall rule. This feature does not
   authorize security-setting changes. Run `scripts/Apply-RecordsGovernanceMigration.ps1
   -WhatIfOnly`, then the approved apply, then an idempotent rerun. Remove and verify removal
   of the user-created rule using the release procedure.
3. Validate imported hold count/history and preservation behavior using approved Demo fixtures;
   refresh the Demo baseline and prove reset compatibility before release. This task does not
   authorize a cloud migration or a local PHI-store migration/backup.
4. Publish the matching API contract and client through a later release. Default policies remain
   absent/unapproved and enforcement remains `PolicyOnly`.

## Gates before any runtime purge

- Agency/legal approval of each versioned period, record-class applicability, clock trigger,
  statutory exceptions, authority to release and evidence retention. No period is inferred here.
- Reviewed, complete inventories and dependency maps for each clinical/billing/audit/chat/
  document/evidence store, plus operational exports, encrypted objects and backups. Unknown
  or unsupported coverage must remain a blocking result.
- Protected recovery-reference resolver, encrypted-object/outbox reconciliation and verified
  backup restoration (coordinate prompt 17). Assign a named operational owner and failure alerts.
- A separately reviewed production-capable execution gate replacing the fixture-only boundary,
  explicit approved policy activation, bounded worker scheduling, incident handling and rollback
  evidence. No automatic enablement occurs merely because this migration is applied.

## Verification

Synthetic API/rule tests cover tenancy, authority, stale revisions, immutable history, exact
retry, independent decisions, all six connected classes/reverse edges, missing dependencies,
changed policy/hold, missing/misbound backup/object/recovery evidence, batch bounds and rollback
after a transactional adapter failure. WPF tests cover accessible selectors, dual-control
commands, account clearing, reload completion and stale preview suppression. Private SQL tests
rehearse additive import, guarded rollback/rerun/schema rejection and two-connection contention
where a newly committed hold defeats a prepared purge. Regression probes remove approval and
preservation guards, require the targeted tests to fail, then restore and rerun the source.
No destructive test uses SatiProduction, shared LocalDB, Azure SQL or real PHI.

Verification evidence at completion: 31 focused API/workflow/legacy checks, 46 focused desktop/
migration-boundary checks and 3 private SQL checks passed. Broader regression runs passed
2,911 desktop and 1,056 API tests, with external/opt-in tests skipped; the later disk-database
and linked-copy hardening was verified in the final focused runs. The three other solution
test projects passed 119, 8 and 4 tests. The initial broad run exposed a SQLite in-memory
identity bug; that was repaired and the API suite rerun successfully. Migration-chain replay
tracked 127 migrations with zero symbolic problems. Runner syntax and its SQL hash pin passed.

Run the focused SQL checks with `pwsh -NoProfile -File scripts/Test-IsolatedLocalDb.ps1
-RecordsGovernanceOnly`. That wrapper owns and cleans only a newly created private instance.
