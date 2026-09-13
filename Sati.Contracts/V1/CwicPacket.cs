namespace Sati.Contracts.V1;

/// <summary>
/// User-entered answers for MaineHealth's Benefits Counseling Services referral packet.
/// Identity, date of birth, age, and Social Security number are deliberately absent:
/// the authoritative service derives those values from the selected consumer.
/// </summary>
public sealed record CwicPacketRequest(
    string? MailingAddress = null,
    string? City = null,
    string? State = null,
    string? Zip = null,
    string? County = null,
    string? HomePhone = null,
    string? CellPhone = null,
    string? Email = null,
    string? MaritalStatus = null,
    string? Gender = null,
    bool? SpouseReceivesDisabilityBenefits = null,
    string? ReferringOrganization = null,
    bool ScheduleWithConsumer = true,
    string? SchedulingContactName = null,
    string? SchedulingContactRelationship = null,
    string? SchedulingContactPhone = null,
    IReadOnlyList<string>? MeetingMethods = null,
    bool? HasRepresentativePayee = null,
    string? RepresentativePayeeName = null,
    string? RepresentativePayeePhone = null,
    bool? HasLegalGuardian = null,
    string? GuardianName = null,
    string? GuardianPhone = null,
    string? GuardianCommunicationPermission = null,
    IReadOnlyList<string>? EmploymentSituations = null,
    string? SelfEmploymentMonthlyProfit = null,
    string? SelfEmploymentHoursPerMonth = null,
    string? WorkingHoursPerWeek = null,
    string? WorkingHourlyWage = null,
    DateOnly? WorkBeganOn = null,
    string? OfferedHoursPerWeek = null,
    string? OfferedHourlyWage = null,
    string? JobSatisfaction = null,
    IReadOnlyList<string>? Benefits = null,
    string? OtherBenefit = null,
    bool? ChildrenUnder21ReceiveMaineCare = null,
    string? BenefitsQuestion = null,
    string? InPersonLocation = null,
    IReadOnlyList<string>? Accommodations = null,
    string? ForeignLanguage = null,
    string? OtherAccommodation = null,
    bool? HasVrCounselor = null,
    string? VrCounselorName = null,
    string? VrCounselorPhone = null,
    string? VrStatus = null,
    DateOnly? VrStatusDate = null,
    string? EstimatedReturnToWork = null,
    bool? HasIpe = null,
    string? IpeGoal = null,
    string? EstimatedHoursPerWeek = null,
    DateOnly? IpeDate = null,
    DateOnly? IpeExpectedEndDate = null,
    string? VrDivision = null,
    string? VrOfficeAddress = null,
    DateOnly? DolReleaseStart = null,
    DateOnly? DolReleaseEnd = null,
    bool? AuthorizeSubstanceUseDisclosure = null,
    bool? AuthorizeMentalHealthDisclosure = null,
    bool? ReviewBeforeRelease = null,
    bool? AuthorizeHivDisclosure = null);

public sealed record CwicPacketSubject(
    int PersonId,
    string FullName,
    DateTime BirthDate,
    string? SocialSecurityNumber);

public sealed record CwicPacketResult(
    byte[] Pdf,
    string FileName,
    IReadOnlyList<string> BlankFields,
    string SourceRevision);

/// <summary>Closed choice sets and input limits for the referral packet.</summary>
public static class CwicPacketRules
{
    public const string SourceRevision = "BCS referral packet, revised 12/2020";
    public const int ShortTextMaxLength = 200;
    public const int NarrativeMaxLength = 1_500;

    public static IReadOnlyList<string> MaritalStatuses { get; } = ["Single", "Widowed", "Married"];
    public static IReadOnlyList<string> MeetingMethods { get; } = ["Phone/Mail", "Virtual (Zoom)", "In-person"];
    public static IReadOnlyList<string> EmploymentSituations { get; } =
    [
        "Thinking about work", "Applied or interviewed", "Self-employed", "Working", "Job offer"
    ];
    public static IReadOnlyList<string> JobSatisfactionChoices { get; } =
    [
        "Very dissatisfied", "Dissatisfied", "Not sure", "Satisfied", "Very satisfied"
    ];
    public static IReadOnlyList<string> BenefitChoices { get; } =
    [
        "SSI", "Title II", "MaineCare", "Medicare", "SNAP", "Housing", "Veterans Benefits", "Other"
    ];
    public static IReadOnlyList<string> AccommodationChoices { get; } =
    [
        "Sign Language Interpreter", "Foreign Language Interpreter", "Large print documents", "Other"
    ];
    public static IReadOnlyList<string> VrStatuses { get; } = ["In application", "Eligible", "Service", "Employed"];
    public static IReadOnlyList<string> ReturnToWorkChoices { get; } =
    [
        "Next two months", "Next six months", "Next year or two", "Not sure"
    ];
    public static IReadOnlyList<string> VrDivisions { get; } =
    [
        "Vocational Rehabilitation", "Blind and Visually Impaired"
    ];

    public static IReadOnlyList<string> MaineLocations { get; } =
    [
        "Bangor", "Dover-Foxcroft", "Machias", "Calais", "Fort Kent", "Millinocket", "Caribou", "Houlton",
        "Newport", "Ellsworth", "Lincoln", "Presque Isle", "Augusta", "Rockland", "Belfast", "Skowhegan",
        "Boothbay", "Topsham", "Brunswick", "Waterville", "Bridgton", "Wilton", "Lewiston", "Rumford",
        "South Paris", "Biddeford", "Portland", "Sanford"
    ];

    public static IReadOnlyDictionary<string, string[]> Validate(CwicPacketRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var values))
                errors[field] = values = [];
            values.Add(message);
        }

        void CheckLength(string field, string? value, int maximum = ShortTextMaxLength)
        {
            if (value?.Trim().Length > maximum)
                Add(field, $"{field} cannot exceed {maximum} characters.");
        }

        void CheckChoice(string field, string? value, IReadOnlyList<string> allowed)
        {
            if (!string.IsNullOrWhiteSpace(value) && !allowed.Contains(value, StringComparer.Ordinal))
                Add(field, $"{field} contains an unknown choice.");
        }

        void CheckChoices(string field, IReadOnlyList<string>? values, IReadOnlyList<string> allowed)
        {
            foreach (var value in values ?? [])
                CheckChoice(field, value, allowed);
        }

        foreach (var (field, value) in TextFields(request))
            CheckLength(field, value, field is "BenefitsQuestion" or "IpeGoal" ? NarrativeMaxLength : ShortTextMaxLength);

        CheckChoice(nameof(request.MaritalStatus), request.MaritalStatus, MaritalStatuses);
        CheckChoice(nameof(request.JobSatisfaction), request.JobSatisfaction, JobSatisfactionChoices);
        CheckChoice(nameof(request.InPersonLocation), request.InPersonLocation, MaineLocations);
        CheckChoice(nameof(request.VrStatus), request.VrStatus, VrStatuses);
        CheckChoice(nameof(request.EstimatedReturnToWork), request.EstimatedReturnToWork, ReturnToWorkChoices);
        CheckChoice(nameof(request.VrDivision), request.VrDivision, VrDivisions);
        CheckChoices(nameof(request.MeetingMethods), request.MeetingMethods, MeetingMethods);
        CheckChoices(nameof(request.EmploymentSituations), request.EmploymentSituations, EmploymentSituations);
        CheckChoices(nameof(request.Benefits), request.Benefits, BenefitChoices);
        CheckChoices(nameof(request.Accommodations), request.Accommodations, AccommodationChoices);

        if (request.DolReleaseStart is DateOnly start && request.DolReleaseEnd is DateOnly end && end < start)
            Add(nameof(request.DolReleaseEnd), "The release end date cannot be before its start date.");
        if (request.DolReleaseStart is DateOnly releaseStart && request.DolReleaseEnd is DateOnly releaseEnd &&
            releaseEnd > releaseStart.AddYears(1))
            Add(nameof(request.DolReleaseEnd), "The Maine DOL release cannot cover more than one year.");

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> BlankFields(CwicPacketSubject subject, CwicPacketRequest request)
    {
        var blanks = new List<string>();
        void Need(string label, string? value) { if (string.IsNullOrWhiteSpace(value)) blanks.Add(label); }
        void NeedAnswer(string label, bool? value) { if (value is null) blanks.Add(label); }
        void NeedAny(string label, IReadOnlyCollection<string>? value) { if (value is null || value.Count == 0) blanks.Add(label); }

        Need("Social Security number", subject.SocialSecurityNumber);
        Need("Mailing address", request.MailingAddress);
        Need("City", request.City);
        Need("ZIP code", request.Zip);
        Need("County", request.County);
        Need("Marital status", request.MaritalStatus);
        NeedAnswer("Spouse disability-benefit answer", request.SpouseReceivesDisabilityBenefits);
        Need("Referring organization", request.ReferringOrganization);
        NeedAny("Preferred meeting method", request.MeetingMethods);
        NeedAnswer("Representative payee answer", request.HasRepresentativePayee);
        NeedAnswer("Legal guardian answer", request.HasLegalGuardian);
        NeedAny("Job situation", request.EmploymentSituations);
        Need("Job-situation satisfaction", request.JobSatisfaction);
        NeedAny("Benefits received", request.Benefits);
        NeedAnswer("Children under 21/MaineCare answer", request.ChildrenUnder21ReceiveMaineCare);
        NeedAnswer("VR counselor answer", request.HasVrCounselor);
        blanks.Add("Required signatures and signature dates");
        return blanks.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<KeyValuePair<string, string?>> TextFields(CwicPacketRequest request)
    {
        yield return new(nameof(request.MailingAddress), request.MailingAddress);
        yield return new(nameof(request.City), request.City);
        yield return new(nameof(request.State), request.State);
        yield return new(nameof(request.Zip), request.Zip);
        yield return new(nameof(request.County), request.County);
        yield return new(nameof(request.HomePhone), request.HomePhone);
        yield return new(nameof(request.CellPhone), request.CellPhone);
        yield return new(nameof(request.Email), request.Email);
        yield return new(nameof(request.Gender), request.Gender);
        yield return new(nameof(request.ReferringOrganization), request.ReferringOrganization);
        yield return new(nameof(request.SchedulingContactName), request.SchedulingContactName);
        yield return new(nameof(request.SchedulingContactRelationship), request.SchedulingContactRelationship);
        yield return new(nameof(request.SchedulingContactPhone), request.SchedulingContactPhone);
        yield return new(nameof(request.RepresentativePayeeName), request.RepresentativePayeeName);
        yield return new(nameof(request.RepresentativePayeePhone), request.RepresentativePayeePhone);
        yield return new(nameof(request.GuardianName), request.GuardianName);
        yield return new(nameof(request.GuardianPhone), request.GuardianPhone);
        yield return new(nameof(request.GuardianCommunicationPermission), request.GuardianCommunicationPermission);
        yield return new(nameof(request.SelfEmploymentMonthlyProfit), request.SelfEmploymentMonthlyProfit);
        yield return new(nameof(request.SelfEmploymentHoursPerMonth), request.SelfEmploymentHoursPerMonth);
        yield return new(nameof(request.WorkingHoursPerWeek), request.WorkingHoursPerWeek);
        yield return new(nameof(request.WorkingHourlyWage), request.WorkingHourlyWage);
        yield return new(nameof(request.OfferedHoursPerWeek), request.OfferedHoursPerWeek);
        yield return new(nameof(request.OfferedHourlyWage), request.OfferedHourlyWage);
        yield return new(nameof(request.OtherBenefit), request.OtherBenefit);
        yield return new(nameof(request.BenefitsQuestion), request.BenefitsQuestion);
        yield return new(nameof(request.ForeignLanguage), request.ForeignLanguage);
        yield return new(nameof(request.OtherAccommodation), request.OtherAccommodation);
        yield return new(nameof(request.VrCounselorName), request.VrCounselorName);
        yield return new(nameof(request.VrCounselorPhone), request.VrCounselorPhone);
        yield return new(nameof(request.IpeGoal), request.IpeGoal);
        yield return new(nameof(request.EstimatedHoursPerWeek), request.EstimatedHoursPerWeek);
        yield return new(nameof(request.VrOfficeAddress), request.VrOfficeAddress);
    }
}
