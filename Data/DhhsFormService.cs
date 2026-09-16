using Sati.Models;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Forms;

namespace Sati.Data;

/// <summary>
/// The local Production implementation: everything happens on the workstation.
///
/// No network call, no PHI in transit, and nothing here needs the API to be
/// reachable — a case manager with no connectivity still produces a form. That is
/// the requirement this class exists to satisfy, and it is why the filler lives in
/// the shared <c>Sati.Forms</c> library rather than inside <c>Sati.Api</c>: one
/// implementation of the stamping, two processes that run it.
///
/// AN SSN IS NEVER FILLED HERE. Social Security numbers are cloud-only, encrypted
/// under a Key Vault key this process has no access to, and deliberately declared as
/// shadow properties on <c>SatiContext</c> so the local model has no property to read
/// them through. The SSN box therefore prints blank in local Production, and the
/// result says so rather than leaving the case manager to notice on paper. See
/// DECISIONS.md, "An SSN is cloud-only".
/// </summary>
public sealed class DhhsFormService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService,
    LocalSsnStore ssnStore) : IDhhsFormService
{
    /// <summary>
    /// Local Production stores SSNs, protected by the Windows user's DPAPI key.
    ///
    /// This reverses the original cloud-only decision, and the reason is workflow
    /// rather than architecture: filling the Appointment form is occasional, but
    /// reading a consumer's number to the Social Security Administration on their
    /// behalf is routine, and a case manager cannot do that from a blank box. The
    /// protection is real but its limits are narrower than the cloud path's — see
    /// <see cref="DpapiKeyWrapper"/>.
    /// </summary>
    public bool SupportsSsnStorage => true;

    /// <summary>
    /// Local Production can show the number. That is the point of storing it here:
    /// reading it to the Social Security Administration on a consumer's behalf is
    /// routine work and cannot be done from a mask.
    /// </summary>
    public bool SupportsSsnReveal => true;

    public async Task<SsnStatusDto> GetSsnStatusAsync(
        int personId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        var person = await LoadOwnPersonAsync(context, personId, cancellationToken);

        return new SsnStatusDto(
            LocalSsnStore.MaskFor(context, person),
            LocalSsnStore.IsOnFile(context, person));
    }

    public async Task<SsnStatusDto> UpdateSsnAsync(
        int personId,
        string? socialSecurityNumber,
        CancellationToken cancellationToken = default)
    {
        var actor = sessionService.CurrentUser
            ?? throw new InvalidOperationException("An SSN cannot be stored without a signed-in user.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        var person = await LoadOwnPersonAsync(context, personId, cancellationToken);

        var normalized = SsnMask.Normalize(socialSecurityNumber);
        await ssnStore.SetAsync(context, person, normalized, cancellationToken);

        // The action, never the value. An audit row naming what changed is the point;
        // one containing the number would defeat the column it describes.
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.PersonSsnUpdated,
            "Person",
            personId);
        await context.SaveChangesAsync(cancellationToken);

        return new SsnStatusDto(
            LocalSsnStore.MaskFor(context, person),
            LocalSsnStore.IsOnFile(context, person));
    }

    /// <summary>
    /// Reveals the stored number for the caller to read aloud or transcribe, and
    /// records that it was read.
    ///
    /// Audited separately from any document it might feed, because a disclosure is
    /// the read itself — the same reason the API records `person.ssn-decrypted`
    /// alongside the form it generated.
    /// </summary>
    public async Task<string> RevealSsnAsync(
        int personId,
        CancellationToken cancellationToken = default)
    {
        var actor = sessionService.CurrentUser
            ?? throw new InvalidOperationException("An SSN cannot be read without a signed-in user.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        var person = await LoadOwnPersonAsync(context, personId, cancellationToken);

        var ssn = await ssnStore.RevealAsync(context, person, cancellationToken);

        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.PersonSsnRevealed,
            "Person",
            personId);
        await context.SaveChangesAsync(cancellationToken);

        return ssn;
    }

    /// <summary>
    /// Scoped to the signed-in case manager's own caseload and agency, the same
    /// restriction the API route applies. A transitional local service repeats the
    /// rule rather than relying on being the only caller.
    /// </summary>
    private async Task<Person> LoadOwnPersonAsync(
        SatiContext context,
        int personId,
        CancellationToken cancellationToken)
    {
        var actor = sessionService.CurrentUser
            ?? throw new InvalidOperationException("No user is signed in.");

        await LocalTenantAccess.EnsureCurrentActorAsync(context, actor, cancellationToken);
        if (!await LocalTenantAccess.OwnsPersonAsync(context, actor, personId, cancellationToken))
            throw new InvalidOperationException("That consumer is not on your current caseload.");

        return await context.People.SingleOrDefaultAsync(
            candidate => candidate.Id == personId &&
                         candidate.UserId == actor.Id &&
                         candidate.AgencyId == actor.AgencyId,
            cancellationToken)
            ?? throw new InvalidOperationException("That consumer is not on your caseload.");
    }

    public async Task<DhhsFormResult> GenerateAsync(
        DhhsFormDefinition.FormKey form,
        int personId,
        DhhsFormDefinition.Selections selections,
        CancellationToken cancellationToken = default) =>
        await GenerateCoreAsync(
            form, personId, selections, targetEffectiveDate: null,
            releaseObligationId: null, cancellationToken);

    public async Task<DhhsFormResult> GenerateForAnnualTargetAsync(
        DhhsFormDefinition.FormKey form,
        int personId,
        DhhsFormDefinition.Selections selections,
        DateTime targetEffectiveDate,
        Guid? releaseObligationId,
        CancellationToken cancellationToken = default) =>
        await GenerateCoreAsync(
            form, personId, selections, targetEffectiveDate.Date,
            releaseObligationId, cancellationToken);

    private async Task<DhhsFormResult> GenerateCoreAsync(
        DhhsFormDefinition.FormKey form,
        int personId,
        DhhsFormDefinition.Selections selections,
        DateTime? targetEffectiveDate,
        Guid? releaseObligationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selections);
        if (form != DhhsFormDefinition.FormKey.AuthorizationToRelease &&
            (targetEffectiveDate is not null || releaseObligationId is not null))
            throw new ArgumentException(
                "Annual target identity applies only to the DHHS authorization-to-release form.");

        var actor = sessionService.CurrentUser
            ?? throw new InvalidOperationException("A form cannot be filled without a signed-in user.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var agencyId = actor.AgencyId;

        // Scoped to the signed-in case manager's own caseload and agency, the same
        // restriction the API route applies. A transitional local service repeats the
        // rule rather than relying on being the only caller.
        // Tracked, not AsNoTracking: the encrypted SSN lives in shadow properties, and
        // shadow values are held by the change tracker. An untracked entity has none,
        // so the number would silently read as absent and the box would print blank.
        var person = await LoadOwnPersonAsync(context, personId, cancellationToken);
        var releaseTarget = form == DhhsFormDefinition.FormKey.AuthorizationToRelease
            ? await ResolveDhhsReleaseTargetAsync(
                context, person, actor.AgencyId, targetEffectiveDate,
                releaseObligationId, cancellationToken)
            : null;

        var agency = await context.Agencies.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == actor.AgencyId, cancellationToken);

        // Decrypted only if one is on file, and recorded as a disclosure in its own
        // right — the same separation the API keeps between reading a number and the
        // document that occasioned the read.
        string? ssn = null;
        if (LocalSsnStore.IsOnFile(context, person))
        {
            ssn = await ssnStore.RevealAsync(context, person, cancellationToken);
            LocalAuditTrail.Record(
                context, actor, LocalAuditActions.PersonSsnRevealed, "Person", personId);
        }

        var subject = new DhhsFormDefinition.Subject(
                FullName: $"{person.LastName}, {person.FirstName}".Trim(' ', ','),
                BirthDate: person.BirthDate,
                Address: person.Address,
                PhoneNumber: person.PhoneNumber,
                SocialSecurityNumber: ssn,
                RepresentativeName: null,
                RepresentativeAddress: null,
                RepresentativePhone: null,
                RepresentativeEmail: null)
            .WithRepresentative(
                actor.DisplayName,
                actor.Phone,
                actor.Email,
                agency?.Street,
                agency?.City,
                agency?.State,
                agency?.Zip);

        var pdf = new DhhsFormFiller().Fill(form, subject, selections);
        var blankFields = DhhsFormDefinition.UnfilledFields(form, subject).ToList();
        if (form is DhhsFormDefinition.FormKey.AuthorizationToRelease or
            DhhsFormDefinition.FormKey.AuthorizedRepresentative)
        {
            var isDraft = (selections.Checks?.Count ?? 0) == 0 &&
                (selections.Text?.Count ?? 0) == 0;
            if (isDraft)
                blankFields.Add(form == DhhsFormDefinition.FormKey.AuthorizationToRelease
                    ? "Consumer authorization choices"
                    : "Representative authority choices");
            var fileName = SuggestedFileName(form, person.LastName, person.FirstName, personId);
            // The DHHS release is filed under its exact annual target. The once-only
            // Authorized Representative form has no annual obligation, so it keeps the
            // current period only as its document-store placement.
            var cycleStart = releaseTarget?.TargetEffectiveDate ?? AnnualDocumentCycle.CurrentStart(
                person.EffectiveDate ?? throw new InvalidOperationException("The consumer has no effective date."),
                DateTime.Today);
            var documentKind = form == DhhsFormDefinition.FormKey.AuthorizationToRelease
                ? AnnualDocumentKind.ReleaseDhhs
                : AnnualDocumentKind.DhhsAuthorizedRepresentative;
            await DocumentArtifactStore.StageGeneratedAsync(
                context,
                personId,
                actor.AgencyId,
                documentKind,
                cycleStart,
                isDraft ? DocumentArtifactOrigin.Draft : DocumentArtifactOrigin.GeneratedInSati,
                DateTime.UtcNow,
                actor.Id,
                pdf,
                fileName,
                blankFields,
                cancellationToken,
                releaseObligationId: releaseTarget?.Obligation?.Id);
            LocalAuditTrail.Record(
                context,
                actor,
                LocalAuditActions.DocumentGenerated,
                "Person",
                personId,
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    kind = documentKind.ToString(),
                    cycleStart = cycleStart.ToString("yyyy-MM-dd"),
                    releaseObligationId = releaseTarget?.Obligation?.ObligationId,
                    releaseObligationKey = releaseTarget?.Obligation?.StableKey,
                    origin = isDraft ? DocumentArtifactOrigin.Draft.ToString() : DocumentArtifactOrigin.GeneratedInSati.ToString()
                }));
        }

        // Generating a release form is a disclosure whichever environment produced it,
        // so the local path records the same action name the API route does.
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.DhhsFormGenerated,
            "Person",
            personId,
            $"{{\"Form\":\"{form}\"}}");
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new DhhsFormResult(
            pdf,
            SuggestedFileName(form, person.LastName, person.FirstName, personId),
            blankFields);
    }

    private static async Task<DhhsReleaseTarget> ResolveDhhsReleaseTargetAsync(
        SatiContext context,
        Person person,
        int agencyId,
        DateTime? requestedTargetEffectiveDate,
        Guid? requestedObligationId,
        CancellationToken cancellationToken)
    {
        var effective = person.EffectiveDate ??
            throw new InvalidOperationException("The consumer has no effective date.");
        ReleaseObligation? obligation = null;
        if (requestedObligationId is Guid obligationId)
        {
            obligation = await context.ReleaseObligations.AsNoTracking()
                .Include(item => item.AuthorizationEvents)
                .SingleOrDefaultAsync(item =>
                    item.ObligationId == obligationId &&
                    item.AgencyId == agencyId &&
                    item.PersonId == person.Id,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "The selected DHHS release obligation was not found for this consumer.");
            if (obligation.Category != ReleaseObligationCategory.Dhhs)
                throw new InvalidOperationException(
                    "The selected obligation does not belong to a DHHS release.");
        }

        var timing = await context.Settings.AsNoTracking()
            .Where(item => item.AgencyId == agencyId)
            .Select(item => new
            {
                item.ReleaseDhhsOpenDaysBefore,
                item.ReleaseDhhsDaysBeforeAnniversary
            })
            .SingleOrDefaultAsync(cancellationToken);
        var openDays = Math.Max(0, timing?.ReleaseDhhsOpenDaysBefore ?? 90);
        var dueDays = Math.Max(0, timing?.ReleaseDhhsDaysBeforeAnniversary ?? 0);
        var target = obligation?.TargetEffectiveDate.Date ??
            requestedTargetEffectiveDate?.Date ??
            AnnualDocumentCycle.SuggestedStart(
                effective, DateTime.Today, openDays, dueDays);

        if (requestedTargetEffectiveDate is DateTime requested &&
            requested.Date != target)
            throw new InvalidOperationException(
                "The selected DHHS obligation belongs to a different annual effective-date cycle.");
        if (target < effective.Date ||
            AnnualDocumentCycle.CurrentStart(effective, target) != target)
            throw new ArgumentException(
                "Choose an effective-date anniversary on or after enrollment.",
                nameof(requestedTargetEffectiveDate));

        obligation ??= await context.ReleaseObligations.AsNoTracking()
            .Include(item => item.AuthorizationEvents)
            .SingleOrDefaultAsync(item =>
                item.AgencyId == agencyId &&
                item.PersonId == person.Id &&
                item.Category == ReleaseObligationCategory.Dhhs &&
                item.TargetEffectiveDate == target,
                cancellationToken);

        var availableOn = obligation?.AvailableOn.Date ??
            target.AddDays(-dueDays).AddDays(-openDays);
        if (DateTime.Today < availableOn)
            throw new InvalidOperationException(
                $"This DHHS release becomes available on {availableOn:yyyy-MM-dd}.");
        if (obligation?.RetiredOn is DateTime retiredOn && DateTime.Today >= retiredOn.Date)
            throw new InvalidOperationException(
                "This DHHS release obligation has been retired.");
        if (obligation?.WithdrawnOn is not null)
            throw new InvalidOperationException(
                "This DHHS release authorization was withdrawn and cannot receive a replacement document.");

        return new DhhsReleaseTarget(target, obligation);
    }

    private sealed record DhhsReleaseTarget(
        DateTime TargetEffectiveDate,
        ReleaseObligation? Obligation);

    internal static string SuggestedFileName(
        DhhsFormDefinition.FormKey form,
        string? lastName,
        string? firstName,
        int personId)
    {
        var name = new string($"{lastName}-{firstName}"
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray())
            .Trim('-');
        return string.IsNullOrEmpty(name)
            ? $"{form}-{personId}.pdf"
            : $"{form}-{personId}-{name}.pdf";
    }
}
