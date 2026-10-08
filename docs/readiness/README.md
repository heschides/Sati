# Readiness owners and evidence

This is the entry point for readiness; it does not duplicate scores or activation status.
[The immutable rubrics and dated snapshots](readiness.json) own those assessments.
[The method](readiness-method.md) defines percentages, unknowns, evidence and change rules.
A high percentage does not override a blocker or authorize a deployment.

| Owner | Purpose |
|---|---|
| [Readiness ledger](readiness.json) | Versioned rubrics and dated evidence snapshots |
| [Assessment method](readiness-method.md) | Scoring/uncertainty and interpretation |
| [Working evidence](work-evidence.md) | Dated significant-work results, limits and next slices between release assessments |
| [Idempotency protocol baseline](protocol-baseline.md) | What implementation and independent acceptance evidence must establish |
| [Multitenancy contingencies](multitenancy-contingencies.md) | Required failure, capacity and isolation scenarios |
| [Official guidance and release bars](authoritative-release-bars.md) | Verified primary guidance, applicable obligations and internal acceptance thresholds |
| [Active agenda](../../AGENDA.md) | Stable work IDs and explicit deferral/disposition |
| [Dated environment inventory](../../DATABASE_ENVIRONMENTS.md) | What was observed, when and within what verification limits |
| [Operations](../../OPERATIONS.md) | Named-owner response and recovery procedures |

Readiness tests validate bookkeeping, not service performance or legal compliance. Changes require
dated evidence, the applicable rubric version, explicit remaining blockers and an independent
acceptance pass where the method requires it. Source tests, disposable provider tests, deployed
operation, vendor acceptance and approved real-data use are distinct evidence classes.
