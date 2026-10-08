# Authoritative guidance and Sati release bars

A release bar is the evidence and approval required for a stated use. It is stronger than “the tests are green” and more specific than “enterprise-ready.” Sati uses official guidance to choose controls, then records its own acceptance and actual results in [readiness.json](readiness.json). The thermometer is an internal progress index; it is not an OWASP, NIST, Microsoft or HHS certification.

Primary-source status was checked October 8, 2026. Keep the source/version/date with any detailed future control mapping. The 42-criterion crosswalk below is scoped to tenant separation, safe replay and their operating dependencies; it does not replace a complete application-security or healthcare compliance assessment.

## Distinguish the three kinds of requirement

**Applicable law and approved obligations.** If the intended entity/service/data fall under HIPAA, its actual regulatory requirements and applicable contracts govern. HHS describes risk analysis and risk management for covered entities/business associates, cloud responsibility agreements and required BAAs in the relevant relationships. Encryption or a cloud provider's eligible services do not by themselves meet the service's obligations. Applicability, agency/program rules, lawful retention and contractual responsibilities require qualified review. [HHS cloud guidance](https://www.hhs.gov/hipaa/for-professionals/special-topics/health-information-technology/cloud-computing/index.html), [HHS risk-analysis guidance](https://www.hhs.gov/hipaa/for-professionals/security/guidance/guidance-risk-analysis/index.html).

**Official recommendations and open verification standards.** Microsoft architecture guidance, OWASP ASVS and NIST guides help define defensible engineering and assessment. They are not automatically law for this private product. Adoption must identify intended scope and retained evidence; a recommendation cannot be declared satisfied merely by citing its page.

**Sati's internal acceptance thresholds.** Worker-turn/time bounds, load/latency limits, provider budgets, RPO/RTO, exact negative/race tests, fail-first proof and evidence stages are Sati decisions based on agency needs and risk. The official sources below motivate the controls; they do not prescribe Sati's numeric values or this thermometer formula. Define and approve those values before running acceptance.

## Versioned primary references

| Source | Verified status and use |
|---|---|
| [Microsoft multitenancy checklist](https://learn.microsoft.com/en-us/azure/architecture/guide/multitenant/checklist) | Official Azure design guidance: select tenancy models from customer needs, prevent noisy-neighbor impacts, test isolation/scale, set tenant SLOs, manage lifecycle, monitor tenants and test alerts. It does not require one worker or database per agency. |
| [OWASP ASVS](https://owasp.org/projects/asvs) and [official level guidance](https://github.com/OWASP/ASVS/blob/master/5.0/en/0x03-What-is-the-ASVS.md) | Latest stable release listed is 5.0.0. Use the pinned release requirements for the actual assessment; level choice is risk-based. This crosswalk is not full ASVS conformance or a control-by-control mapping. |
| [NIST SSDF 1.1 / SP 800-218](https://csrc.nist.gov/pubs/sp/800/218/final) | Final, February 2022. Use for secure development, protected release processes, vulnerability handling and retained software-development evidence. |
| [NIST SSDF 1.2 / SP 800-218 Rev. 1](https://csrc.nist.gov/pubs/sp/800/218/r1/ipd) | Initial Public Draft, December 2025, not a final replacement for 1.1 at this review. Track it as draft input; do not claim final-standard adoption. |
| [NIST SP 800-66 Rev. 2](https://csrc.nist.gov/pubs/sp/800/66/r2/final) | Final, February 2024. Practical cybersecurity resource guidance for understanding and implementing applicable HIPAA Security Rule safeguards; not a legal clearance or product certification. |
| [HHS risk-analysis guidance](https://www.hhs.gov/hipaa/for-professionals/security/guidance/guidance-risk-analysis/index.html) | Official interpretation/guidance for applicable Security Rule risk analysis. Its scope includes all relevant ePHI and circumstances; a source-code-only review is narrower. |
| [HHS cloud guidance](https://www.hhs.gov/hipaa/for-professionals/special-topics/health-information-technology/cloud-computing/index.html) | Official guidance for applicable cloud relationships, responsibilities, agreements and contingency concerns. HHS does not endorse/certify a cloud product as compliant. |
| [HTTP and replay baseline](protocol-baseline.md) | RFC 9110, RFC 9111 and RFC 9457 are the standards references for the relevant HTTP behavior. The checked Idempotency-Key document is an expired Internet-Draft, not a final RFC. Application replay/transaction guarantees need their own proof. |

For the future PHI-bearing API/public portal, Sati's proposed assessment target is **ASVS 5.0.0 Level 2 at minimum, with Level 3 scope selected by documented risk review** for sensitive records, billing, privileged administration and public access. The responsible security reviewer must approve that target and create the full pinned requirement/evidence matrix before any claim of meeting a level. This is an internal target, not a legal mandate. WPF/client and deployment/operating boundaries also need suitable review beyond ASVS's web-application scope.

## Scoped crosswalk to all 42 criteria

This is a topic crosswalk: the listed primary guidance supports why these areas need evidence. Exact ASVS/NIST control identifiers must be verified against pinned editions in a separate full assessment; none are invented here. Acceptance tests and thresholds remain the ledger's Sati-specific requirements.

| Sati criteria | Official guidance theme | Evidence required for Sati's release bar |
|---|---|---|
| MT01, MT02, MT03, MT04, MT05, MT12, MT14 | Microsoft trusted tenant mapping/isolation; ASVS access, files, sessions and data protection | Complete enabled release-path inventory; positive/foreign-tenant tests; intended SQL/storage principal checks; current authority and scoped account/file/cursor bindings. |
| MT06, MT07, MT08, MT09, MT10, MT11 | Microsoft noisy-neighbor, SLO, scale and fault guidance | Declared turn/deadline/concurrency limits, independent-host SQL races, representative load/fault results, shared-provider scope and healthy-agency latency evidence. |
| MT13 | Microsoft tenant lifecycle; HHS cloud responsibility/return-of-data guidance when applicable | Rehearsed provisioning/transfer/closure/recovery, access revocation, preservation and approved tenant responsibilities. |
| ID01, ID02, ID03, ID10, ID12, ID14 | HTTP semantics plus ASVS validation/business-integrity topics; SSDF verification discipline | Scoped lasting keys/fingerprints/results, atomic concurrent claims, revision refusal, bounded safe retries, lifetime/version tests and truthful client responses. |
| ID04, ID05, ID06, ID07, ID08, ID09, ID11, ID13 | ASVS business-integrity topics; NIST/HHS integrity and contingency concerns when applicable | Durable intent/receipt effects, stale-owner refusal, uncertainty quarantine, original/correction and current-eligibility rules, notification safety and restore reconciliation. |
| OP01, OP02, OP03, OP12 | ASVS authentication/configuration/security verification; NIST safeguard/risk guidance | Approved target environment, actual least privilege and identity recovery, independent scope review and retained remediation evidence. |
| OP04, OP05, OP07, OP10 | Microsoft reliability, tenant health/alerts and scale; NIST/HHS contingency concerns | Measured full-service recovery, actual alert delivery/response, shared-failure/degraded-mode exercise and intended-host capacity results. |
| OP06, OP09, OP13 | ASVS logging/data-protection topics; NIST/HHS risk, preservation and cloud responsibilities | Complete safe activity evidence, redaction/access proof, approved preservation/agreements, and certification of each intended enabled provider workflow. |
| OP08, OP11, OP14 | SSDF development/release/vulnerability discipline; ASVS configuration; Microsoft controlled updates | Supported client/server/schema matrix, tested safe rollback/repair, dependency/configuration review, immutable source-bound release evidence and understandable accessible acceptance. |

## Allowed-use gates

**Synthetic demonstration or synthetic evaluation:** prove the declared environment/data boundary and exact customer journey, identify mock/unavailable features, name a support owner and retain the relevant acceptance. A low production-readiness score does not erase a bounded synthetic demonstration's useful tested capabilities; a successful demonstration does not open PHI or financial-effect gates.

**PHI-bearing multi-agency pilot or cloud Production:** verify every blocking criterion in the chosen rubric for the intended enabled scope, close relevant failed invariants, and obtain independent security/architecture and qualified agency/legal/operating decisions. Require actual environment permissions, alert receipt/response, service recovery, supported releases and capacity evidence. A small pilot is not an exception to obligations that apply to its data or financial effects.

**Real billing, signatures or email:** obtain the relevant provider/account/payer acceptance and agency/operating approvals before those effects are enabled. Preserve test-only gates until then. A feature intentionally unavailable must have its boundary independently verified; do not score its real-use certification as completed.

**Higher-assurance target:** deepen the risk-selected security assessment and automate/repeat drift, dependency, isolation, load/fault, recovery and release checks at declared cadences. Record observed operation against objectives and close new findings. “Elite enterprise” is an internal aspiration, not a named official certification or demand for unrelated features.

[readiness-method.md](readiness-method.md) governs the ledger calculation and immutable comparison. [RELEASE_PLAYBOOK.md](../../RELEASE_PLAYBOOK.md), the active operating runbooks and qualified approval owners govern actions. The release helpers validate honest records; they cannot grant authority to deploy, query PHI, alter cloud security or certify healthcare compliance.
