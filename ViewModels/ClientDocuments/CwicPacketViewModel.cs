using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;

namespace Sati.ViewModels.ClientDocuments;

public partial class CwicPacketViewModel(ICwicPacketService service) : ObservableObject
{
    private Person? person;
    private int personVersion;

    public IReadOnlyList<string> MaritalStatuses => CwicPacketRules.MaritalStatuses;
    public IReadOnlyList<string> JobSatisfactionChoices => CwicPacketRules.JobSatisfactionChoices;
    public IReadOnlyList<string> Locations => CwicPacketRules.MaineLocations;
    public IReadOnlyList<string> VrStatuses => CwicPacketRules.VrStatuses;
    public IReadOnlyList<string> ReturnToWorkChoices => CwicPacketRules.ReturnToWorkChoices;
    public IReadOnlyList<string> VrDivisions => CwicPacketRules.VrDivisions;
    public IReadOnlyList<CwicNullableChoice> YesNoChoices { get; } =
    [
        new("Not answered", null), new("No", false), new("Yes", true)
    ];
    public IReadOnlyList<CwicChoiceOption> MeetingMethods { get; } = Options(CwicPacketRules.MeetingMethods);
    public IReadOnlyList<CwicChoiceOption> EmploymentSituations { get; } = Options(CwicPacketRules.EmploymentSituations);
    public IReadOnlyList<CwicChoiceOption> Benefits { get; } = Options(CwicPacketRules.BenefitChoices);
    public IReadOnlyList<CwicChoiceOption> Accommodations { get; } = Options(CwicPacketRules.AccommodationChoices);

    [ObservableProperty] private string personName = "Select a consumer";
    [ObservableProperty] private string birthDateDisplay = "";
    [ObservableProperty] private string ageDisplay = "";
    [ObservableProperty] private string mailingAddress = "";
    [ObservableProperty] private string city = "";
    [ObservableProperty] private string state = "ME";
    [ObservableProperty] private string zip = "";
    [ObservableProperty] private string county = "";
    [ObservableProperty] private string homePhone = "";
    [ObservableProperty] private string cellPhone = "";
    [ObservableProperty] private string email = "";
    [ObservableProperty] private string? maritalStatus;
    [ObservableProperty] private string gender = "";
    [ObservableProperty] private bool? spouseReceivesDisabilityBenefits;
    [ObservableProperty] private string referringOrganization = "";
    [ObservableProperty] private bool scheduleWithConsumer = true;
    [ObservableProperty] private string schedulingContactName = "";
    [ObservableProperty] private string schedulingContactRelationship = "";
    [ObservableProperty] private string schedulingContactPhone = "";
    [ObservableProperty] private bool? hasRepresentativePayee;
    [ObservableProperty] private string representativePayeeName = "";
    [ObservableProperty] private string representativePayeePhone = "";
    [ObservableProperty] private bool? hasLegalGuardian;
    [ObservableProperty] private string guardianName = "";
    [ObservableProperty] private string guardianPhone = "";
    [ObservableProperty] private string guardianCommunicationPermission = "";
    [ObservableProperty] private string selfEmploymentMonthlyProfit = "";
    [ObservableProperty] private string selfEmploymentHoursPerMonth = "";
    [ObservableProperty] private string workingHoursPerWeek = "";
    [ObservableProperty] private string workingHourlyWage = "";
    [ObservableProperty] private DateTime? workBeganOn;
    [ObservableProperty] private string offeredHoursPerWeek = "";
    [ObservableProperty] private string offeredHourlyWage = "";
    [ObservableProperty] private string? jobSatisfaction;
    [ObservableProperty] private string otherBenefit = "";
    [ObservableProperty] private bool? childrenUnder21ReceiveMaineCare;
    [ObservableProperty] private string benefitsQuestion = "";
    [ObservableProperty] private string? inPersonLocation;
    [ObservableProperty] private string foreignLanguage = "";
    [ObservableProperty] private string otherAccommodation = "";
    [ObservableProperty] private bool? hasVrCounselor;
    [ObservableProperty] private string vrCounselorName = "";
    [ObservableProperty] private string vrCounselorPhone = "";
    [ObservableProperty] private string? vrStatus;
    [ObservableProperty] private DateTime? vrStatusDate;
    [ObservableProperty] private string? estimatedReturnToWork;
    [ObservableProperty] private bool? hasIpe;
    [ObservableProperty] private string ipeGoal = "";
    [ObservableProperty] private string estimatedHoursPerWeek = "";
    [ObservableProperty] private DateTime? ipeDate;
    [ObservableProperty] private DateTime? ipeExpectedEndDate;
    [ObservableProperty] private string? vrDivision;
    [ObservableProperty] private string vrOfficeAddress = "";
    [ObservableProperty] private DateTime? dolReleaseStart;
    [ObservableProperty] private DateTime? dolReleaseEnd;
    [ObservableProperty] private bool? authorizeSubstanceUseDisclosure;
    [ObservableProperty] private bool? authorizeMentalHealthDisclosure;
    [ObservableProperty] private bool? reviewBeforeRelease;
    [ObservableProperty] private bool? authorizeHivDisclosure;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private string blankFieldsMessage = "";

    public bool HasPerson => person is not null;
    public bool CanGenerate => HasPerson && !IsBusy;
    public bool ShowSchedulingContact => !ScheduleWithConsumer;
    public bool ShowRepresentativePayee => HasRepresentativePayee == true;
    public bool ShowGuardian => HasLegalGuardian == true;
    public bool ShowVrDetails => HasVrCounselor == true;
    public bool ShowIpeDetails => HasIpe == true;
    public string SourceRevision => CwicPacketRules.SourceRevision;

    public event EventHandler<CwicPacketPdfReadyEventArgs>? PdfReady;
    public event EventHandler<CwicPacketProblemEventArgs>? Problem;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }
    partial void OnScheduleWithConsumerChanged(bool value) => OnPropertyChanged(nameof(ShowSchedulingContact));
    partial void OnHasRepresentativePayeeChanged(bool? value) => OnPropertyChanged(nameof(ShowRepresentativePayee));
    partial void OnHasLegalGuardianChanged(bool? value) => OnPropertyChanged(nameof(ShowGuardian));
    partial void OnHasVrCounselorChanged(bool? value) => OnPropertyChanged(nameof(ShowVrDetails));
    partial void OnHasIpeChanged(bool? value) => OnPropertyChanged(nameof(ShowIpeDetails));

    public void SetPerson(Person? value)
    {
        personVersion++;
        person = value;
        PersonName = value?.FullName ?? "Select a consumer";
        BirthDateDisplay = value is null ? "" : value.BirthDate.ToString("MM/dd/yyyy");
        AgeDisplay = value is null ? "" : AgeOn(value.BirthDate, DateTime.Today).ToString();
        ApplyProfileDefaults();
        OnPropertyChanged(nameof(HasPerson));
        OnPropertyChanged(nameof(CanGenerate));
        GenerateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (person is null) return;
        var request = BuildRequest();
        var validation = CwicPacketRules.Validate(request);
        if (validation.Count > 0)
        {
            StatusMessage = string.Join(Environment.NewLine,
                validation.Values.SelectMany(values => values).Select(message => $"• {message}"));
            return;
        }

        var id = person.Id;
        var version = personVersion;
        IsBusy = true;
        StatusMessage = "Preparing the official ten-page CWIC referral packet...";
        BlankFieldsMessage = "";
        try
        {
            var result = await service.GenerateAsync(id, request);
            if (version != personVersion || person?.Id != id) return;
            BlankFieldsMessage = result.BlankFields.Count == 1 &&
                                 result.BlankFields[0] == "Required signatures and signature dates"
                ? "Only the required signatures and dates remain for the consumer or guardian."
                : $"Review before sending: {string.Join(", ", result.BlankFields)}.";
            StatusMessage = "The draft packet is ready to save. Review every page, then obtain the required signatures before sending it.";
            PdfReady?.Invoke(this, new CwicPacketPdfReadyEventArgs(result.Pdf, result.FileName));
        }
        catch (Exception exception)
        {
            if (version != personVersion || person?.Id != id) return;
            StatusMessage = "The CWIC packet could not be generated.";
            Problem?.Invoke(this, new CwicPacketProblemEventArgs(
                "CWIC Packet Not Generated",
                $"The CWIC referral packet could not be generated.\n\n{exception.Message}"));
        }
        finally
        {
            if (version == personVersion) IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        ApplyProfileDefaults();
        StatusMessage = "The packet was reset to the selected consumer's profile information.";
    }

    private bool CanClear() => !IsBusy;

    internal CwicPacketRequest BuildRequest() => new(
        MailingAddress, City, State, Zip, County, HomePhone, CellPhone, Email, MaritalStatus, Gender,
        SpouseReceivesDisabilityBenefits, ReferringOrganization, ScheduleWithConsumer,
        SchedulingContactName, SchedulingContactRelationship, SchedulingContactPhone,
        Selected(MeetingMethods), HasRepresentativePayee, RepresentativePayeeName, RepresentativePayeePhone,
        HasLegalGuardian, GuardianName, GuardianPhone, GuardianCommunicationPermission,
        Selected(EmploymentSituations), SelfEmploymentMonthlyProfit, SelfEmploymentHoursPerMonth,
        WorkingHoursPerWeek, WorkingHourlyWage, DateOnlyOf(WorkBeganOn), OfferedHoursPerWeek,
        OfferedHourlyWage, JobSatisfaction, Selected(Benefits), OtherBenefit,
        ChildrenUnder21ReceiveMaineCare, BenefitsQuestion, InPersonLocation, Selected(Accommodations),
        ForeignLanguage, OtherAccommodation, HasVrCounselor, VrCounselorName, VrCounselorPhone,
        VrStatus, DateOnlyOf(VrStatusDate), EstimatedReturnToWork, HasIpe, IpeGoal,
        EstimatedHoursPerWeek, DateOnlyOf(IpeDate), DateOnlyOf(IpeExpectedEndDate), VrDivision,
        VrOfficeAddress, DateOnlyOf(DolReleaseStart), DateOnlyOf(DolReleaseEnd),
        AuthorizeSubstanceUseDisclosure, AuthorizeMentalHealthDisclosure, ReviewBeforeRelease,
        AuthorizeHivDisclosure);

    private void ApplyProfileDefaults()
    {
        var value = person;
        MailingAddress = value?.Address ?? value?.BillingStreet ?? "";
        City = value?.BillingCity ?? "";
        State = value?.BillingState ?? "ME";
        Zip = value?.BillingZip ?? "";
        County = "";
        HomePhone = value?.PhoneNumber ?? "";
        CellPhone = "";
        Email = value?.Email ?? "";
        MaritalStatus = null;
        Gender = value?.Gender switch
        {
            global::Sati.Gender.Male => "Male", global::Sati.Gender.Female => "Female",
            global::Sati.Gender.NonBinary => "Nonbinary", _ => ""
        };
        SpouseReceivesDisabilityBenefits = null;
        ReferringOrganization = value?.Agency?.Name ?? "";
        ScheduleWithConsumer = true;
        SchedulingContactName = SchedulingContactRelationship = SchedulingContactPhone = "";
        Reset(MeetingMethods);
        HasRepresentativePayee = value is null ? null : value.CaseManagerIsRepPayee;
        RepresentativePayeeName = RepresentativePayeePhone = "";
        HasLegalGuardian = value is null ? null : value.HasGuardian;
        GuardianName = value?.GuardianName ?? "";
        GuardianPhone = GuardianCommunicationPermission = "";
        Reset(EmploymentSituations);
        if (value?.IsEmployed == true) EmploymentSituations.Single(option => option.Value == "Working").IsSelected = true;
        SelfEmploymentMonthlyProfit = SelfEmploymentHoursPerMonth = WorkingHoursPerWeek = WorkingHourlyWage = "";
        WorkBeganOn = null;
        OfferedHoursPerWeek = OfferedHourlyWage = "";
        JobSatisfaction = null;
        Reset(Benefits);
        if (!string.IsNullOrWhiteSpace(value?.MaineCareId)) Benefits.Single(option => option.Value == "MaineCare").IsSelected = true;
        OtherBenefit = "";
        ChildrenUnder21ReceiveMaineCare = null;
        BenefitsQuestion = "";
        InPersonLocation = null;
        Reset(Accommodations);
        ForeignLanguage = OtherAccommodation = "";
        HasVrCounselor = value is null ? null : value.OpenWithVR;
        VrCounselorName = value?.VrCounselorName ?? "";
        VrCounselorPhone = "";
        VrStatus = value?.OpenWithVR == true ? "Service" : null;
        VrStatusDate = null;
        EstimatedReturnToWork = null;
        HasIpe = null;
        IpeGoal = EstimatedHoursPerWeek = "";
        IpeDate = IpeExpectedEndDate = null;
        VrDivision = value?.OpenWithVR == true ? "Vocational Rehabilitation" : null;
        VrOfficeAddress = "";
        DolReleaseStart = DateTime.Today;
        DolReleaseEnd = DateTime.Today.AddYears(1);
        AuthorizeSubstanceUseDisclosure = AuthorizeMentalHealthDisclosure = ReviewBeforeRelease = AuthorizeHivDisclosure = null;
        BlankFieldsMessage = StatusMessage = "";
    }

    private static IReadOnlyList<CwicChoiceOption> Options(IEnumerable<string> values) =>
        values.Select(value => new CwicChoiceOption(value)).ToList();
    private static IReadOnlyList<string> Selected(IEnumerable<CwicChoiceOption> options) =>
        options.Where(option => option.IsSelected).Select(option => option.Value).ToList();
    private static void Reset(IEnumerable<CwicChoiceOption> options)
    {
        foreach (var option in options) option.IsSelected = false;
    }
    private static DateOnly? DateOnlyOf(DateTime? value) => value is DateTime date ? DateOnly.FromDateTime(date) : null;
    private static int AgeOn(DateTime birthDate, DateTime onDate)
    {
        var age = onDate.Year - birthDate.Year;
        if (birthDate.Date > onDate.Date.AddYears(-age)) age--;
        return Math.Max(0, age);
    }
}

public partial class CwicChoiceOption(string value) : ObservableObject
{
    public string Value { get; } = value;
    [ObservableProperty] private bool isSelected;
}

public sealed record CwicNullableChoice(string DisplayName, bool? Value);

public sealed class CwicPacketPdfReadyEventArgs(byte[] content, string suggestedFileName) : EventArgs
{
    public byte[] Content { get; } = content;
    public string SuggestedFileName { get; } = suggestedFileName;
}

public sealed class CwicPacketProblemEventArgs(string title, string message) : EventArgs
{
    public string Title { get; } = title;
    public string Message { get; } = message;
}
