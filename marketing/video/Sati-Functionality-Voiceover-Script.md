# Sati Functionality Tour — Voiceover and Visual Script

**Format:** narrated product walkthrough with screen capture, screenshots, and simple motion graphics<br>
**Estimated full length:** 24–28 minutes at a calm pace<br>
**Intended audience:** provider-agency leaders, case managers, supervisors, billing staff, reviewers, and technical stakeholders<br>
**Recording environment:** Sati Demo, using synthetic records only

## Production notes

- Text under **Voiceover** is meant to be read aloud.
- Text under **On screen** is direction for the person recording or editing the video and is not read aloud.
- Keep the permanent **DEMO** indicator visible whenever the application is shown.
- Use designated synthetic accounts and records. Never enter or display real client information.
- Mask usernames, passwords, access tokens, local file paths, and downloaded files when necessary.
- Record slow, deliberate pointer movement. Let important validation messages remain visible long enough to be read.
- If a feature is not enabled in the current hosted Demo, omit that shot. Do not substitute Local Production or real data.
- The billing demonstration stays in test mode. Do not suggest that the generated 837P is payer-certified or ready for live submission.
- The optional development-preview chapters at the end are separate from the main tour and must be labeled as such on screen.

---

## 1. Opening — why Sati exists

**Approximate time:** 0:00–1:05

**On screen**

Begin with quiet shots of a calendar, handwritten reminders, separate forms, and a billing checklist. Transition to the Sati sign-in screen and logo. Display the title: **Sati — Human Services Infrastructure for Maine**. Add a smaller subtitle: **Demonstration environment — synthetic data**.

**Voiceover**

Human-services work carries an unusual burden. A case manager has to remain present with a person, remember the details of the visit, meet documentation standards, track annual and quarterly requirements, coordinate with providers, respond to supervision, and make sure the work can ultimately be billed.

Sati brings those responsibilities into one connected workflow.

It is a Maine-focused case-management platform designed from direct experience with the daily work. Its purpose is simple: make the next responsibility visible, make documentation easier to complete correctly, and carry trustworthy information forward without asking staff to re-enter the same facts again and again.

In this tour, we will follow that workflow from the beginning of the day through case management, documentation, supervision, billing, and administrative review.

Everything shown in this video is synthetic demonstration data. Sati remains under active development and is not being presented here as a certified billing system, a regulatory certification, or a complete replacement for an agency's existing production systems.

---

## 2. Environment selection and sign-in

**Approximate time:** 1:05–2:05

**On screen**

Launch Sati. Show the environment chooser before the splash screen. Select **Demo**, allow the splash screen to complete, and sign in with a designated synthetic case-manager account. Hold briefly on the permanent **DEMO** marker after the shell opens.

**Voiceover**

Sati begins by asking which data environment to open. That choice happens before sign-in and before any data connection is made.

The demonstration environment is separate from working data. It has its own database, credentials, service identity, deployment, logs, and administrative access. The permanent Demo marker remains visible throughout the session so the presenter and the viewer always know which environment they are seeing.

After sign-in, the application shows only the work allowed for this user's verified role and permissions. That is a convenience in the interface, but it is not the security boundary. The server independently confirms the user's identity, role, agency, and access whenever protected data is requested.

---

## 3. The day begins with an agenda

**Approximate time:** 2:05–3:30

**On screen**

Show the daily agenda window. Move through overdue work, upcoming work, and any Comprehensive Assessment suggestion. Select one or two lines and add them as structured work items. Close the agenda and reveal the Overview workspace.

**Voiceover**

At sign-in, Sati can begin with a daily agenda. Instead of asking the case manager to search several screens, the agenda brings forward overdue forms, upcoming requirements, and work that is approaching its planning window.

The agenda is read-only until the user chooses an action. A case manager can select an item and add it to today's work. Sati records that as a structured scheduled-form note, linked to the right consumer and form type. It is not merely copied into a block of text, and it does not mark the form complete.

That distinction matters. Planning the work is not the same as completing the work, and Sati preserves the difference.

The user can turn the daily agenda off in personal settings, and Sati remembers whether it has already been shown that day. The feature is designed to clarify the morning, not interrupt it repeatedly.

---

## 4. Overview — the working center of the day

**Approximate time:** 3:30–5:05

**On screen**

Show the Overview at a wide window size. Identify the current-note area, Work Agenda, upcoming due dates, and productivity summary. Add a short line to today's work, move an item between the two dated drafts if available, and open scratchpad history. Resize the window through balanced, compact, and narrow layouts. Briefly activate Focus note.

**Voiceover**

The Overview is the working center of Sati.

The current note stays close to the Work Agenda, while upcoming due dates show what is approaching across the caseload. When there is enough vertical space, a monthly productivity summary appears in the same view.

The Work Agenda keeps separate drafts for today and the next workday. These drafts are saved for the signed-in user, and the same live agenda moves with the user when they navigate to other parts of Sati. There is not a second hidden copy waiting to overwrite it.

The layout also adapts to the space actually available inside the application. On a wide display, the main responsibilities sit side by side. On a smaller window, the same live panels rearrange and eventually stack. The note editor keeps its text, cursor position, selection, and undo history while the layout changes.

For concentrated writing, Focus note gives the editor more room without creating a different note or a separate workflow.

The aim is to keep the day visible while allowing the interface to fit the real screen in front of the user.

---

## 5. The consumer record

**Approximate time:** 5:05–7:15

**On screen**

Open **Clients** and choose the designated synthetic consumer. Move slowly through the profile Overview, service and waiver information, guardian or representative information, employment and supports, billing address, contacts, provider links, quarterly tracking, and annual requirement indicators. Demonstrate last-name sorting or the compact client selector only if it helps the shot.

**Voiceover**

The consumer record gathers the information a case manager needs to understand the person and prepare the work.

The profile can include waiver and service information, vocational-rehabilitation status, guardian and representative-payee details, employment and support services, addresses used for ordinary contact and billing, and other agency-defined facts.

Sensitive identity information is not displayed casually. When protected values are available, the user must deliberately reveal them, and saving follows the application's protected-data path.

Contacts are maintained as structured records. Sati can distinguish an emergency contact from a contact with an active information release, while still leaving the actual decision about disclosure with the documented authorization and agency process.

The provider section connects the consumer to entries in the agency's shared provider directory. This avoids a separate, private list for each case manager and lets corrections improve the directory for the agency as a whole.

Quarterly and annual requirements appear alongside the profile: ninety-day reviews, the Person-Centered Plan, Comprehensive Assessment, reclassification, Safety Plan, privacy practices, and release forms. These indicators are not only visual reminders. The same underlying rules are used when Sati decides whether documentation can move forward toward billing.

When an agency is onboarding records from Credible, the import workflow previews mapped fields before saving. It checks for likely existing consumers in a defined order and does not silently guess at fields it cannot identify.

---

## 6. Guided documents and assessments

**Approximate time:** 7:15–9:15

**On screen**

Open the consumer's document workspaces. Show the Comprehensive Assessment authoring surface and a saved version. Briefly show the Person-Centered Plan, Classification, DHHS Forms, Agency Release, and Annual Documents areas that are available in the current Demo. Show an AT request and its PDF preview or generation path. Do not show Safety Plan or electronic-signature controls unless they are enabled in the exact hosted build being recorded.

**Voiceover**

From the same consumer record, Sati opens guided document workspaces.

The Comprehensive Assessment supports structured authoring without reducing the document to a set of checkboxes. Draft work is preserved, required sections can be validated, and submitted versions are retained so the official history is not replaced by a later edit.

Related workspaces keep different decisions separate: the Person-Centered Plan, classification information, official DHHS forms, agency releases, and annual-document tracking each have their own purpose and lifecycle.

Known profile and agency details can flow into a form so staff do not have to retype information the record already holds. But consumer-directed choices remain explicit. Sati does not infer consent, manufacture a signature, or treat a populated field as proof that a person agreed.

Assistive-technology requests follow the same principle. The user can select a provider, capture the request details, save the work, and produce a reviewable PDF. A generated document is an output of the recorded facts; it is not a substitute for required approval or external agency process.

Across these areas, drafts, submissions, approvals, and later corrections are treated as different states. That separation is essential when a document becomes part of clinical, financial, or audit history.

---

## 7. Provider coordination

**Approximate time:** 9:15–10:25

**On screen**

Open **Providers**. Search the directory, select an organization, show parent affiliation and named contacts, then begin adding or editing a synthetic entry. Trigger a same-name warning if a safe fixture exists. If using an Admin account later, capture the merge review separately and insert a short cutaway here.

**Voiceover**

The Providers workspace is the agency's shared rolodex.

It can hold organizations, individual providers, parent affiliations, identifiers, billing details, and multiple named contacts. Staff with the appropriate role can add or correct entries, while destructive actions such as deletion and merge are restricted to administrators.

Because two legitimate organizations may share a name, Sati warns about a likely duplicate without automatically blocking the entry. When an administrator confirms that two records truly represent the same provider, the merge workflow reviews conflicts, protects frozen document references, moves eligible relationships, and records an audit event without placing consumer names in that event.

The result is a directory that can be shared without pretending that shared data maintains itself.

---

## 8. Writing a service note

**Approximate time:** 10:25–13:10

**On screen**

Return to Overview or open **Notes**. Start a new note for the designated consumer. Show note type, status, service date, start time, minutes, and the service-day timeline. Select a Visit note and check representative meeting facts. Use **Build Case Note Template**. Enter a short synthetic narrative. Show the suggested follow-up and accept it if appropriate. Attempt an overlapping time only on a disposable synthetic example, or use a prepared screenshot of the conflict message. Save as **Hold for Now**, then open the note from the Notes log and choose **Send to Supervisor**.

**Voiceover**

Now we reach the center of the case-manager workflow: the service note.

The case manager selects the consumer, note type, workflow status, service date, start time, and duration. A service-day timeline shows time already recorded and the time claimed by the current draft. Sati checks the full interval, not just the starting time, so overlapping billable work cannot be hidden by two different durations.

For a Visit note, meeting details appear only when they are relevant. The case manager records the facts of the contact, then can build a structured case-note template from those checked facts. Any narrative already written is preserved beneath a clear Meeting Narrative heading. The template helps organize known facts; the case manager remains responsible for accuracy, context, and professional judgment.

Sati can also suggest the consumer's next outstanding form. Accepting the suggestion adds a follow-up to the current note; it does not complete the form or change compliance status.

At any point, the note can be held as unfinished work. When it is ready, the case manager sends it to a supervisor.

Before that transition succeeds, Sati evaluates the documentation and compliance rules. If an annual requirement is overdue, the application names the exact problem and stops the note at submission. The issue is visible where it can be fixed, instead of allowing an unbillable record to travel farther down the pipeline.

Once submitted, the note becomes review work. Later corrections follow the permitted return or amendment path rather than silently rewriting completed history.

---

## 9. Notes log, calendar, reviews, and statistics

**Approximate time:** 13:10–15:10

**On screen**

Show the Notes log with status and date filters, preserving filters if useful. Open the Calendar in month view and year overview. Show a note on a selected day. If the Outlook import feature is part of the recorded build, show the explicit file-selection step but do not import a real calendar file. Open Reviews, Caseload Matrix, and Statistics. Change the reporting date range and show consumer-level billing/productivity rows.

**Voiceover**

The Notes log provides the deeper view of documentation already in motion. Staff can filter the list, reopen eligible work, and see whether a note is being held, waiting for review, returned, approved, or farther along in the financial workflow.

The Calendar places scheduled and recorded work in time. Month and year views help the user move between the immediate week and the larger pattern of the year. Calendar data remains tied to the underlying record rather than becoming another independent copy.

The Reviews area and Caseload Matrix widen the view from one note to the obligations across a caseload. They make quarterly reviews and annual requirements easier to compare, including work that has not started, has been requested, has been received, or has been logged.

Statistics turns the same recorded work into a selected-period summary. It can show performed units, workdays, non-billable periods caused by compliance gaps, and consumer-level detail. The reporting view helps explain the numbers, but it does not override the rules that produced them.

Together, these screens answer different questions from the same workflow: What happened? What is due? What is blocked? And what does the current period look like?

---

## 10. Supervisor review

**Approximate time:** 15:10–17:20

**On screen**

Switch to the designated synthetic supervisor or reviewer account. Open **Supervisor**. Show Team overview, Overdue items, Pending approvals, and Monthly productivity. Filter pending approvals by case manager. Open the submitted note, show its narrative, units, and compliance results. Return one prepared synthetic note with a reason, then approve another. If suitable fixtures exist, show **Approve all within threshold** and its explicit unit threshold without applying it to unintended records.

**Voiceover**

The supervisor workspace turns submitted work into an assigned, reviewable queue.

Team overview and overdue items show where attention is needed across the supervisor's actual scope. Monthly productivity adds context without making a productivity number the same thing as record quality.

In Pending approvals, the reviewer sees the consumer, author, service date, note type, units, narrative, and compliance result together. The first page loads quickly, and additional notes are loaded in bounded pages as the reviewer asks for them.

A supervisor can approve an eligible note or return it with a reason. A returned note goes back to the case manager as editable work. An approved note can continue toward billing only when the underlying requirements pass.

For routine, low-unit work, an authorized reviewer can explicitly choose to approve all notes within a selected threshold. Nothing is approved merely because the page loaded, the user scrolled, or the threshold changed. Each note still passes through the ordinary authorization, compliance, time-conflict, concurrency, and audit checks.

This is the second major gate in the pipeline: review remains fast, but it is never reduced to a cosmetic status change.

---

## 11. Billing preparation

**Approximate time:** 17:20–19:25

**On screen**

Switch to an authorized billing account if required. Open **Billing**, then **Overview** and **Queue**. Show agency billing configuration without exposing real identifiers. In the queue, point out ready and needs-attention counts, open the explanation for a blocked row, select ready synthetic services, and promote them into a draft billing period.

**Voiceover**

Approved documentation reaches the Billing workspace only for users with billing permission.

The Overview holds agency-level claim and envelope configuration: procedure codes, optional modifiers, unit rates, payer information, submitter information, and the contact details needed for the 837P structure. Missing or invalid configuration stops claim creation instead of producing a file that merely looks complete.

The Billing Queue separates services that are ready from services that need attention. Each blocked item explains why: a missing demographic or payer field, a compliance failure, invalid units, or another concrete readiness problem.

Billing staff can select ready services and promote them into a draft monthly billing period. That action creates claim-bearing work from approved source records while preserving the relationship back to the documentation and the rules that qualified it.

The important idea is that billing is not a disconnected export at the end. It is the continuation of a controlled path that began with the consumer record and the service note.

---

## 12. Submission staging, test 837P, and response tracking

**Approximate time:** 19:25–22:05

**On screen**

Open **Submissions**. Select a draft period and show its claim lines. Use a prepared exact-ready synthetic period for **Submit & Lock**. Show it move from the Draft Period Queue into **837 Staging**. Keep **Test mode** selected. Generate the selected 837P and show that the period leaves staging and appears in Submission Home. Use the **Mock Clearinghouse — Demo Only** controls to choose a safe prepared outcome and submit the generated test file. Then show Submission Home, Remittances, and Alerts, including a synthetic accepted, rejected, partially accepted, denied, or unpaid example.

**Voiceover**

The Submissions workspace makes the financial lifecycle explicit.

A draft billing period remains editable while its claim lines are being reviewed. Submit and Lock moves an exact-ready period into 837 staging. That transition freezes the period represented by the submission workflow; it is not simply a label change.

In this demonstration, test mode remains selected. Sati then generates a test 837P file for each selected monthly period. Successful generation is recorded, the period leaves staging, and its continuing exchange state appears in Submission Home.

The Demo includes a mock clearinghouse so the workflow can be exercised without contacting a real clearinghouse or payer. It can simulate the response path for generated, transmitted, failed, accepted, rejected, and partially accepted work. The simulated 999, 277CA, and 835 responses travel through the same ingestion boundaries intended for external responses, while every synthetic row is clearly marked.

Submission Home shows where each batch is in the exchange, when activity last occurred, and whether action is still required.

The Remittances view compares billed, allowed, paid, adjusted, and patient-responsibility amounts. The Alerts view turns rejected, denied, and unpaid outcomes into an actionable worklist, with plain-language explanations and aging information.

Duplicate commands and ambiguous retries are designed not to create a second successful financial result. Submitted financial history is retained, and correction follows a controlled path.

This is an early-stage billing and 837P workflow. It still requires payer-specific validation, operational controls, and certification before any live submission use.

---

## 13. Administration, audit evidence, and lifecycle controls

**Approximate time:** 22:05–24:25

**On screen**

Switch to the designated synthetic Admin. Open **Admin**. Show agency counts, recent protected activity, data or operations health, audit and EDI counts, and the displayed retention mode. Select a synthetic consumer and show the lifecycle timeline. Enter a legitimate synthetic-demo business purpose and export a bounded audit CSV or download the auditor PDF. Return to recent activity and point out the export event. Show user management, caseload distribution, and safe lifecycle controls. Do not perform a consumer deletion or Demo restore during ordinary filming; use prepared screenshots of the typed confirmation if needed.

**Voiceover**

The Admin workspace brings operational evidence and sensitive lifecycle controls into one place.

Agency summaries show users, case managers, consumers, recent notes, sign-ins, protected changes, and current audit activity. Operational panels expose the application's data health and retained counts without requiring an administrator to connect directly to the database.

Recent protected activity answers practical questions: who acted, what category of record changed, when it happened, and under which agency scope. Selecting a synthetic consumer opens a versioned lifecycle timeline so an internal reviewer can follow changes without relying on the current profile alone.

Audit exports are bounded and require a stated business purpose. Spreadsheet-bound text is neutralized on export, because quoting an unsafe value is not enough to prevent spreadsheet execution. The export itself then appears as protected activity, creating evidence that the evidence was taken.

User management and caseload distribution are also role-controlled. Caller-supplied user or agency identifiers are never trusted by themselves; the server checks the signed-in actor and the requested scope before a query or mutation proceeds.

Consumer lifecycle actions are deliberately difficult to confuse. Ordinary records can be assigned an appropriate status, while deletion is narrowly limited, checked against billing integrity and legal-hold information, and protected by exact typed confirmation. Test-consumer deletion is a separate administrative path. These controls are not a complete retention program, and the current legal-hold registry is narrower than the broader policy and dual-control work still required.

The dashboard can support oversight and internal review. It does not, by itself, establish HIPAA compliance or any other legal conclusion.

---

## 14. Personal settings, accessibility, and presentation

**Approximate time:** 24:25–25:55

**On screen**

Open Settings from the user greeting. Show Profile, Password & Security, Appearance, Text shortcuts, and the role-appropriate agency settings. Change the theme, toggle Easy Eyes, and show the immediate result. Briefly show scheduling, workday exclusions, templates/forms, reference data, and release notes. Demonstrate keyboard focus on several navigation elements. If practical, show the inactivity privacy overlay and dismiss it.

**Voiceover**

Sati separates personal presentation choices from agency rules.

The signed-in user can maintain a personal profile, change a password, choose an appearance theme, configure text shortcuts, decide whether to show the daily agenda, and select personal list preferences.

Easy Eyes increases the overall working scale, simplifies selected layouts, and hides dense narrative columns where appropriate. It is a personal display choice stored for this user, Windows profile, and environment; it does not change the clinical record.

Agency-authorized settings cover matters such as billing and request requirements, work schedules, holidays, templates, forms, and reference data. Permissions determine who can manage those shared values.

Accessibility is treated as part of the interface, not a color palette added afterward. Interactive surfaces receive keyboard behavior and meaningful automation names, visible focus is preserved, changing status can be announced to assistive technology, and theme combinations are tested for readable contrast. Automated checks provide a floor; hands-on screen-reader and usability review remains necessary.

After a period of inactivity, Sati can place a privacy screen over the application. That helps prevent casual viewing, but the current overlay is described honestly as a privacy feature, not as a full security lock.

---

## 15. The technical pipeline behind the screens

**Approximate time:** 25:55–27:20

**On screen**

Move from the application to a simple animated diagram:

`Windows client → HTTPS API → authorization and domain rules → short-lived database work → Azure SQL`

Animate a short-lived token from the client to the API. Show separate Demo and Production lanes. Add callouts for tenant access, validation, transactions, audit events, record versions, and concurrency checks. Do not display source code, secrets, resource names, or connection strings.

**Voiceover**

The screens are only one part of Sati. The more important boundary sits behind them.

In the hosted Demo, the Windows client calls Sati's API over HTTPS. The client does not contain an Azure SQL connection string, does not receive a shared database credential, and does not run cloud migrations.

Each authenticated request carries a short-lived token. The API revalidates the actor, applies tenant and caseload access, checks the same named business rules used to explain the decision in the client, and performs the authorized work in a short-lived database context.

Transactions keep related changes together. Optimistic concurrency prevents one person's stale screen from silently overwriting a newer change. Protected actions append audit events. Submitted clinical and financial records use versions, events, returns, or amendments rather than invisible replacement.

Demo and Production remain separate from the first environment choice onward. This architecture is intended to produce evidence of access control, integrity, and accountability. Those technical controls are necessary foundations, but policies, training, risk analysis, incident response, recovery testing, legal review, and operational practice are also required before a healthcare platform can make a compliance or production-readiness claim.

---

## 16. Closing

**Approximate time:** 27:20–28:05

**On screen**

Return to a clean Overview shot. Slowly crossfade through the consumer record, note editor, supervisor queue, billing staging, and audit timeline. End on the Sati logo and the line: **Keep what matters present and accounted for.** Add: **SatiLogica / RobinBradleyAMS** and **Demonstration shown with synthetic data**.

**Voiceover**

Sati connects the work instead of treating each responsibility as a separate destination.

The day's agenda leads into the consumer record. The record supports the note. Compliance rules protect the handoff to supervision. Approval opens a controlled route to billing. Submission responses become worklists. And protected actions leave evidence for review.

The result is not less responsibility. It is a clearer place for each responsibility to be seen, completed, checked, and understood.

Sati is being built so that the paperwork becomes easier to navigate—and more of the day can return to the people the work is meant to serve.

---

# Optional development-preview chapters

These chapters are not part of the standard hosted-Demo narration. Include them only when the exact build being recorded enables the feature, uses synthetic data, and carries an on-screen banner reading **Development preview — not deployed or approved for real-client use**.

## A. Safety Plan authoring and review

**On screen**

Show the Safety Plan draft, readiness validation, submission, supervisor review, and a draft-marked PDF. Avoid implying that its database migration is deployed to the hosted Demo.

**Voiceover**

This development preview shows Sati's structured Safety Plan workflow.

An author can prepare and validate a draft, then submit it for review. Approval requires a different supervisor whose real caseload scope includes the consumer; agency membership alone is not enough. A plan that has not been approved remains visibly marked as a draft and cannot satisfy the annual Safety Plan prerequisite.

This workflow is implemented for synthetic testing but is not being presented as deployed or approved for real-client use.

## B. Electronic signatures

**On screen**

Show the staff request workspace, then a synthetic browser capture of the isolated signing portal, explicit consent and intent, the decision, and the retained receipt. Do not send real email or describe the portal as deployed.

**Voiceover**

This development preview shows an electronic-signature architecture built around a frozen original document.

The staff workflow creates a signer-specific request without exposing the staff API or database. The separate public portal records consent, intent, events, and the final decision. Signing does not replace the source document, manufacture a staff acknowledgment, or silently mark an unrelated form complete.

Signed evidence and derived packages are retained as separate records. Operational hosting, accessible-PDF work, identity procedures, mail validation, legal review, retention, recovery, and real-data authorization remain required before deployment.

## C. Team chat

**On screen**

Show a synthetic consumer-scoped room, explicit members, paged message history, a generic change notification, redaction history, and the effect of removing a member. Do not show real clinical discussion.

**Voiceover**

This development preview shows Sati's synthetic, API-only team chat.

Access to a consumer-scoped room depends on explicit membership and the user's live caseload relationship. A generic real-time notice tells the client that something changed, while authenticated and authorized HTTP retrieves the actual content and records minimized release evidence.

Messages and changes remain in durable history. Removing a member invalidates that membership episode and stops further access. Retention policy, discovery and legal-hold procedures, account-wide session revocation, deployment rehearsal, and agency approval remain open gates before any real-client use.

---

# Recording checklist

## Required continuity

- Use the same designated synthetic consumer through agenda, note, review, billing, and audit shots whenever possible.
- Use clearly different case-manager, supervisor, billing, and Admin accounts so role boundaries are visible.
- Capture successful paths and at least one readable blocked path for compliance, time overlap, claim readiness, and authorization where safe fixtures exist.
- Keep dates internally consistent. A note should not appear in a billing period before its service date or approval.
- Capture each important status before and after the action that changes it.
- Allow two to three seconds of stillness before and after each click to make editing easier.

## Required disclosures

- Demonstration data is synthetic.
- The billing and 837P workflow is early-stage and not payer-certified.
- Mock clearinghouse responses do not contact a real clearinghouse or payer.
- Audit views support review but do not establish legal or regulatory compliance.
- Sati is under active development and is not currently represented as production-ready or HIPAA compliant.
- Optional Safety Plan, signature, and chat segments are development previews unless their deployment and approval status changes before recording.

## Do not capture

- Real consumer, staff, provider, payer, or agency data.
- Passwords, tokens, connection strings, secret-store locations, or unmasked credentials.
- Local Production as a fallback when Demo is unavailable.
- A live or production-labeled 837P submission.
- Diagnostic files that could reveal machine-specific paths or operational details.
- Claims that nightly full reset, external alerting, full legal-hold enforcement, payer certification, real-use electronic signatures, or real-use chat are operational unless separately verified at recording time.
