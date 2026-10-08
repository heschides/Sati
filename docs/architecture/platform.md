# Planned platform products

**Status:** current architecture reference, October 8, 2026. Detailed rules remain with the linked canonical owners.

Sati is implemented; Karuna and Upekkha documents describe future product/authority boundaries. Membership and exceptional cross-tenant authority are separate. Shared platform types use opaque product-tagged IDs; existing Sati-shaped rules are not silently generalized or moved.

Karuna K0 requires its own identity/model, explicit non-null tenant ownership, query filters and a SQL RLS/session-context lifecycle spike. No reserved solution folder, design or scaffold proves a deployed product.

## Canonical detailed owners

- [PLATFORM_DOMAIN.md](../../PLATFORM_DOMAIN.md)
- [PLATFORM_RESTRUCTURE_PLAN.md](../../PLATFORM_RESTRUCTURE_PLAN.md)
- [karuna/CODEX_HANDOFF.md](../../karuna/CODEX_HANDOFF.md)

Original feature rationale and source references are retained in the
[pre-consolidation architecture snapshot](../archive/2026-10-08/ARCHITECTURE.md).
[Architecture index](../../ARCHITECTURE.md) · [Readiness evidence](../readiness/README.md)