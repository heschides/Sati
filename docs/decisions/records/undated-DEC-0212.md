<!-- Retained decision record; original wording. Use DECISIONS.md supersession register. -->
## Consumer provider order — October 3, 2026 (source only)

Use the existing SortOrder rather than introduce another persisted ordering model.
Primary care remains pinned first; ended assignments retain chronological history.
Move commands stage an explicit arrangement, and one save validates every current
association and updates only SortOrder. No affiliation, role or primary mark changes.

A SHA-256 fingerprint over the complete retained relationship DTO collection provides
an expected snapshot without a schema migration. It is compared under serializable
isolation before writes in both service paths. Changes to membership, order or other
relationship facts require explicit review/retry; directory changes are excluded.
Identical final facts have the same token, so this does not claim to detect ABA history.
A stale retry returns a typed conflict; the user may explicitly apply the intended
permutation to a fresh snapshot if its membership and primary placement still agree.

Rejected: saving individual rows from the UI, which can leave half an order saved and
silently overwrite a simultaneous edit. Rejected: letting display order change primary
care status, medical affiliation, ended history or frozen document/financial snapshots.
Reorder adds an atomic metadata-only audit. Existing unrelated provider-change audit
coverage remains separately deferred.

