# Signature and chat boundaries

**Status:** current architecture reference, October 8, 2026. Detailed rules remain with the linked canonical owners.

Implemented source: signatures freeze exact original documents and signer-specific evidence; package/mail work uses durable operation identities/leases. The portal has a separate narrow identity and private artifact boundary. Chat persists changes with room membership and consumer reach; in-process notices carry no content.

Service identity separation is not per-agency SQL/blob identity isolation. Signature mail provider acceptance is not inbox delivery. Legal, accessible-client, deployed-grant, delivery-event, retention and recovery evidence remains separate.

## Canonical detailed owners

- [SIGNATURE_PORTAL_GUIDE.md](../../SIGNATURE_PORTAL_GUIDE.md)
- [SIGNATURE_PORTAL_REVIEW.md](../../SIGNATURE_PORTAL_REVIEW.md)
- [SIGNATURE_PORTAL_VALIDATION.md](../../SIGNATURE_PORTAL_VALIDATION.md)
- [TEAM_CHAT_GUIDE.md](../../TEAM_CHAT_GUIDE.md)
- [TEAM_CHAT_REVIEW.md](../../TEAM_CHAT_REVIEW.md)

Original feature rationale and source references are retained in the
[pre-consolidation architecture snapshot](../archive/2026-10-08/ARCHITECTURE.md).
[Architecture index](../../ARCHITECTURE.md) · [Readiness evidence](../readiness/README.md)