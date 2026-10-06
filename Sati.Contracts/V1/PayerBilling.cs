using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sati.Contracts.V1;

public enum PayerBillingProfileKind { MaineCareSection13ClaimMd = 1, OtherProfessional = 2 }

/// <summary>Agency decisions, never evidence of enrollment or payer certification by themselves.</summary>
public sealed record PayerBillingConfiguration
{
    public string ProfileKey { get; init; } = "";
    public PayerBillingProfileKind Kind { get; init; } = PayerBillingProfileKind.MaineCareSection13ClaimMd;
    public DateTime EffectiveOn { get; init; }
    public DateTime? ExpiresOn { get; init; }
    public string BillingProviderName { get; init; } = "";
    public string BillingProviderNpi { get; init; } = "";
    public string BillingProviderTaxId { get; init; } = "";
    public string BillingTaxonomy { get; init; } = "";
    public string BillingStreet { get; init; } = "";
    public string BillingCity { get; init; } = "";
    public string BillingState { get; init; } = "";
    public string BillingZip { get; init; } = "";
    public string RenderingProviderName { get; init; } = "";
    public string RenderingProviderFirstName { get; init; } = "";
    public string RenderingProviderEntityType { get; init; } = "2";
    public string RenderingProviderNpi { get; init; } = "";
    public string RenderingTaxonomy { get; init; } = "";
    public string FacilityName { get; init; } = "";
    public string FacilityStreet { get; init; } = "";
    public string FacilityCity { get; init; } = "";
    public string FacilityState { get; init; } = "";
    public string FacilityZip { get; init; } = "";
    public string FacilityNpi { get; init; } = "";
    public string FacilityIdQualifier { get; init; } = "";
    public string FacilityId { get; init; } = "";
    public string PayerName { get; init; } = "";
    public string PayerId { get; init; } = "";
    public string ClaimFilingIndicator { get; init; } = "MC";
    public string SubmitterId { get; init; } = "";
    public string ContactName { get; init; } = "";
    public string ContactPhone { get; init; } = "";
    public string ProcedureCode { get; init; } = "";
    public IReadOnlyList<string> Modifiers { get; init; } = [];
    public decimal UnitRate { get; init; }
    public bool RequiresAuthorization { get; init; } = true;
    public bool RequirementsReviewed { get; init; }
    public string RequirementsEvidenceReference { get; init; } = "";
}

public sealed record PayerBillingVersionDto(Guid VersionId, int AgencyId, long Revision,
    PayerBillingConfiguration Configuration, int CreatedByUserId, DateTime RecordedAtUtc);
public sealed record PublishPayerBillingRequest(Guid ChangeId, long ExpectedRevision, PayerBillingConfiguration Configuration);
public sealed record PayerBillingFieldError(string Field, string Message);
public sealed record PayerAuthorizationReference(int PersonId, string ProfileKey, string ProcedureCode,
    string ProviderNpi, string Reference, DateTime EffectiveOn, DateTime ExpiresOn,
    string EvidenceReference, bool CoverageReviewed)
{
    public DateTime AuthorizedOn { get; init; }
    public IReadOnlyList<string> Modifiers { get; init; } = [];
}
public sealed record PayerClaimPreparation(string ProfileKey, Guid ExpectedVersionId,
    PayerAuthorizationReference? Authorization);
public sealed record PayerClaimInputs(PayerBillingVersionDto ConfigurationVersion,
    PayerAuthorizationReference? Authorization, int VerifiedByUserId, DateTime VerifiedAtUtc,
    string? PreviousSnapshotSha256 = null);
public sealed record PayerClaimPreviewDto(int NoteId, Guid? ConfigurationVersionId,
    IReadOnlyList<PayerBillingFieldError> Errors, string ProcedureCode, IReadOnlyList<string> Modifiers,
    decimal Units, decimal Charge, string FacilityId)
{
    public bool IsReady => Errors.Count == 0;
}

/// <summary>Single shared owner for configuration, date selection and frozen payer inputs.</summary>
public static partial class PayerBillingRules
{
    public const string RevisionCode = "payer_configuration_revision_conflict";
    public static bool CanRead(AgencyActor actor) => CanPrepare(actor) || CanPublish(actor);
    public static bool CanPrepare(AgencyActor actor) => actor.UserId > 0 && actor.AgencyId > 0 && UserPermissionRules.HasBillingPermissions(actor.Permissions);
    public static bool CanPublish(AgencyActor actor) => actor.UserId > 0 && actor.AgencyId > 0 && UserPermissionRules.HasAdminPermissions(actor.Permissions);

    public static IReadOnlyList<PayerBillingFieldError> Validate(PayerBillingConfiguration? c)
    {
        var errors = new List<PayerBillingFieldError>();
        if (c is null) return [new("Configuration", "Configuration is required.")];
        void Check(bool valid, string field, string message) { if (!valid) errors.Add(new(field, message)); }
        void Element(string? value, int length, string field) => Check(BillingRules.IsSafeX12Element(value, length), field,
            $"{field} is required and must contain printable ASCII without X12 delimiters (maximum {length} characters).");
        Check(ProfileKeyPattern().IsMatch(c.ProfileKey ?? ""), nameof(c.ProfileKey), "Use 1–40 lowercase letters, digits or hyphens for the profile key.");
        Check(Enum.IsDefined(c.Kind), nameof(c.Kind), "Unsupported payer profile kind.");
        Check(c.EffectiveOn.Year is >= 2000 and <= 2200 && c.EffectiveOn.TimeOfDay == TimeSpan.Zero,
            nameof(c.EffectiveOn), "An effective date between 2000 and 2200 is required, without a time.");
        Check(c.ExpiresOn is null || (c.ExpiresOn >= c.EffectiveOn && c.ExpiresOn.Value.Year <= 2200 && c.ExpiresOn.Value.TimeOfDay == TimeSpan.Zero),
            nameof(c.ExpiresOn), "Expiration must be on or after the effective date, without a time.");
        Element(c.BillingProviderName, 60, nameof(c.BillingProviderName));
        Check(BillingRules.IsValidNpi(c.BillingProviderNpi), nameof(c.BillingProviderNpi), "Billing NPI must be a valid ten-digit NPI.");
        Check(DigitsNinePattern().IsMatch(c.BillingProviderTaxId ?? ""), nameof(c.BillingProviderTaxId), "Billing tax ID must contain nine digits.");
        Check(TaxonomyPattern().IsMatch(c.BillingTaxonomy ?? ""), nameof(c.BillingTaxonomy), "Billing taxonomy must contain ten uppercase letters or digits.");
        Element(c.BillingStreet, 55, nameof(c.BillingStreet)); Element(c.BillingCity, 30, nameof(c.BillingCity));
        Check(StatePattern().IsMatch(c.BillingState ?? ""), nameof(c.BillingState), "Billing state must contain two uppercase letters.");
        Check(ZipPattern().IsMatch(c.BillingZip ?? ""), nameof(c.BillingZip), "Billing ZIP must contain five or nine digits.");
        Element(c.RenderingProviderName, 60, nameof(c.RenderingProviderName));
        Check(c.RenderingProviderEntityType is "1" or "2", nameof(c.RenderingProviderEntityType), "Rendering entity type must be 1 (person) or 2 (organization).");
        Check(c.RenderingProviderEntityType == "1" ? BillingRules.IsSafeX12Element(c.RenderingProviderFirstName, 35) : string.IsNullOrEmpty(c.RenderingProviderFirstName),
            nameof(c.RenderingProviderFirstName), "Provide a first name for a person; leave it empty for an organization.");
        Check(BillingRules.IsValidNpi(c.RenderingProviderNpi), nameof(c.RenderingProviderNpi), "Rendering NPI must be a valid ten-digit NPI.");
        Check(TaxonomyPattern().IsMatch(c.RenderingTaxonomy ?? ""), nameof(c.RenderingTaxonomy), "Rendering taxonomy must contain ten uppercase letters or digits.");
        Element(c.FacilityName, 60, nameof(c.FacilityName)); Element(c.FacilityStreet, 55, nameof(c.FacilityStreet));
        Element(c.FacilityCity, 30, nameof(c.FacilityCity));
        Check(StatePattern().IsMatch(c.FacilityState ?? ""), nameof(c.FacilityState), "Facility state must contain two uppercase letters.");
        Check(ZipPattern().IsMatch(c.FacilityZip ?? ""), nameof(c.FacilityZip), "Facility ZIP must contain five or nine digits.");
        Check(string.IsNullOrEmpty(c.FacilityNpi) || BillingRules.IsValidNpi(c.FacilityNpi), nameof(c.FacilityNpi), "Optional facility NPI must be a valid ten-digit NPI.");
        Check(c.FacilityIdQualifier is "LU" or "G2", nameof(c.FacilityIdQualifier), "Select the LU or G2 qualifier confirmed in your payer/agency evidence; no qualifier is inferred.");
        Element(c.FacilityId, 50, nameof(c.FacilityId)); Element(c.PayerName, 60, nameof(c.PayerName));
        Element(c.PayerId, 80, nameof(c.PayerId)); Element(c.SubmitterId, 15, nameof(c.SubmitterId));
        Check(c.ClaimFilingIndicator is "MC" or "CI", nameof(c.ClaimFilingIndicator), "Supported filing indicators are MC (Medicaid) and CI (commercial). Other types require a separately reviewed extension.");
        Check(c.Kind == PayerBillingProfileKind.MaineCareSection13ClaimMd ||
            !new[] { "MEMCD", "MCDME", "SKME0" }.Contains(c.PayerId?.Trim(), StringComparer.OrdinalIgnoreCase),
            nameof(c.Kind), "Maine Medicaid routing must use the documented MaineCare profile; another kind cannot bypass its prior-authorization requirements.");
        Element(c.ContactName, 60, nameof(c.ContactName));
        Check(PhonePattern().IsMatch(c.ContactPhone ?? ""), nameof(c.ContactPhone), "Contact phone must contain 10–15 digits.");
        Check(BillingRules.IsValidProcedureCode(c.ProcedureCode), nameof(c.ProcedureCode), "A valid procedure code is required.");
        Check(c.Modifiers is not null && c.Modifiers.Count <= 4 && c.Modifiers.All(m => !string.IsNullOrEmpty(m) && BillingRules.IsValidModifier(m)) &&
            c.Modifiers.Distinct(StringComparer.Ordinal).Count() == c.Modifiers.Count, nameof(c.Modifiers), "Supply up to four distinct two-character modifiers in submission order.");
        Check(c.UnitRate > 0 && c.UnitRate <= 999999m && decimal.Round(c.UnitRate, 2) == c.UnitRate, nameof(c.UnitRate), "Unit rate must be positive with at most two decimal places.");
        Check(c.RequirementsReviewed, nameof(c.RequirementsReviewed), "An administrator must review payer, provider, facility, coding and enrollment requirements.");
        Check(ValidEvidence(c.RequirementsEvidenceReference), nameof(c.RequirementsEvidenceReference), "A protected evidence reference for that review is required; do not paste clinical content or credentials.");
        if (c.Kind == PayerBillingProfileKind.MaineCareSection13ClaimMd)
        {
            Check(c.ClaimFilingIndicator == "MC", nameof(c.ClaimFilingIndicator), "MaineCare uses the Medicaid filing indicator MC.");
            Check(c.PayerId == "MEMCD", nameof(c.PayerId), "Claim.MD Maine Medicaid routing uses MEMCD; direct MIHMS routing is a separate profile.");
            Check(FacilityPattern().IsMatch(c.FacilityId ?? ""), nameof(c.FacilityId), "Claim.MD Maine Medicaid facility ID must use xxxx-xxx (letters/digits with the dash).");
            Check(c.RequiresAuthorization, nameof(c.RequiresAuthorization), "The Section 13 rule effective April 28, 2026 requires prior authorization.");
            var mods = string.Join(",", c.Modifiers ?? []);
            Check((c.ProcedureCode == "T1017" && mods is "" or "UD" or "UC" or "UB" or "HF" or "U5") ||
                (c.ProcedureCode == "G9012" && mods is "" or "HI"), nameof(c.Modifiers),
                "Select a Section 13 code/modifier combination from the April 28, 2026 table and verify population/enrollment applicability.");
            Check(c.EffectiveOn >= new DateTime(2026, 4, 28), nameof(c.EffectiveOn), "This documented Section 13 profile supports service dates from April 28, 2026; older dates require a separately evidenced profile.");
        }
        return errors;
    }

    public static PayerBillingVersionDto? Resolve(IEnumerable<PayerBillingVersionDto> versions, int agencyId,
        string profileKey, DateTime serviceDate)
    {
        // A newer start supersedes the older version, even when the newer one has expired.
        // Never silently fall back to older routing after expiration.
        var version = versions.Where(v => v.AgencyId == agencyId && v.Configuration.ProfileKey == profileKey &&
            v.Configuration.EffectiveOn <= serviceDate.Date).OrderByDescending(v => v.Configuration.EffectiveOn)
            .ThenByDescending(v => v.Revision).FirstOrDefault();
        return version?.Configuration.ExpiresOn is DateTime end && serviceDate.Date > end ? null : version;
    }

    public static IReadOnlyList<PayerBillingFieldError> ValidatePreparation(PayerBillingVersionDto? version,
        PayerClaimPreparation? preparation, int agencyId, int personId, DateTime serviceDate)
    {
        var errors = new List<PayerBillingFieldError>();
        if (version is null) return [new("ProfileKey", "No effective payer configuration covers this service date. Select a profile and refresh.")];
        errors.AddRange(Validate(version.Configuration));
        if (version.AgencyId != agencyId || version.VersionId == Guid.Empty || version.Revision <= 0)
            errors.Add(new("ConfigurationVersionId", "Configuration ownership or version is invalid."));
        if (preparation is null || preparation.ExpectedVersionId != version.VersionId || preparation.ProfileKey != version.Configuration.ProfileKey)
            errors.Add(new("ConfigurationVersionId", "The payer configuration changed or was not selected. Refresh the preview before creating a claim."));
        var c = version.Configuration;
        if (serviceDate.Date < c.EffectiveOn || (c.ExpiresOn is DateTime end && serviceDate.Date > end))
            errors.Add(new("EffectiveOn", "The frozen configuration does not cover the service date."));
        var a = preparation?.Authorization;
        if (c.RequiresAuthorization && a is null) errors.Add(new("Authorization.Reference", "Prior authorization and reviewed coverage evidence are required."));
        if (a is not null)
        {
            if (a.PersonId != personId || a.ProfileKey != c.ProfileKey || a.ProcedureCode != c.ProcedureCode || a.ProviderNpi != c.RenderingProviderNpi ||
                a.Modifiers is null || !a.Modifiers.SequenceEqual(c.Modifiers ?? [], StringComparer.Ordinal))
                errors.Add(new("Authorization.Scope", "Authorization must identify this consumer, payer profile, procedure and rendering provider."));
            if (!BillingRules.IsSafeX12Element(a.Reference, 50)) errors.Add(new("Authorization.Reference", "Authorization reference must be 1–50 printable characters without X12 delimiters."));
            if (a.EffectiveOn.TimeOfDay != TimeSpan.Zero || a.ExpiresOn.TimeOfDay != TimeSpan.Zero || a.EffectiveOn > a.ExpiresOn ||
                serviceDate.Date < a.EffectiveOn || serviceDate.Date > a.ExpiresOn)
                errors.Add(new("Authorization.EffectiveOn", "Authorization coverage dates must include the service date."));
            if (a.AuthorizedOn.Year < 2000 || a.AuthorizedOn.TimeOfDay != TimeSpan.Zero || a.AuthorizedOn.Date > serviceDate.Date)
                errors.Add(new("Authorization.AuthorizedOn", "Record the evidenced authorization decision date, on or before the service date."));
            if (!a.CoverageReviewed || !ValidEvidence(a.EvidenceReference)) errors.Add(new("Authorization.EvidenceReference", "Explicit coverage review and a protected authorization evidence reference are required."));
        }
        return errors;
    }

    public static IReadOnlyList<PayerBillingFieldError> ValidateServiceQuantity(PayerBillingConfiguration c, int? minutes) =>
        c.Kind == PayerBillingProfileKind.MaineCareSection13ClaimMd && minutes is not >= 15
            ? [new("Note.Minutes", "The Section 13 profile requires at least 15 minutes of substantive service contact.")]
            : [];

    public static ProfessionalClaimSnapshot Freeze(ProfessionalClaimSnapshot subscriber, PayerBillingVersionDto version,
        PayerClaimPreparation preparation, int actorUserId, DateTime recordedAtUtc, DateTime serviceDate)
    {
        var errors = ValidatePreparation(version, preparation, subscriber.AgencyId, subscriber.PersonId, serviceDate);
        if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}")));
        if (actorUserId <= 0 || recordedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("A verified actor and UTC timestamp are required.");
        var c = version.Configuration;
        return subscriber with
        {
            Version = ProfessionalClaimSnapshotCodec.PayerConfiguredVersion,
            BillingProviderName = c.BillingProviderName, BillingProviderNpi = c.BillingProviderNpi,
            BillingProviderTaxId = c.BillingProviderTaxId, BillingProviderStreet = c.BillingStreet,
            BillingProviderCity = c.BillingCity, BillingProviderState = c.BillingState, BillingProviderZip = c.BillingZip,
            SubmitterId = c.SubmitterId, SubmitterContactName = c.ContactName, SubmitterContactPhone = c.ContactPhone,
            PayerName = c.PayerName, PayerId = c.PayerId,
            PayerInputs = new(version, preparation.Authorization, actorUserId, recordedAtUtc)
        };
    }

    public static ProfessionalClaimSnapshot PreserveCorrection(ProfessionalClaimSnapshot currentSubscriber,
        string previousSnapshotJson, DateTime correctedServiceDate)
    {
        var previous = ProfessionalClaimSnapshotCodec.Deserialize(previousSnapshotJson);
        if (previous.PayerInputs is not { } inputs) return currentSubscriber;
        var preparation = new PayerClaimPreparation(inputs.ConfigurationVersion.Configuration.ProfileKey,
            inputs.ConfigurationVersion.VersionId, inputs.Authorization);
        // Reuse the exact original provider/configuration/authorization evidence. A date outside
        // its scope needs an explicit new billing preparation, never an implicit configuration switch.
        var frozen = Freeze(currentSubscriber, inputs.ConfigurationVersion, preparation,
            inputs.VerifiedByUserId, inputs.VerifiedAtUtc, correctedServiceDate);
        return frozen with { PayerInputs = inputs with { PreviousSnapshotSha256 =
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(previousSnapshotJson))).ToLowerInvariant() } };
    }

    public static IReadOnlyList<PayerBillingFieldError> ValidateFrozen(ProfessionalClaimSnapshot snapshot, ProfessionalClaimLineFacts line)
    {
        if (snapshot.PayerInputs is not { } inputs) return [];
        var v = inputs.ConfigurationVersion;
        var errors = ValidatePreparation(v, new(v.Configuration.ProfileKey, v.VersionId, inputs.Authorization),
            snapshot.AgencyId, snapshot.PersonId, line.DateOfService).ToList();
        var c = v.Configuration;
        if (errors.Count != 0) return errors;
        if (v.CreatedByUserId <= 0 || v.RecordedAtUtc.Kind != DateTimeKind.Utc || v.RecordedAtUtc.Year < 2000 || v.RecordedAtUtc > inputs.VerifiedAtUtc)
            errors.Add(new("PayerInputs.ConfigurationVersion", "Frozen configuration publication provenance is invalid."));
        if (c.Kind == PayerBillingProfileKind.MaineCareSection13ClaimMd && line.Units is < 1)
            errors.Add(new("Units", "The Section 13 claim must contain at least one substantive-contact unit."));
        var expected = Freeze(snapshot, v, new(c.ProfileKey, v.VersionId, inputs.Authorization),
            Math.Max(1, inputs.VerifiedByUserId), DateTime.SpecifyKind(inputs.VerifiedAtUtc, DateTimeKind.Utc), line.DateOfService);
        if (inputs.VerifiedByUserId <= 0 || inputs.VerifiedAtUtc.Kind != DateTimeKind.Utc)
            errors.Add(new("PayerInputs.VerifiedByUserId", "Frozen verification provenance is invalid."));
        if (snapshot.BillingProviderName != expected.BillingProviderName || snapshot.BillingProviderNpi != expected.BillingProviderNpi ||
            snapshot.BillingProviderTaxId != expected.BillingProviderTaxId || snapshot.BillingProviderStreet != expected.BillingProviderStreet ||
            snapshot.BillingProviderCity != expected.BillingProviderCity || snapshot.BillingProviderState != expected.BillingProviderState ||
            snapshot.BillingProviderZip != expected.BillingProviderZip || snapshot.SubmitterId != expected.SubmitterId ||
            snapshot.SubmitterContactName != expected.SubmitterContactName || snapshot.SubmitterContactPhone != expected.SubmitterContactPhone ||
            snapshot.PayerName != expected.PayerName || snapshot.PayerId != expected.PayerId)
            errors.Add(new("PayerInputs.Configuration", "Frozen provider/routing fields do not match the configuration version."));
        if (line.ProcedureCode != c.ProcedureCode || (line.ProcedureModifier ?? "") != (c.Modifiers.FirstOrDefault() ?? "") || line.RenderingProviderNpi != c.RenderingProviderNpi)
            errors.Add(new("PayerInputs.ProcedureCode", "Claim service/provider identifiers do not match frozen payer inputs."));
        // Corrections may carry explicitly reviewed financial amounts. Rate provenance is frozen;
        // the existing correction workflow remains authoritative for those financial decisions.
        return errors;
    }

    private static bool ValidEvidence(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 &&
        value.All(c => c >= 32 && c != 127) && !value.Contains('\n') && !value.Contains('\r');
    [GeneratedRegex("^[a-z0-9-]{1,40}$")] private static partial Regex ProfileKeyPattern();
    [GeneratedRegex("^[0-9]{9}$")] private static partial Regex DigitsNinePattern();
    [GeneratedRegex("^[A-Z0-9]{10}$")] private static partial Regex TaxonomyPattern();
    [GeneratedRegex("^[A-Z]{2}$")] private static partial Regex StatePattern();
    [GeneratedRegex("^[0-9]{5}([0-9]{4})?$")] private static partial Regex ZipPattern();
    [GeneratedRegex("^[0-9]{10,15}$")] private static partial Regex PhonePattern();
    [GeneratedRegex("^[A-Za-z0-9]{4}-[A-Za-z0-9]{3}$")] private static partial Regex FacilityPattern();
}
