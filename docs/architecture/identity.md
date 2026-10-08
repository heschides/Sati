# Identity, tenancy and sessions

**Status:** current architecture reference, October 8, 2026. Detailed rules remain with the linked canonical owners.

Implemented: JWT verification followed by persisted enabled/security-version/agency/capability checks; caller target IDs are scoped, not trusted identity. Local services validate their stored session. Agency Admin is separate from the narrowly permitted cross-tenant PlatformOperator. Revocation governs later authorization; it does not retrieve released data or prove cancellation of every in-flight operation.

Planned: structural tenant defaults/composite constraints, database-tenancy choice and multi-instance admission/abuse controls. RLS/session-context pooling is a planned spike, not current Sati enforcement.

`ApiExceptionBoundaryMiddleware`, registered first in the API pipeline, owns containment of
escaping downstream request failures and generic 500/correlation responses or safe abort.
Constructor-injected logging emits bounded type/HResult/correlation fields without raw exceptions;
`ApiIncidentRecorder` retains best-effort safe-envelope ownership. Authentication/tenant middleware
order and route authority are unchanged. `IncidentAggregator` owns the complete aggregation
transaction in a named zero-retry scope and refuses an active retrying caller before work.
[The incident execution owner](../../LOGGING_DESIGN.md#api-incident-execution--source-october-8-2026)
records the immediate-reference replay limit; no historical deduplication or deployed SQL
persistence is established. The separate
[two-check health boundary](../../LOGGING_DESIGN.md#api-health-failures--source-october-8-2026)
removes caught exceptions from health results/framework sinks while retaining identity/schema
validation, anonymous failure status and current cancellation behavior. Hosted startup identity
handling is unchanged. Independent EF/provider, startup, registration-construction and callback
sinks remain outside these proofs. [The request logging owner](../../LOGGING_DESIGN.md#api-request-boundary--source-october-8-2026)
and [working evidence](../readiness/work-evidence.md) describe actual proof and remaining scope.

## Canonical detailed owners

- [API_AUTHORIZATION.md](../../API_AUTHORIZATION.md)
- [API_SECURITY_AUDIT.md](../../API_SECURITY_AUDIT.md)

Original feature rationale and source references are retained in the
[pre-consolidation architecture snapshot](../archive/2026-10-08/ARCHITECTURE.md).
[Architecture index](../../ARCHITECTURE.md) · [Readiness evidence](../readiness/README.md)
