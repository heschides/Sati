# Professional payer requirements — source verification October 4, 2026

The first documented profile is **MaineCare Section 13 through Claim.MD**, as selected by Josh.
This is a source implementation and requirements record. Live enrollment, credentials,
submission authority, testing and payer acceptance require the separate
[certification checklist](PAYER_BILLING_CERTIFICATION.md). No populated identifier establishes those facts.

## Primary-source matrix

| Requirement | Primary document, version/date | Effective scope and format | Implementation / outstanding evidence |
|---|---|---|---|
| Payer routing | [Claim.MD ME Medicaid](https://docs.claim.md/docs/me-medicaid), updated April 7, 2026; [payer listing](https://www.claim.md/payer/memcd), consulted October 4 | Claim.MD payer ID `MEMCD` | MaineCare profile rejects alternate routing. ISA/GS identify the separately configured Claim.MD account; NM1 PR identifies the payer. Direct MIHMS routing needs a different evidenced profile. |
| Service location | Same Claim.MD payer article | Block 32, professional loop 2310C; alphanumeric `xxxx-xxx`, including dash | Freeze facility name/address, optional NPI, ID and explicit REF qualifier. **Article does not establish the qualifier**: agency must obtain current companion-guide/vendor evidence for LU or G2. Neither is defaulted. Synthetic G2 is test data. |
| Taxonomy | [Maine DHHS taxonomy bulletin](https://content.govdelivery.com/accounts/MEHHS/bulletins/3e351e0), June 3, 2025 | 837P billing 2000A PRV03; rendering 2310B PRV03 | Both ten-character taxonomies are configured and frozen. NPI checks validate syntax/check digit, not enrollment or taxonomy assignment. Agency verifies its enrolled billing/rendering entities. |
| Provider/facility details | [Claim.MD Professional Claim Form Overview](https://docs.claim.md/docs/professional-claim-form-overview), updated December 5, 2025 | Billing tax ID/NPI; rendering person or organization/NPI; facility name/address and situational NPI | Field-specific validation rejects X12 delimiters and non-ASCII. The overview has inconsistent crosswalk labels (including facility 2310E and some NPI/payer mappings). Do not use those labels to override the payer's 2310C instruction or the licensed professional implementation guide. Resolve inconsistencies during certification. |
| Prior authorization | [MaineCare Chapter II §13](https://www.maine.gov/dhhs/sites/maine.gov.dhhs/files/rule-2026-04/MaineCare%20Benefits%20Manual,%20Chapter%20II,%20Section%2013.pdf), adopted/effective April 28, 2026, §13.05-2(A) | Case-management services require prior authorization from the Department's authorized entity | Required reference, decision date no later than service date, inclusive coverage dates, consumer/profile/procedure/modifier/provider scope, protected evidence reference and explicit human coverage review. Review actor/time freeze with claim. This does not implement quantity utilization (prompt 6). |
| Authorization loop | Claim.MD overview, block 23; [X12 RFI 2459](https://x12.org/resources/requests-for-interpretation/rfi-2459authorization-numbers-found-2300-loop-v-2400-loop) | Claim-level REF G1 in 2300; do not repeat it at line level | One authorization per supported single-line claim. RFI discusses X222A2; current Sati rendering remains X222A1. Obtain applicable licensed guide and payer confirmation before live certification. |
| Procedure and population | Chapter II §13, effective April 28, 2026, §13.10-1 table | T1017: no modifier/HIV; UD/child developmental; UC/child behavioral; UB/child chronic; HF/adult substance; U5/homeless. G9012: no modifier/carceral; HI/adult developmental | Allow listed combinations only in this profile. Agency selects its authorized population; Sati does not infer member eligibility from diagnosis, code or modifier. Older service dates require a separately evidenced profile. |
| Units and rate | Same rule, §13.10-1; Chapter I and current fee schedule referenced by it | 15-minute basis and minimum substantive contact; payer fee schedule governs rates | Configured positive evidenced rate; existing shared Section 13 decimal-unit calculation is retained. New MaineCare preparation conservatively requires at least 15 documented minutes. Verify contact aggregation, Chapter I rounding, fee schedule and any SPA-dependent effective conditions before live use; no rate was copied or invented. |
| Current operational transition | [MaineCare provider page](https://www.maine.gov/dhhs/oms/providers), consulted October 4, 2026 | MIHMS portal freeze announced October 2, 8 pm through October 12, 8 am; provider-system transition | Linked HealthPAS/companion-guide pages could not be retrieved during this research. Recheck the current guide, routing, enrollment and fee schedule after the transition. This is a live activation gate, not a claim of a verified guide version. |

## Version and compatibility policy

`PayerBillingRules` owns permission interpretation, validation, effective-date selection and
frozen payer inputs. `PayerBillingStore` and one persistence mapping serve API and local EF paths.
Versions are append-only, agency owned and identified by a change UUID plus a per-profile revision.
Publication requires current Administration permission, an expected revision and reviewed agency
evidence. Billing may prepare claims; Administration without Billing may configure but cannot
preview consumer billing facts. Caller-supplied agency scope never reaches an unvalidated write.
The editor is reachable from Billing's Payer configuration button and Administration's Payer
configuration tab. Refresh loads protected versions; only Billing also loads approved unclaimed notes.

For each profile, new starts must be strictly later than the preceding start. Starts and expiration
dates are inclusive. The latest start on/before the service date wins; expiration never causes
fallback to an older version. Historical correction of a bad configuration's effective period
requires a separately named, evidenced profile, rather than rewriting a retained version.

An agency with no profiles retains the existing **legacy compatibility** creation path. Publishing
its first profile makes explicit effective-version preparation mandatory for **every new claim**;
missing, expired or stale selections fail closed. Compatibility is not live payer certification.
No profile is seeded or activated by the migration, and no real agency data is read by feature tests.

Snapshot v1 retains its JSON shape and original renderer, including legacy taxonomy/segment
behavior. Snapshot v2 must carry validated configuration provenance and authorization evidence;
unknown versions, missing v2 inputs or payer inputs disguised as v1 are rejected. V2 adds explicit
2000A billing taxonomy, 2300 G1, 2310B rendering and 2310C facility loops, ordered modifiers,
professional POS qualifier B and diagnosis pointer 1. Each current claim remains single-line.
Configured MaineCare generation requires a Claim.MD trading-partner account. Different envelope
facts in one period fail readiness; different frozen subscriber identities get separate HL groups.

Resend/replacement refreshes subscriber identity/diagnosis from current authorized records while
preserving the standing claim's exact configuration and authorization. It adds the SHA-256 of the
prior snapshot. A corrected date outside frozen coverage is refused; configuration replacement
for an already submitted claim needs a separately designed explicit review action. A void repeats
the standing snapshot and financial facts exactly. Existing financial amendment review and correction
lineage still govern units/charges. No config edit rewrites originals, correction history, generated
files, encrypted receipts or transmission identities. D9 stays stable; CLM/control identity changes
per generation; F8 identifies the payer's standing claim for replacement/void.

These preservation rules describe v2 corrections. Existing v1 correction behavior retains its
legacy snapshot format and refreshes legacy defaults as before; it is outside the certified v2
profile scope. Live activation must resolve outstanding legacy claims/corrections or implement a
separately reviewed conversion. Other professional profiles currently support MC/CI filing indicators
and the existing Section 13 service-unit basis. Known Maine Medicaid routing cannot be disguised as
an Other profile to opt out of authorization. Other programs/unit bases require an explicit extension.

## Follow-on boundaries

Prompt 6 must replace manually reviewed authorization references with an authoritative authorization
aggregate and utilization ledger while retaining historical evidence. Prompt 7 must extend the frozen
single-line model deliberately. Neither enrollment verification, eligibility lookup, multi-line
claim grouping, rates integration nor automatic utilization enforcement is represented as implemented.
The local and server generator adapters share the same formatter and accept the same explicit
trading-partner profile. The existing local file-export service still selects Office Ally; configured
MaineCare files require the API's existing server-owned Claim.MD account selection. This feature does
not introduce local account/credential operation or external transport.
See [activation and migration guidance](PAYER_BILLING_CERTIFICATION.md).
