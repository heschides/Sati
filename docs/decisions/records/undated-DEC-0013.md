<!-- Retained decision record; original wording. Use DECISIONS.md supersession register. -->
## Local AI-Assisted Case Notes

> Historical note: the original background-context and calculated-follow-up decisions below were
> superseded by the closed-world transformation decision dated 2026-08-22. They remain here as the
> development record, not as a description of current behavior.

### The model drafts; the case manager authors

AI output is never written directly to a note, submitted, logged, billed, approved, or used to
determine compliance. Sati retains the user's rough narrative while presenting the generated text
as a separate draft. The case manager must compare and explicitly accept the draft, may edit it,
and remains the author responsible for the submitted record.

The model may reorganize and clarify supplied facts but may not add missing services,
interventions, durations, participants, outcomes, quotations, diagnoses, consent, risk,
follow-up, or assertions of billability. Sparse source material must produce a sparse draft rather
than a plausibly completed fiction.

### On-device inference with an explicit off switch

The development slice uses Foundry Local in-process, initialized only when requested. Model catalog
lookup and first-time model acquisition may use the network, but note inference is local. The
feature is controlled by `LocalAi:Enabled`; disabling it hides the UI and prevents runtime/model
initialization. No cloud fallback is permitted silently.

### Note policy is external and versionable

Agency drafting instructions live in `AI_CASE_NOTE_RULES.md`, not inside the ViewModel or XAML.
This allows the standard to be reviewed and refined without entangling presentation code. Before
production, Sati must persist the exact rule-set/model version used for an accepted draft and test
every change against a de-identified regression corpus.

### Required note envelope and calculated follow-up

Every generated draft begins `Community Case Manager (CCM) [signed-in user's full
name]` and finishes with a `Follow-up:` section. The signed-in user's display name is
trusted application context, not guessed from the rough note. When the source does not
contain an evident follow-up, Sati supplies the model with a deterministic fallback
from the consumer's form records: the most recently overdue core form first, otherwise
the next incomplete 90-day review, Comprehensive Assessment, PCP, or Reclassification.
The model does not calculate or invent the form or due date.

### Client background is assembled, not learned

The local model is not fine-tuned on or given permanent memory of client records. On each request,
Sati builds a fresh, permission-checked context snapshot. General Bio is always included. Journal,
address, phone, MaineCare ID, diagnosis code, place-of-service, and billing fields are excluded from
the query. This is a purpose limitation, not a claim that those fields can never support a future
separately authorized AI workflow.

The first retrieval policy is intentionally understandable: ten recent notes plus up to five older
keyword-matched notes, current service flags and deadlines, and either the author's active draft
assessment or the latest approved assessment. Semantic embeddings are deferred until this behavior
has been evaluated with de-identified cases.

Prior records may clarify established names, roles, services, and deadlines, but cannot establish
what happened during the contact being documented. Client-record text is untrusted prompt data,
not executable instruction. The case manager can expand **Context used** before accepting a draft
to see the source note IDs and document version supplied to the model.

