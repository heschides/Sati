using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;

namespace Sati.ViewModels.ClientDocuments;

public partial class HousingSupportFundsViewModel(
    IHousingSupportFundsService service,
    ISessionService session) : ObservableObject
{
    private Person? person;
    private int personVersion;

    public IReadOnlyList<string> HousingTypes => HousingSupportFundsRules.HousingTypes;
    public IReadOnlyList<HousingNullableChoice> YesNoChoices { get; } =
    [
        new("Not answered", null), new("No", false), new("Yes", true)
    ];

    [ObservableProperty] private string personName = "Select a consumer";
    [ObservableProperty] private string waiverDisplay = "Not recorded";
    [ObservableProperty] private string sharedLivingDisplay = "Not recorded";
    [ObservableProperty] private bool hasGuardian;
    [ObservableProperty] private string guardianName = "";
    [ObservableProperty] private string consumerTelephone = "";
    [ObservableProperty] private string consumerEmail = "";
    [ObservableProperty] private string consumerAddress = "";
    [ObservableProperty] private string? housingType;
    [ObservableProperty] private string landlordName = "";
    [ObservableProperty] private string landlordAddress = "";
    [ObservableProperty] private string landlordTelephone = "";
    [ObservableProperty] private string landlordEmail = "";
    [ObservableProperty] private decimal? monthlyHousingAmount;
    [ObservableProperty] private decimal? amountRequested;
    [ObservableProperty] private bool? receivesSubsidy;
    [ObservableProperty] private string subsidyType = "";
    [ObservableProperty] private string guardianAddress = "";
    [ObservableProperty] private string guardianTelephone = "";
    [ObservableProperty] private string guardianEmail = "";
    [ObservableProperty] private string representativePayeeName = "";
    [ObservableProperty] private string representativePayeeAddress = "";
    [ObservableProperty] private string caseManagerName = "";
    [ObservableProperty] private string providerName = "";
    [ObservableProperty] private string providerAddress = "";
    [ObservableProperty] private string providerTelephone = "";
    [ObservableProperty] private string providerEmail = "";
    [ObservableProperty] private string additionalDetails = "";
    [ObservableProperty] private bool supportingDocumentReady;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private string reviewItemsMessage = "";

    public bool HasPerson => person is not null;
    public bool CanGenerate => HasPerson && !IsBusy;
    public bool ShowLandlord => HousingType == "Rental";
    public bool ShowSubsidyType => ReceivesSubsidy == true;
    public bool HasEligibilityConflict => person?.HasSharedLiving == true || ReceivesSubsidy == true;
    public string EligibilityMessage => person?.HasSharedLiving == true && ReceivesSubsidy == true
        ? "The profile records Shared Living and the form says the consumer receives a subsidy. The state application says Housing Support Funds cannot support either situation."
        : person?.HasSharedLiving == true
            ? "The profile records Shared Living. The state application says these funds cannot reimburse or support Shared Living."
            : ReceivesSubsidy == true
                ? "The form says the consumer receives a subsidy. The state application says these funds cannot support someone currently receiving rental assistance."
                : "";
    public string SourceRevision => HousingSupportFundsRules.SourceRevision;

    public event EventHandler<HousingSupportFundsPdfReadyEventArgs>? PdfReady;
    public event EventHandler<HousingSupportFundsProblemEventArgs>? Problem;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    partial void OnHousingTypeChanged(string? value) => OnPropertyChanged(nameof(ShowLandlord));
    partial void OnReceivesSubsidyChanged(bool? value)
    {
        OnPropertyChanged(nameof(ShowSubsidyType));
        OnPropertyChanged(nameof(HasEligibilityConflict));
        OnPropertyChanged(nameof(EligibilityMessage));
    }

    public void SetPerson(Person? value)
    {
        personVersion++;
        person = value;
        PersonName = value?.FullName ?? "Select a consumer";
        ApplyProfileDefaults();
        OnPropertyChanged(nameof(HasPerson));
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(HasEligibilityConflict));
        OnPropertyChanged(nameof(EligibilityMessage));
        GenerateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (person is null) return;
        var request = BuildRequest();
        var validation = HousingSupportFundsRules.Validate(request);
        if (validation.Count > 0)
        {
            StatusMessage = string.Join(Environment.NewLine,
                validation.Values.SelectMany(values => values).Select(message => $"- {message}"));
            return;
        }

        var id = person.Id;
        var version = personVersion;
        IsBusy = true;
        StatusMessage = "Preparing the official three-page Housing Support Funds application...";
        ReviewItemsMessage = "";
        try
        {
            var result = await service.GenerateAsync(id, request);
            if (version != personVersion || person?.Id != id) return;
            ReviewItemsMessage = result.ReviewItems.Count == 1 &&
                                 result.ReviewItems[0] == "Consumer or guardian signature and date"
                ? "Only the required consumer or guardian signature and date remain."
                : $"Review before submission: {string.Join(", ", result.ReviewItems)}.";
            StatusMessage = "The editable draft is ready to save. Review pages 1-2, attach the required proof, and obtain the required signature. Page 3 remains for DHHS staff only.";
            PdfReady?.Invoke(this, new HousingSupportFundsPdfReadyEventArgs(result.Pdf, result.FileName));
        }
        catch (Exception exception)
        {
            if (version != personVersion || person?.Id != id) return;
            StatusMessage = "The Housing Support Funds application could not be generated.";
            Problem?.Invoke(this, new HousingSupportFundsProblemEventArgs(
                "Housing Support Funds Application Not Generated",
                $"The application could not be generated.\n\n{exception.Message}"));
        }
        finally
        {
            if (version == personVersion) IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset()
    {
        ApplyProfileDefaults();
        StatusMessage = "The application was reset to the selected consumer's current profile information.";
    }

    private bool CanReset() => !IsBusy;

    internal HousingSupportFundsRequest BuildRequest() => new(
        ConsumerTelephone, ConsumerEmail, ConsumerAddress, HousingType,
        LandlordName, LandlordAddress, LandlordTelephone, LandlordEmail,
        MonthlyHousingAmount, AmountRequested, ReceivesSubsidy, SubsidyType,
        GuardianAddress, GuardianTelephone, GuardianEmail,
        RepresentativePayeeName, RepresentativePayeeAddress,
        AdditionalDetails, SupportingDocumentReady);

    private void ApplyProfileDefaults()
    {
        var value = person;
        var actor = session.CurrentUser;
        WaiverDisplay = HousingSupportFundsRules.NormalizeWaiver(value?.Waiver.ToString()) ?? "Not recorded";
        SharedLivingDisplay = value is null ? "Not recorded" : value.HasSharedLiving ? "Yes" : "No";
        HasGuardian = value?.HasGuardian == true;
        GuardianName = HasGuardian ? value?.GuardianName ?? "" : "";
        ConsumerTelephone = value?.PhoneNumber ?? "";
        ConsumerEmail = value?.Email ?? "";
        ConsumerAddress = FullConsumerAddress(value);
        HousingType = null;
        LandlordName = LandlordAddress = LandlordTelephone = LandlordEmail = "";
        MonthlyHousingAmount = AmountRequested = null;
        ReceivesSubsidy = null;
        SubsidyType = "";
        GuardianAddress = GuardianTelephone = GuardianEmail = "";
        CaseManagerName = value?.User?.DisplayName ?? actor?.DisplayName ?? "";
        ProviderName = value?.Agency?.Name ?? actor?.Agency?.Name ?? "";
        ProviderAddress = HousingSupportFundsService.AddressOf(value?.Agency ?? actor?.Agency) ?? "";
        ProviderTelephone = value?.User?.Phone ?? actor?.Phone ?? "";
        ProviderEmail = value?.User?.Email ?? actor?.Email ?? "";
        RepresentativePayeeName = value?.CaseManagerIsRepPayee == true ? CaseManagerName : "";
        RepresentativePayeeAddress = value?.CaseManagerIsRepPayee == true ? ProviderAddress : "";
        AdditionalDetails = "";
        SupportingDocumentReady = false;
        ReviewItemsMessage = StatusMessage = "";
    }

    private static string FullConsumerAddress(Person? value)
    {
        if (value is null) return "";
        if (!string.IsNullOrWhiteSpace(value.Address)) return value.Address.Trim();
        var locality = string.Join(" ", new[]
        {
            string.IsNullOrWhiteSpace(value.BillingCity) ? null : $"{value.BillingCity.Trim()},",
            value.BillingState?.Trim(), value.BillingZip?.Trim()
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.Join(" ", new[] { value.BillingStreet?.Trim(), locality }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}

public sealed record HousingNullableChoice(string DisplayName, bool? Value);

public sealed class HousingSupportFundsPdfReadyEventArgs(byte[] content, string suggestedFileName) : EventArgs
{
    public byte[] Content { get; } = content;
    public string SuggestedFileName { get; } = suggestedFileName;
}

public sealed class HousingSupportFundsProblemEventArgs(string title, string message) : EventArgs
{
    public string Title { get; } = title;
    public string Message { get; } = message;
}
