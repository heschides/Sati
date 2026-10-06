using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Data.Cloud;
using Sati.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace Sati.ViewModels.Billing;

public sealed partial class PayerBillingField(string key, string label) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    [ObservableProperty] private string value = "";
}
public sealed record PayerBillingNote(int NoteId, int PersonId, DateTime ServiceDate)
{
    public string Display => $"Note {NoteId} · consumer {PersonId} · {ServiceDate:yyyy-MM-dd}";
}
public sealed record PayerBillingKindChoice(PayerBillingProfileKind Kind, string Display);
public partial class PayerBillingViewModel : ObservableObject
{
    private readonly IBillingService service;
    private readonly ISessionService session;
    private readonly LatestRequestTracker requests = new();
    private bool populating;
    private long operationOwner;
    private PublishPayerBillingRequest? pendingPublication;
    private PayerClaimPreparation? previewPreparation;
    private int previewNoteId;
    public ObservableCollection<PayerBillingVersionDto> Versions { get; } = [];
    public ObservableCollection<PayerBillingField> Fields { get; } = [];
    public ObservableCollection<PayerBillingNote> Notes { get; } = [];
    public ObservableCollection<string> Errors { get; } = [];
    public IReadOnlyList<PayerBillingKindChoice> Kinds { get; } = [
        new(PayerBillingProfileKind.MaineCareSection13ClaimMd, "MaineCare Section 13 through Claim.MD"),
        new(PayerBillingProfileKind.OtherProfessional, "Other professional payer (current Section 13 unit basis)")];
    [ObservableProperty] private PayerBillingVersionDto? selectedVersion;
    [ObservableProperty] private PayerBillingNote? selectedNote;
    [ObservableProperty] private PayerBillingProfileKind kind = PayerBillingProfileKind.MaineCareSection13ClaimMd;
    [ObservableProperty] private string effectiveOn = "2026-04-28";
    [ObservableProperty] private string expiresOn = "";
    [ObservableProperty] private string modifiers = "";
    [ObservableProperty] private string unitRate = "";
    [ObservableProperty] private bool requiresAuthorization = true;
    [ObservableProperty] private bool requirementsReviewed;
    [ObservableProperty] private string authorizationReference = "";
    [ObservableProperty] private string authorizationDecisionDate = "";
    [ObservableProperty] private string authorizationEffectiveOn = "";
    [ObservableProperty] private string authorizationExpiresOn = "";
    [ObservableProperty] private string authorizationEvidenceReference = "";
    [ObservableProperty] private bool authorizationCoverageReviewed;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool previewReady;
    [ObservableProperty] private string statusMessage = "Select or publish an evidenced payer profile. Identifiers alone do not establish enrollment or payer acceptance.";
    public bool CanEdit => !IsBusy && session.CurrentUser is { } user && PayerBillingRules.CanPublish(user.ToAgencyActor());
    public bool IsConfigurationReadOnly => !CanEdit;
    public bool CanPrepare => !IsBusy && session.CurrentUser is { } user && PayerBillingRules.CanPrepare(user.ToAgencyActor());
    public bool CanLoad => !IsBusy && session.CurrentUser is { } user && PayerBillingRules.CanRead(user.ToAgencyActor());

    public PayerBillingViewModel(IBillingService service, ISessionService session)
    {
        this.service = service; this.session = session;
        AddField("ProfileKey", "Profile Key");
        AddField("BillingProviderName", "Billing Provider Name");
        AddField("BillingProviderNpi", "Billing Provider NPI");
        AddField("BillingProviderTaxId", "Billing Provider Tax ID");
        AddField("BillingTaxonomy", "Billing Taxonomy");
        AddField("BillingStreet", "Billing Street");
        AddField("BillingCity", "Billing City");
        AddField("BillingState", "Billing State");
        AddField("BillingZip", "Billing Zip");
        AddField("RenderingProviderName", "Rendering Provider Name");
        AddField("RenderingProviderFirstName", "Rendering Provider First Name");
        AddField("RenderingProviderEntityType", "Rendering Provider Entity Type");
        AddField("RenderingProviderNpi", "Rendering Provider NPI");
        AddField("RenderingTaxonomy", "Rendering Taxonomy");
        AddField("FacilityName", "Facility Name");
        AddField("FacilityStreet", "Facility Street");
        AddField("FacilityCity", "Facility City");
        AddField("FacilityState", "Facility State");
        AddField("FacilityZip", "Facility Zip");
        AddField("FacilityNpi", "Facility NPI");
        AddField("FacilityIdQualifier", "Facility ID Qualifier");
        AddField("FacilityId", "Facility ID");
        AddField("PayerName", "Payer Name");
        AddField("PayerId", "Payer ID");
        AddField("ClaimFilingIndicator", "Claim filing indicator (MC Medicaid, CI commercial)");
        AddField("SubmitterId", "Submitter ID");
        AddField("ContactName", "Contact Name");
        AddField("ContactPhone", "Contact Phone");
        AddField("ProcedureCode", "Procedure Code");
        AddField("RequirementsEvidenceReference", "Requirements Evidence Reference");
        PropertyChanged += (_, e) => {
            if (!populating && e.PropertyName is nameof(Kind) or nameof(EffectiveOn) or nameof(ExpiresOn) or nameof(Modifiers) or nameof(UnitRate) or nameof(RequiresAuthorization))
                RequirementsReviewed = false;
            if (!populating && e.PropertyName is nameof(SelectedNote) or nameof(SelectedVersion) or nameof(AuthorizationReference) or nameof(AuthorizationDecisionDate) or
                nameof(AuthorizationEffectiveOn) or nameof(AuthorizationExpiresOn) or nameof(AuthorizationEvidenceReference))
                AuthorizationCoverageReviewed = false;
            if (e.PropertyName is not (nameof(IsBusy) or nameof(StatusMessage) or nameof(PreviewReady) or nameof(CanEdit) or nameof(IsConfigurationReadOnly) or nameof(CanPrepare) or nameof(CanLoad))) InvalidatePreview();
        };
    }
    private void AddField(string key, string label)
    {
        var field = new PayerBillingField(key, label); Fields.Add(field);
        field.PropertyChanged += (_, _) => { if (!populating) RequirementsReviewed = false; InvalidatePreview(); };
    }
    private void InvalidatePreview()
    {
        if (populating) return;
        if (PreviewReady) StatusMessage = "Inputs changed. Preview readiness again before creating a claim.";
        requests.Invalidate(); PreviewReady = false; previewPreparation = null;
    }
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(IsConfigurationReadOnly)); OnPropertyChanged(nameof(CanPrepare)); OnPropertyChanged(nameof(CanLoad)); }
    partial void OnSelectedVersionChanged(PayerBillingVersionDto? value)
    {
        if (value is not null) Populate(value.Configuration);
    }
    private AgencyActor Actor() => session.CurrentUser?.ToAgencyActor() ?? throw new UnauthorizedAccessException("Sign in to access payer configuration.");
    private string Value(string key) => Fields.Single(f => f.Key == key).Value.Trim();
    private static DateTime Date(string text, string field) =>
        DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new InvalidOperationException(field + ": enter a date as yyyy-MM-dd.");
    private PayerBillingConfiguration Configuration() => new()
    {
        ProfileKey = Value("ProfileKey"),
        BillingProviderName = Value("BillingProviderName"),
        BillingProviderNpi = Value("BillingProviderNpi"),
        BillingProviderTaxId = Value("BillingProviderTaxId"),
        BillingTaxonomy = Value("BillingTaxonomy"),
        BillingStreet = Value("BillingStreet"),
        BillingCity = Value("BillingCity"),
        BillingState = Value("BillingState"),
        BillingZip = Value("BillingZip"),
        RenderingProviderName = Value("RenderingProviderName"),
        RenderingProviderFirstName = Value("RenderingProviderFirstName"),
        RenderingProviderEntityType = Value("RenderingProviderEntityType"),
        RenderingProviderNpi = Value("RenderingProviderNpi"),
        RenderingTaxonomy = Value("RenderingTaxonomy"),
        FacilityName = Value("FacilityName"),
        FacilityStreet = Value("FacilityStreet"),
        FacilityCity = Value("FacilityCity"),
        FacilityState = Value("FacilityState"),
        FacilityZip = Value("FacilityZip"),
        FacilityNpi = Value("FacilityNpi"),
        FacilityIdQualifier = Value("FacilityIdQualifier"),
        FacilityId = Value("FacilityId"),
        PayerName = Value("PayerName"),
        PayerId = Value("PayerId"),
        ClaimFilingIndicator = Value("ClaimFilingIndicator"),
        SubmitterId = Value("SubmitterId"),
        ContactName = Value("ContactName"),
        ContactPhone = Value("ContactPhone"),
        ProcedureCode = Value("ProcedureCode"),
        RequirementsEvidenceReference = Value("RequirementsEvidenceReference"),
        Kind = Kind, EffectiveOn = Date(EffectiveOn, nameof(EffectiveOn)),
        ExpiresOn = string.IsNullOrWhiteSpace(ExpiresOn) ? null : Date(ExpiresOn, nameof(ExpiresOn)),
        Modifiers = Modifiers.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
        UnitRate = decimal.TryParse(UnitRate, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate) ? rate : 0,
        RequiresAuthorization = RequiresAuthorization, RequirementsReviewed = RequirementsReviewed
    };
    private void Populate(PayerBillingConfiguration c)
    {
        InvalidatePreview(); populating = true;
        try {
            Fields.Single(f => f.Key == "ProfileKey").Value = c.ProfileKey;
            Fields.Single(f => f.Key == "BillingProviderName").Value = c.BillingProviderName;
            Fields.Single(f => f.Key == "BillingProviderNpi").Value = c.BillingProviderNpi;
            Fields.Single(f => f.Key == "BillingProviderTaxId").Value = c.BillingProviderTaxId;
            Fields.Single(f => f.Key == "BillingTaxonomy").Value = c.BillingTaxonomy;
            Fields.Single(f => f.Key == "BillingStreet").Value = c.BillingStreet;
            Fields.Single(f => f.Key == "BillingCity").Value = c.BillingCity;
            Fields.Single(f => f.Key == "BillingState").Value = c.BillingState;
            Fields.Single(f => f.Key == "BillingZip").Value = c.BillingZip;
            Fields.Single(f => f.Key == "RenderingProviderName").Value = c.RenderingProviderName;
            Fields.Single(f => f.Key == "RenderingProviderFirstName").Value = c.RenderingProviderFirstName;
            Fields.Single(f => f.Key == "RenderingProviderEntityType").Value = c.RenderingProviderEntityType;
            Fields.Single(f => f.Key == "RenderingProviderNpi").Value = c.RenderingProviderNpi;
            Fields.Single(f => f.Key == "RenderingTaxonomy").Value = c.RenderingTaxonomy;
            Fields.Single(f => f.Key == "FacilityName").Value = c.FacilityName;
            Fields.Single(f => f.Key == "FacilityStreet").Value = c.FacilityStreet;
            Fields.Single(f => f.Key == "FacilityCity").Value = c.FacilityCity;
            Fields.Single(f => f.Key == "FacilityState").Value = c.FacilityState;
            Fields.Single(f => f.Key == "FacilityZip").Value = c.FacilityZip;
            Fields.Single(f => f.Key == "FacilityNpi").Value = c.FacilityNpi;
            Fields.Single(f => f.Key == "FacilityIdQualifier").Value = c.FacilityIdQualifier;
            Fields.Single(f => f.Key == "FacilityId").Value = c.FacilityId;
            Fields.Single(f => f.Key == "PayerName").Value = c.PayerName;
            Fields.Single(f => f.Key == "PayerId").Value = c.PayerId;
            Fields.Single(f => f.Key == "ClaimFilingIndicator").Value = c.ClaimFilingIndicator;
            Fields.Single(f => f.Key == "SubmitterId").Value = c.SubmitterId;
            Fields.Single(f => f.Key == "ContactName").Value = c.ContactName;
            Fields.Single(f => f.Key == "ContactPhone").Value = c.ContactPhone;
            Fields.Single(f => f.Key == "ProcedureCode").Value = c.ProcedureCode;
            Fields.Single(f => f.Key == "RequirementsEvidenceReference").Value = c.RequirementsEvidenceReference;
            Kind = c.Kind; EffectiveOn = c.EffectiveOn.ToString("yyyy-MM-dd"); ExpiresOn = c.ExpiresOn?.ToString("yyyy-MM-dd") ?? "";
            Modifiers = string.Join(",", c.Modifiers); UnitRate = c.UnitRate.ToString(CultureInfo.InvariantCulture);
            RequiresAuthorization = c.RequiresAuthorization;
            RequirementsReviewed = session.CurrentUser is { } user && PayerBillingRules.CanPublish(user.ToAgencyActor()) ? false : c.RequirementsReviewed;
        } finally { populating = false; }
    }
    [RelayCommand] private void NewProfile()
    {
        if (!CanEdit) return;
        SelectedVersion = null;
        Populate(new() { ProfileKey = "mainecare-section13", EffectiveOn = new(2026, 4, 28), PayerName = "MAINE MEDICAID", PayerId = "MEMCD" });
        StatusMessage = "Enter agency-confirmed identifiers, qualifier, taxonomy, coding, rates and evidence; review before publishing.";
    }
    public void ClearForAccountSwitch()
    {
        requests.Invalidate(); operationOwner++; populating = true;
        Versions.Clear(); Notes.Clear(); Errors.Clear(); SelectedVersion = null; SelectedNote = null;
        foreach (var field in Fields) field.Value = "";
        AuthorizationReference = AuthorizationEvidenceReference = AuthorizationEffectiveOn = AuthorizationExpiresOn = AuthorizationDecisionDate = "";
        AuthorizationCoverageReviewed = RequirementsReviewed = false; previewPreparation = null; pendingPublication = null; PreviewReady = false; IsBusy = false;
        populating = false; OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(IsConfigurationReadOnly)); OnPropertyChanged(nameof(CanPrepare)); OnPropertyChanged(nameof(CanLoad));
    }
    [RelayCommand] public async Task LoadAsync()
    {
        if (IsBusy) return;
        var account = session.CurrentUser; var identity = requests.Begin(); var operation = ++operationOwner; IsBusy = true;
        try {
            var actor = Actor();
            var versions = await service.GetPayerConfigurationsAsync(actor);
            var notes = PayerBillingRules.CanPrepare(actor) ? await service.GetApprovedUnbilledNotesAsync(actor) : [];
            if (!requests.IsCurrent(identity) || !ReferenceEquals(session.CurrentUser, account)) return;
            populating = true; Versions.Clear(); Notes.Clear();
            foreach (var v in versions) Versions.Add(v);
            foreach (var n in notes.Where(n => n.EventDate is not null)) Notes.Add(new(n.Id, n.PersonId, n.EventDate!.Value.Date));
            populating = false; StatusMessage = "Select a version to copy its fields, or choose New profile. New versions need a later effective date. Preview validates the persisted note.";
        } catch (Exception e) { if (requests.IsCurrent(identity)) StatusMessage = SafeError(e); }
        finally { populating = false; if (operation == operationOwner) IsBusy = false; }
    }
    [RelayCommand] private async Task PublishAsync()
    {
        if (!CanEdit) return;
        var account = session.CurrentUser; var identity = requests.Begin(); var operation = ++operationOwner; IsBusy = true; Errors.Clear();
        try {
            var c = Configuration(); var validation = PayerBillingRules.Validate(c);
            if (validation.Count != 0) { foreach(var e in validation) Errors.Add(e.Field + ": " + e.Message); return; }
            var expected = Versions.Where(v => v.Configuration.ProfileKey == c.ProfileKey).Select(v => v.Revision).DefaultIfEmpty().Max();
            var request = new PublishPayerBillingRequest(Guid.NewGuid(), expected, c);
            if (pendingPublication is { } previous && previous.ExpectedRevision == expected &&
                JsonSerializer.Serialize(previous.Configuration) == JsonSerializer.Serialize(c)) request = previous;
            pendingPublication = request;
            var result = await service.PublishPayerConfigurationAsync(Actor(), request);
            if (!requests.IsCurrent(identity) || !ReferenceEquals(session.CurrentUser, account)) return;
            Versions.Insert(0, result); pendingPublication = null;
            StatusMessage = $"Published {c.ProfileKey} revision {result.Revision} ({result.VersionId}). Prior claims retain their frozen version.";
        } catch (Exception e) { if (requests.IsCurrent(identity)) StatusMessage = SafeError(e); }
        finally { if (operation == operationOwner) IsBusy = false; }
    }
    private PayerClaimPreparation Preparation()
    {
        var note = SelectedNote ?? throw new InvalidOperationException("Select an approved unclaimed note.");
        var profileKey = SelectedVersion?.Configuration.ProfileKey ?? Value("ProfileKey");
        var v = PayerBillingRules.Resolve(Versions, Actor().AgencyId, profileKey, note.ServiceDate)
            ?? throw new InvalidOperationException("ProfileKey: no effective configuration covers this service date.");
        PayerAuthorizationReference? auth = null;
        if (v.Configuration.RequiresAuthorization || !string.IsNullOrWhiteSpace(AuthorizationReference))
            auth = new(note.PersonId, profileKey, v.Configuration.ProcedureCode, v.Configuration.RenderingProviderNpi,
                AuthorizationReference.Trim(), Date(AuthorizationEffectiveOn, "Authorization effective date"),
                Date(AuthorizationExpiresOn, "Authorization expiration date"), AuthorizationEvidenceReference.Trim(), AuthorizationCoverageReviewed)
                { AuthorizedOn = Date(AuthorizationDecisionDate, "Authorization decision date"), Modifiers = v.Configuration.Modifiers };
        return new(profileKey, v.VersionId, auth);
    }
    [RelayCommand] private async Task PreviewAsync()
    {
        if (!CanPrepare) return;
        var account = session.CurrentUser; var identity = requests.Begin(); var operation = ++operationOwner; IsBusy = true; Errors.Clear(); PreviewReady = false;
        try {
            var preparation = Preparation(); var noteId = SelectedNote!.NoteId;
            var preview = await service.PreviewPayerClaimAsync(Actor(), noteId, preparation);
            if (!requests.IsCurrent(identity) || !ReferenceEquals(session.CurrentUser, account)) return;
            foreach(var e in preview.Errors) Errors.Add(e.Field + ": " + e.Message);
            previewPreparation = preview.IsReady ? preparation : null; previewNoteId = noteId; PreviewReady = preview.IsReady;
            StatusMessage = $"Version {preview.ConfigurationVersionId}: {preview.ProcedureCode} {string.Join(",", preview.Modifiers)}, {preview.Units} units, {preview.Charge:C}, facility {preview.FacilityId}. " +
                (preview.IsReady ? "Ready for claim creation; live payer certification remains separate." : "Resolve the listed fields and preview again.");
        } catch (Exception e) { if (requests.IsCurrent(identity)) StatusMessage = SafeError(e); }
        finally { if (operation == operationOwner) IsBusy = false; }
    }
    [RelayCommand] private async Task CreateClaimAsync()
    {
        if (!CanPrepare || !PreviewReady || previewPreparation is null) return;
        var account = session.CurrentUser; var identity = requests.Begin(); var operation = ++operationOwner; var preparation = previewPreparation; var noteId = previewNoteId; IsBusy = true;
        try {
            var line = await service.CreatePreparedClaimLineAsync(Actor(), noteId, preparation);
            if (!requests.IsCurrent(identity) || !ReferenceEquals(session.CurrentUser, account)) return;
            PreviewReady = false; previewPreparation = null;
            populating = true;
            try { Notes.Remove(Notes.Single(n => n.NoteId == noteId)); }
            finally { populating = false; }
            StatusMessage = $"Created claim line {line.Id} with frozen payer inputs. Refresh the billing overview to review the draft period.";
        } catch (Exception e) { if (requests.IsCurrent(identity)) { PreviewReady = false; StatusMessage = SafeError(e); } }
        finally { if (operation == operationOwner) IsBusy = false; }
    }
    private static string SafeError(Exception e) => e is InvalidOperationException or UnauthorizedAccessException or CloudApiException ? e.Message : "The operation could not be confirmed. Refresh or retry; entered values are retained.";
}
