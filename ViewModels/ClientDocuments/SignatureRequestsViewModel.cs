using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Services;

namespace Sati.ViewModels.ClientDocuments;

/// <summary>Staff coordination only. Signer consent and signing remain in the separate portal.</summary>
public partial class SignatureRequestsViewModel(ISignatureService service, ISessionService session) : ObservableObject
{
    private readonly LatestRequestTracker loads = new();
    private int personId;
    private bool active;
    private bool applyingServerResult;
    private int? loadedUserId;
    private Guid createKey = Guid.NewGuid();
    private Guid replaceKey = Guid.NewGuid();
    private Guid externalKey = Guid.NewGuid();
    private readonly HashSet<int> frozenArtifactIds = [];
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isEnabled;
    [ObservableProperty] private bool isExternalUploadEnabled;
    [ObservableProperty] private string explanation = "Electronic signing has not been loaded.";
    [ObservableProperty] private string message = "";
    [ObservableProperty] private DocumentArtifactDto? selectedArtifact;
    [ObservableProperty] private SignatureSignerDto? selectedSigner;
    [ObservableProperty] private SignatureRequestDto? selectedRequest;
    [ObservableProperty] private bool completenessReviewed;
    [ObservableProperty] private bool identityConfirmed;
    [ObservableProperty] private bool emailConfirmed;
    [ObservableProperty] private string authorityEvidence = "";
    [ObservableProperty] private string reason = "";
    [ObservableProperty] private int expiryHours = SignatureRules.DefaultExpiryHours;
    [ObservableProperty] private ExternalSignatureEvidenceDto? selectedExternalEvidence;
    [ObservableProperty] private ExternalSignatureMethod externalMethod = ExternalSignatureMethod.WetInk;
    [ObservableProperty] private DateTime? externallySignedOn = DateTime.Today;
    [ObservableProperty] private string externalSignerName = "";
    [ObservableProperty] private SignerCapacity externalSignerCapacity = SignerCapacity.Consumer;
    [ObservableProperty] private bool externalDocumentReviewed;
    [ObservableProperty] private bool externalIdentityAndAuthorityVerified;
    [ObservableProperty] private bool externalSignaturesAndDatesComplete;
    [ObservableProperty] private string externalVerificationNote = "";
    public IReadOnlyList<SignatureMeaningEntry> Catalog => SignatureMeaningCatalog.All;
    public ObservableCollection<DocumentArtifactDto> Artifacts { get; } = [];
    public ObservableCollection<SignatureSignerDto> Signers { get; } = [];
    public ObservableCollection<SignatureRequestDto> Requests { get; } = [];
    public ObservableCollection<ExternalSignatureEvidenceDto> ExternalEvidence { get; } = [];
    public IReadOnlyList<ExternalSignatureMethod> ExternalMethods { get; } =
        Enum.GetValues<ExternalSignatureMethod>();
    public IReadOnlyList<SignerCapacity> ExternalSignerCapacities { get; } =
        [SignerCapacity.Consumer, SignerCapacity.Guardian];
    public string ScopeNotice => SignatureRules.ScopeNotice;
    public string ConsumerContext => personId > 0 ? $"Consumer record {personId}. Confirm this is the intended consumer before preparing a request." : "Select a consumer to review signing requests.";
    public string PinExplanation => SigningPinRules.Explanation;
    public string DocumentExplanation => SelectedArtifact is { } artifact && Enum.TryParse<AnnualDocumentKind>(artifact.Kind, out var kind)
        ? SignatureMeaningCatalog.Find(kind)?.Explanation ?? "This document cannot be signed." : "Choose a current document record.";
    public string ElectronicReceiptStatus => Requests.Any(x => x.DocumentName == "Notice of Privacy Practices" && x.State == "Signed" &&
        Artifacts.Any(a => a.Id == x.DocumentArtifactId))
        ? "An electronic notice receipt is recorded with the signer's own name and capacity in the request history. Staff receipt records remain separate."
        : "No completed electronic receipt is shown for the current privacy notice.";
    private bool CanPrepareInternalRequest => active && IsEnabled && !IsBusy && IsCurrentAccount && SelectedArtifact is { Origin: "GeneratedInSati", BlankFields.Count: 0 } a &&
        Enum.TryParse<AnnualDocumentKind>(a.Kind, out var kind) && SelectedSigner is { } s && SignatureMeaningCatalog.CanRequest(kind, s.Capacity);
    public bool CanCreate => CanPrepareInternalRequest && SelectedArtifact is { } artifact &&
        frozenArtifactIds.Contains(artifact.Id);
    public bool CanManage => active && IsEnabled && !IsBusy && IsCurrentAccount && SelectedRequest is not null;
    public bool CanFreeze => CanPrepareInternalRequest && CompletenessReviewed;
    public bool CanReplace => CanManage && SelectedRequest is { } r && (r.State is "Issued" or "Viewed" or "Expired" or "Revoked") &&
        SelectedSigner is { } s && s.Capacity.ToString() == r.SignerCapacity && s.ContactId == r.SignerContactId;
    public bool CanRevoke => CanManage && SelectedRequest is { } r && SignatureRules.IsOpen(r.State);
    public bool CanWithdrawAuthorization => CanManage && SelectedRequest is { State: "Signed", Meaning: "Authorization", AuthorizationRevokedAtUtc: null };
    public bool CanDownloadSigned => CanManage && SelectedRequest?.HasSignedPackage == true;
    public bool CanRecordExternal => active && IsExternalUploadEnabled && !IsBusy && IsCurrentAccount &&
        SelectedArtifact is { Origin: "GeneratedInSati", BlankFields.Count: 0, ReleaseObligationRecordId: not null } artifact &&
        artifact.Kind is nameof(AnnualDocumentKind.ReleaseAgency) or nameof(AnnualDocumentKind.ReleaseMedical) or nameof(AnnualDocumentKind.ReleaseDhhs) &&
        ExternallySignedOn is not null && !string.IsNullOrWhiteSpace(ExternalSignerName) &&
        ExternalDocumentReviewed && ExternalIdentityAndAuthorityVerified &&
        ExternalSignaturesAndDatesComplete;
    public bool CanDownloadExternal => active && !IsBusy && IsCurrentAccount && SelectedExternalEvidence is not null;
    private bool IsCurrentAccount => loadedUserId is not null && loadedUserId == session.CurrentUser?.Id;
    public Func<Task<byte[]?>>? ChooseFreezePdfAsync { get; set; }
    public Func<Task<byte[]?>>? ChooseExternalSignedPdfAsync { get; set; }
    public Func<Task>? CompletionChangedAsync { get; set; }
    public event Action<AgencyReleaseResult>? FileReady;
    public event Action? ClearSensitiveInputs;
    partial void OnIsBusyChanged(bool value) => NotifyState();
    partial void OnIsEnabledChanged(bool value) => NotifyState();
    partial void OnIsExternalUploadEnabledChanged(bool value) => NotifyState();
    partial void OnCompletenessReviewedChanged(bool value) => NotifyState();
    private void InvalidateSelection() { if (!applyingServerResult) { loads.Invalidate(); IsBusy = false; } }
    partial void OnSelectedArtifactChanged(DocumentArtifactDto? value) { InvalidateSelection(); ResetAffirmations(); ResetExternalAttestation(); createKey = Guid.NewGuid(); externalKey = Guid.NewGuid(); OnPropertyChanged(nameof(DocumentExplanation)); NotifyState(); }
    partial void OnSelectedSignerChanged(SignatureSignerDto? value) { InvalidateSelection(); ResetAffirmations(); createKey = Guid.NewGuid(); NotifyState(); }
    partial void OnSelectedRequestChanged(SignatureRequestDto? value) { InvalidateSelection(); replaceKey = Guid.NewGuid(); Reason = ""; SelectedSigner = null; ResetAffirmations(); NotifyState(); }
    partial void OnSelectedExternalEvidenceChanged(ExternalSignatureEvidenceDto? value) => NotifyState();
    partial void OnExternallySignedOnChanged(DateTime? value) => NotifyState();
    partial void OnExternalSignerNameChanged(string value) => NotifyState();
    partial void OnExternalDocumentReviewedChanged(bool value) => NotifyState();
    partial void OnExternalIdentityAndAuthorityVerifiedChanged(bool value) => NotifyState();
    partial void OnExternalSignaturesAndDatesCompleteChanged(bool value) => NotifyState();
    private void ResetAffirmations() { CompletenessReviewed = false; IdentityConfirmed = false; EmailConfirmed = false; AuthorityEvidence = ""; ClearSensitiveInputs?.Invoke(); }
    private void ResetExternalAttestation()
    {
        ExternallySignedOn = DateTime.Today;
        ExternalSignerName = "";
        ExternalSignerCapacity = SignerCapacity.Consumer;
        ExternalDocumentReviewed = false;
        ExternalIdentityAndAuthorityVerified = false;
        ExternalSignaturesAndDatesComplete = false;
        ExternalVerificationNote = "";
    }
    private void NotifyState()
    {
        foreach (var name in new[] { nameof(CanCreate), nameof(CanFreeze), nameof(CanManage), nameof(CanReplace), nameof(CanRevoke), nameof(CanWithdrawAuthorization), nameof(CanDownloadSigned), nameof(CanRecordExternal), nameof(CanDownloadExternal), nameof(ElectronicReceiptStatus) }) OnPropertyChanged(name);
    }
    public void SetContext(int id, IReadOnlyList<DocumentArtifactDto> artifacts)
    {
        loads.Invalidate(); personId = id; loadedUserId = session.CurrentUser?.Id; IsBusy = false;
        OnPropertyChanged(nameof(ConsumerContext));
        SelectedArtifact = null; SelectedSigner = null; SelectedRequest = null; SelectedExternalEvidence = null;
        Requests.Clear(); Signers.Clear(); ExternalEvidence.Clear(); Artifacts.Clear(); frozenArtifactIds.Clear();
        foreach (var artifact in artifacts) Artifacts.Add(artifact);
        IsEnabled = false; Message = ""; ResetAffirmations(); NotifyState();
        if (active && personId > 0) _ = RefreshAsync();
    }
    public void SetActive(bool value)
    {
        active = value; loads.Invalidate(); IsBusy = false; ClearSensitiveInputs?.Invoke(); ResetAffirmations();
        if (!value) { Requests.Clear(); Signers.Clear(); ExternalEvidence.Clear(); SelectedSigner = null; SelectedRequest = null; SelectedExternalEvidence = null; Message = ""; }
        else if (personId > 0) _ = RefreshAsync();
        NotifyState();
    }
    private bool Current(int ticket, int id, int? userId) => active && loads.IsCurrent(ticket) && id == personId &&
        userId is not null && userId == session.CurrentUser?.Id && userId == loadedUserId;
    [RelayCommand] public async Task RefreshAsync()
    {
        if (!active || personId <= 0 || IsBusy || !IsCurrentAccount) return;
        var ticket = loads.Begin(); var id = personId; var userId = loadedUserId; IsBusy = true; Message = "";
        try
        {
            var external = await service.GetExternalSignaturesAsync(id);
            var availability = await service.GetAvailabilityAsync();
            if (!Current(ticket, id, userId)) return;
            ExternalEvidence.Clear(); foreach (var evidence in external) ExternalEvidence.Add(evidence);
            IsEnabled = availability.Enabled; Explanation = availability.Explanation;
            IsExternalUploadEnabled = availability.ExternalUploadEnabled;
            if (!availability.Enabled) { NotifyState(); return; }
            var signers = await service.GetSignersAsync(id);
            var requests = await service.GetRequestsAsync(id);
            if (!Current(ticket, id, userId)) return;
            Signers.Clear(); foreach (var signer in signers) Signers.Add(signer);
            Requests.Clear(); foreach (var request in requests) Requests.Add(request);
            applyingServerResult = true;
            try { SelectedRequest = null; SelectedSigner = null; }
            finally { applyingServerResult = false; }
            if (requests.Any(request => request.State == "Signed") && CompletionChangedAsync is not null)
                await CompletionChangedAsync();
            NotifyState();
        }
        catch (Exception) { if (Current(ticket, id, userId)) { IsEnabled = false; Message = "Signing requests could not be loaded. Check your connection and reload."; } }
        finally { if (Current(ticket, id, userId)) IsBusy = false; }
    }

    [RelayCommand]
    private async Task RecordExternalAsync()
    {
        if (!CanRecordExternal || ChooseExternalSignedPdfAsync is null ||
            SelectedArtifact is not { } artifact || ExternallySignedOn is not DateTime signedOn)
            return;
        var ticket = loads.Begin(); var id = personId; var userId = loadedUserId; IsBusy = true;
        byte[]? pdf = null;
        try
        {
            pdf = await ChooseExternalSignedPdfAsync();
            if (pdf is null || !Current(ticket, id, userId)) return;
            var error = ExternalSignatureRules.ValidatePdf(pdf);
            if (error is not null) { Message = error; return; }
            var evidence = await service.RecordExternalSignatureAsync(id, new(
                externalKey, id, artifact.Id, pdf, ExternalMethod, signedOn,
                ExternalSignerName, ExternalSignerCapacity, ExternalDocumentReviewed,
                ExternalIdentityAndAuthorityVerified, ExternalSignaturesAndDatesComplete,
                ExternalVerificationNote));
            if (!Current(ticket, id, userId)) return;
            ExternalEvidence.Insert(0, evidence);
            SelectedExternalEvidence = evidence;
            externalKey = Guid.NewGuid();
            Message = "The externally signed PDF is retained and marked Externally signed — staff verified. The exact recipient obligation was updated.";
            ResetExternalAttestation();
            if (CompletionChangedAsync is not null)
            {
                try { await CompletionChangedAsync(); }
                catch { Message += " Reload the release list to see its updated completion status."; }
            }
            NotifyState();
        }
        catch (Exception ex)
        {
            if (Current(ticket, id, userId))
                Message = $"The externally signed PDF was not recorded. {ex.Message}";
        }
        finally
        {
            if (pdf is not null) Array.Clear(pdf);
            if (Current(ticket, id, userId)) IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadExternalAsync()
    {
        if (!CanDownloadExternal || SelectedExternalEvidence is not { } evidence) return;
        var ticket = loads.Begin(); var id = personId; var userId = loadedUserId; IsBusy = true;
        try
        {
            var file = await service.GetExternalSignedAsync(evidence.Id);
            if (Current(ticket, id, userId)) FileReady?.Invoke(file);
            else Array.Clear(file.Pdf);
        }
        catch (Exception) { if (Current(ticket, id, userId)) Message = "The retained external signed copy could not be downloaded."; }
        finally { if (Current(ticket, id, userId)) IsBusy = false; }
    }
    [RelayCommand] public async Task FreezeAsync()
    {
        if (!CanFreeze || ChooseFreezePdfAsync is null || SelectedArtifact is not { } artifact) return;
        var ticket = loads.Begin(); var id = personId; var userId = loadedUserId; IsBusy = true;
        byte[]? pdf = null;
        try
        {
            pdf = await ChooseFreezePdfAsync();
            if (pdf is null || !Current(ticket, id, userId)) return;
            if (pdf.Length is <= 0 or > SignatureRules.MaximumPdfBytes) { Message = "Choose a PDF no larger than 15 MB."; return; }
            await service.FreezeAsync(id, artifact.Id, new(Guid.NewGuid(), pdf, true));
            if (Current(ticket, id, userId))
            {
                frozenArtifactIds.Add(artifact.Id);
                Message = "The exact saved document is retained for signing. Verify the signer, email, and access code, then send the secure link.";
                NotifyState();
            }
        }
        catch (Exception) { if (Current(ticket, id, userId)) Message = "The document was not frozen. Choose the exact complete PDF saved when this current record was generated."; }
        finally { if (pdf is not null) Array.Clear(pdf); if (Current(ticket, id, userId)) IsBusy = false; }
    }
    // PIN values exist only for this attempt, never as bindable or persisted view-model fields.
    public async Task SubmitAsync(string pin, string confirmPin, bool replace)
    {
        try
        {
            if (!(replace ? CanReplace : CanCreate)) return;
            if (!IdentityConfirmed || !EmailConfirmed || !SigningPinRules.IsValid(pin) || pin != confirmPin)
            { Message = "Confirm identity and the preferred email, then enter the same new valid code twice."; return; }
            var artifact = SelectedArtifact; var signer = SelectedSigner; var selected = SelectedRequest;
            await RunMutation(async () => replace
                ? await service.ReplaceAsync(selected!.Id, new(replaceKey, selected.Revision, pin, confirmPin, true, true, Reason, signer!.Name, signer.Email))
                : await service.CreateAsync(new(createKey, personId, artifact!.Id, signer!.Capacity, signer.ContactId, pin, confirmPin, true, true, AuthorityEvidence, ExpiryHours, signer.Name, signer.Email)));
        }
        finally { ClearSensitiveInputs?.Invoke(); }
    }
    private async Task RunMutation(Func<Task<SignatureRequestDto>> operation)
    {
        var ticket = loads.Begin(); var id = personId; var userId = loadedUserId; IsBusy = true; Message = "";
        try
        {
            var result = await operation();
            if (!Current(ticket, id, userId)) return;
            var prior = Requests.FirstOrDefault(x => x.Id == result.Id); if (prior is not null) Requests.Remove(prior);
            Requests.Insert(0, result);
            applyingServerResult = true;
            try { SelectedRequest = result; }
            finally { applyingServerResult = false; }
            createKey = Guid.NewGuid();
            Message = "The secure-link request was created. Reload to review email submission, portal access, and signature status."; ResetAffirmations(); NotifyState();
        }
        catch (Exception) { if (Current(ticket, id, userId)) Message = "The request could not be updated. Reload its current status, check the required fields, and try again."; }
        finally { if (Current(ticket, id, userId)) IsBusy = false; }
    }
    [RelayCommand] private Task RevokeAsync() => CanRevoke && SelectedRequest is { } r ? RunMutation(() => service.RevokeAsync(r.Id, new(r.Revision, Reason))) : Task.CompletedTask;
    [RelayCommand] private Task WithdrawAuthorizationAsync() => CanWithdrawAuthorization && SelectedRequest is { } r ? RunMutation(() => service.WithdrawAuthorizationAsync(r.Id, new(r.Revision, Reason))) : Task.CompletedTask;
    [RelayCommand] private Task DownloadOriginalAsync() => DownloadAsync(false);
    [RelayCommand] private Task DownloadSignedAsync() => DownloadAsync(true);
    private async Task DownloadAsync(bool signed)
    {
        if (!CanManage || SelectedRequest is not { } r || (signed && !r.HasSignedPackage)) return;
        var ticket = loads.Begin(); var id = personId; var userId = loadedUserId; IsBusy = true;
        try
        {
            var file = signed ? await service.GetSignedAsync(r.Id) : await service.GetOriginalAsync(r.Id);
            if (Current(ticket, id, userId)) FileReady?.Invoke(file);
            else Array.Clear(file.Pdf);
        }
        catch (Exception) { if (Current(ticket, id, userId)) Message = "The retained document could not be downloaded. Reload and try again."; }
        finally { if (Current(ticket, id, userId)) IsBusy = false; }
    }
}
