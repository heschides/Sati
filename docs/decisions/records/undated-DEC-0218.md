<!-- Retained decision record; original wording. Use DECISIONS.md supersession register. -->
## October 6, 2026 — records governance with preservation before destruction

- Hold scope is immutable. Amendments version its evidence and cancel pending release requests;
  broaden preservation with another hold. The placing Admin and requesting Admin cannot decide
  a release. Decisions identify the exact pending revision; unchanged operation retries are
  idempotent and changed replay is refused. Legacy release commands now request, rather than
  complete, release. Legacy originals and released history remain retained.
- Store the hold/policy reasons in protected append-only evidence; operational audit events contain
  IDs/revisions only. Policies are proposals with no invented or seeded period. Null duration means
  indefinite preservation. No policy is implicitly approved by saving or previewing it.
- Preserve the whole connected dependency component, including reverse edges, across the six
  record classes. A missing dependency/store is unavailable. A record hold with an unresolved
  person conservatively refuses consumer deletion until a reviewed mapper establishes scope.
- Use one serializable agency lock and preservation epoch for holds, policies, consumer deletion
  and retention batches. A preview grants no permission. Recheck policy/epoch/graph/fingerprints
  under the lock; commit at most 50 deletions with the checkpoint and receipt. Failed transactions
  are disposed and retried from a fresh context, never continued.
- Keep runtime PolicyOnly and use an unavailable inventory/deletion adapter. Unknown counts remain
  unknown. The executable engine accepts only owned private fixtures with plan/epoch-bound backup,
  encrypted-object and recovery evidence. An opaque UUID is a protected-reference contract, not
  proof that an external vault or backup adapter has been implemented. Remote deletion needs a
  reviewed durable outbox adapter before activation. See RECORDS_GOVERNANCE_RUNBOOK.md.
