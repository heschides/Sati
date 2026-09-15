using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Forms;
using Sati.Models;
using System.Text.Json;

namespace Sati.Data;

/// <summary>Local Production generation. All profile reads and rendering stay on the workstation.</summary>
public sealed class AgencyReleaseService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService,
    AgencyReleasePdfGenerator generator,
    MedicalReleasePdfGenerator medicalGenerator) : IAgencyReleaseService
{
    public AgencyReleaseService(
        IDbContextFactory<SatiContext> contextFactory,
        ISessionService sessionService,
        AgencyReleasePdfGenerator generator)
        : this(contextFactory, sessionService, generator, new MedicalReleasePdfGenerator(generator))
    {
    }

    public async Task<AgencyReleaseResult> GenerateAsync(
        int personId,
        AgencyReleaseRequest request,
        CancellationToken cancellationToken = default)
    {
        return await GenerateAsync(
            personId, request, AnnualDocumentKind.ReleaseAgency, null, cancellationToken);
    }

    public Task<AgencyReleaseResult> GenerateMedicalAsync(
        int personId,
        AgencyReleaseRequest request,
        CancellationToken cancellationToken = default) =>
        GenerateAsync(personId, request, AnnualDocumentKind.ReleaseMedical, null, cancellationToken);

    public Task<AgencyReleaseResult> GenerateForObligationAsync(
        int personId,
        AgencyReleaseRequest request,
        Guid releaseObligationId,
        CancellationToken cancellationToken = default) =>
        GenerateAsync(
            personId,
            request,
            AnnualDocumentKind.ReleaseAgency,
            RequiredObligationId(releaseObligationId),
            cancellationToken);

    public Task<AgencyReleaseResult> GenerateMedicalForObligationAsync(
        int personId,
        AgencyReleaseRequest request,
        Guid releaseObligationId,
        CancellationToken cancellationToken = default) =>
        GenerateAsync(
            personId,
            request,
            AnnualDocumentKind.ReleaseMedical,
            RequiredObligationId(releaseObligationId),
            cancellationToken);

    private async Task<AgencyReleaseResult> GenerateAsync(
        int personId,
        AgencyReleaseRequest request,
        AnnualDocumentKind kind,
        Guid? releaseObligationId,
        CancellationToken cancellationToken)
    {
        AgencyReleaseRules.EnsureValid(request);
        if (request.IsRevocation && releaseObligationId is not null)
            throw new InvalidOperationException(
                "A revocation cannot replace the document linked to a release obligation. Record the withdrawal separately so the historical authorization is preserved.");
        var actor = sessionService.CurrentUser
            ?? throw new InvalidOperationException("An agency release cannot be generated without a signed-in user.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await LocalTenantAccess.EnsureSessionAsync(context, sessionService);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await LocalTenantAccess.EnsureCurrentActorAsync(context, actor, cancellationToken);
        if (!await LocalTenantAccess.OwnsPersonAsync(context, actor, personId, cancellationToken))
            throw new InvalidOperationException("That consumer is not on your current caseload.");
        var person = await context.People.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == personId &&
                         candidate.UserId == actor.Id &&
                         candidate.AgencyId == actor.AgencyId,
            cancellationToken)
            ?? throw new InvalidOperationException("That consumer is not on your caseload.");
        var releaseObligation = await ResolveReleaseObligationAsync(
            context,
            personId,
            actor.AgencyId,
            kind,
            releaseObligationId,
            DateTime.Today,
            cancellationToken);
        var agency = await context.Agencies.AsNoTracking().SingleAsync(
            candidate => candidate.Id == actor.AgencyId,
            cancellationToken);

        var generatedAtUtc = DateTime.UtcNow;
        var subject = new AgencyReleaseSubject(
            person.Id,
            person.FullName,
            person.BirthDate,
            person.HasGuardian ? person.GuardianName : null,
            agency.Name,
            ComposeAddress(agency.Street, agency.City, agency.State, agency.Zip),
            agency.EdiContactPhone,
            actor.DisplayName,
            actor.Role.ToString());
        var pdf = kind == AnnualDocumentKind.ReleaseMedical
            ? medicalGenerator.Generate(subject, request, generatedAtUtc)
            : generator.Generate(subject, request, generatedAtUtc);
        var fileName = SuggestedFileName(
            person.Id, person.LastName, person.FirstName, request.IsRevocation, kind, request.IsDraft);
        var cycleStart = releaseObligation?.TargetEffectiveDate.Date ??
            AnnualDocumentCycle.CurrentStart(
                person.EffectiveDate ?? throw new InvalidOperationException("The consumer has no effective date."),
                generatedAtUtc.ToLocalTime());
        await DocumentArtifactStore.StageGeneratedAsync(
            context,
            person.Id,
            actor.AgencyId,
            kind,
            cycleStart,
            request.IsDraft ? DocumentArtifactOrigin.Draft : DocumentArtifactOrigin.GeneratedInSati,
            generatedAtUtc,
            actor.Id,
            pdf,
            fileName,
            request.IsDraft ? DraftBlankFields(request) : [],
            cancellationToken,
            releaseObligationId: releaseObligation?.Id);

        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.AgencyReleaseGenerated,
            "Person",
            personId,
            JsonSerializer.Serialize(new
            {
                Scope = request.Scope,
                StaffAttestation = request.ConfirmedObtainedRoi,
                Revocation = request.IsRevocation,
                ReleaseObligationId = releaseObligation?.ObligationId,
                ReleaseObligationKey = releaseObligation?.StableKey,
            }));
        LocalAuditTrail.Record(
            context,
            actor,
            LocalAuditActions.DocumentGenerated,
            "Person",
            personId,
            JsonSerializer.Serialize(new
            {
                kind = kind.ToString(),
                cycleStart = cycleStart.ToString("yyyy-MM-dd"),
                origin = request.IsDraft
                    ? DocumentArtifactOrigin.Draft.ToString()
                    : DocumentArtifactOrigin.GeneratedInSati.ToString(),
                releaseObligationId = releaseObligation?.ObligationId,
                releaseObligationKey = releaseObligation?.StableKey
            }));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AgencyReleaseResult(
            pdf,
            fileName);
    }

    private static async Task<ReleaseObligation?> ResolveReleaseObligationAsync(
        SatiContext context,
        int personId,
        int agencyId,
        AnnualDocumentKind kind,
        Guid? obligationId,
        DateTime today,
        CancellationToken cancellationToken)
    {
        if (obligationId is null)
            return null;

        var expectedCategory = kind switch
        {
            AnnualDocumentKind.ReleaseAgency => ReleaseObligationCategory.Agency,
            AnnualDocumentKind.ReleaseMedical => ReleaseObligationCategory.Medical,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var obligation = await context.ReleaseObligations.AsNoTracking()
            .Include(item => item.AuthorizationEvents)
            .SingleOrDefaultAsync(item =>
                    item.ObligationId == obligationId.Value &&
                    item.PersonId == personId &&
                    item.AgencyId == agencyId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "That release obligation no longer exists for this consumer.");
        if (obligation.Category != expectedCategory)
            throw new InvalidOperationException(
                $"The selected obligation requires a {obligation.Category} release, not a {expectedCategory} release.");
        if (today.Date < obligation.AvailableOn.Date)
            throw new InvalidOperationException(
                $"This release obligation becomes available on {obligation.AvailableOn:yyyy-MM-dd}.");
        if (obligation.RetiredOn is DateTime retiredOn && today.Date >= retiredOn.Date)
            throw new InvalidOperationException(
                "This release obligation has been retired and cannot receive a new document.");
        if (obligation.WithdrawnOn is not null)
            throw new InvalidOperationException(
                "This release authorization was withdrawn and cannot receive a replacement document.");
        return obligation;
    }

    private static Guid RequiredObligationId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A release obligation is required.", nameof(value));
        return value;
    }

    internal static string SuggestedFileName(
        int personId,
        string? lastName,
        string? firstName,
        bool revocation,
        AnnualDocumentKind kind = AnnualDocumentKind.ReleaseAgency,
        bool draft = false)
    {
        var name = new string($"{lastName}-{firstName}"
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        var basePrefix = kind == AnnualDocumentKind.ReleaseMedical ? "Medical-Release" : "Agency-Release";
        var prefix = revocation ? $"{basePrefix}-Revocation" : basePrefix;
        if (draft)
            prefix += "-DRAFT";
        return string.IsNullOrEmpty(name)
            ? $"{prefix}-{personId}.pdf"
            : $"{prefix}-{personId}-{name}.pdf";
    }

    internal static string? ComposeAddress(params string?[] parts)
    {
        var present = parts.Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToArray();
        return present.Length == 0 ? null : string.Join(", ", present);
    }

    private static IReadOnlyList<string> DraftBlankFields(AgencyReleaseRequest request)
    {
        var fields = new List<string>();
        if (request.AuthorizationGranted is null) fields.Add(nameof(request.AuthorizationGranted));
        if (string.IsNullOrWhiteSpace(request.ContactName)) fields.Add(nameof(request.ContactName));
        if (request.InformationCategories is null || request.InformationCategories.Count == 0) fields.Add(nameof(request.InformationCategories));
        if (request.StartDate is null) fields.Add(nameof(request.StartDate));
        if (request.ExpirationDate is null) fields.Add(nameof(request.ExpirationDate));
        if (string.IsNullOrWhiteSpace(request.Scope)) fields.Add(nameof(request.Scope));
        if (request.IncludeDrugAlcohol is null) fields.Add(nameof(request.IncludeDrugAlcohol));
        if (request.IncludeMentalHealth is null) fields.Add(nameof(request.IncludeMentalHealth));
        if (request.IncludeHivAids is null) fields.Add(nameof(request.IncludeHivAids));
        return fields;
    }
}
