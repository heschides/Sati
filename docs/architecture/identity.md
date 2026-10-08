# Identity, tenancy and sessions

**Status:** current architecture reference, October 8, 2026. Detailed rules remain with the linked canonical owners.

Implemented: JWT verification followed by persisted enabled/security-version/agency/capability checks; caller target IDs are scoped, not trusted identity. Local services validate their stored session. Agency Admin is separate from the narrowly permitted cross-tenant PlatformOperator. Revocation governs later authorization; it does not retrieve released data or prove cancellation of every in-flight operation.

Planned: structural tenant defaults/composite constraints, database-tenancy choice and multi-instance admission/abuse controls. RLS/session-context pooling is a planned spike, not current Sati enforcement.

## Canonical detailed owners

- [API_AUTHORIZATION.md](../../API_AUTHORIZATION.md)
- [API_SECURITY_AUDIT.md](../../API_SECURITY_AUDIT.md)

Original feature rationale and source references are retained in the
[pre-consolidation architecture snapshot](../archive/2026-10-08/ARCHITECTURE.md).
[Architecture index](../../ARCHITECTURE.md) · [Readiness evidence](../readiness/README.md)