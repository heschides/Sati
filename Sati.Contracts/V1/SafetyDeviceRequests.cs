namespace Sati.Contracts.V1;

public sealed record SafetyDeviceEntry(
    string? NameAndType = null,
    string? Purpose = null,
    string? WhenUsed = null,
    string? Level = null);

/// <summary>Case-manager transcription for the OADS form. Signatures are intentionally absent.</summary>
public sealed record SafetyDeviceRequest(
    string? MemberOrGuardianContact = null,
    string? ProgramNameAndAddress = null,
    string? ProgramContacts = null,
    string? ProgramContactNumbers = null,
    string? ProgramContactEmails = null,
    string? MedicalProviderName = null,
    IReadOnlyList<SafetyDeviceEntry>? Devices = null,
    string? LessRestrictiveStrategies = null,
    string? EvaluationPlan = null,
    string? OtherResidentsAccommodations = null,
    DateOnly? PlanningTeamMeetingDate = null);

public sealed record SafetyDeviceSubject(
    int PersonId,
    string FullName,
    DateTime BirthDate,
    string? EvergreenId,
    string? MaineCareId,
    string? Address,
    string? GuardianName,
    string? CaseManagerName,
    string? CaseManagerEmail);

public sealed record SafetyDeviceResult(
    byte[] Pdf,
    string FileName,
    IReadOnlyList<string> ReviewItems,
    string SourceRevision);

public static class SafetyDeviceRules
{
    public const string SourceRevision = "OADS Safety Device Request Form (4/2026)";
    public const int MaximumDevices = 10; // Five on page 2 and five in Appendix B.

    public static IReadOnlyDictionary<string, string[]> Validate(SafetyDeviceRequest request, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Check(string name, string? value, int max) {
            if (value?.Trim().Length > max)
                errors[name] = [$"{name} cannot exceed {max} characters."];
        }

        Check(nameof(request.MemberOrGuardianContact), request.MemberOrGuardianContact, 200);
        CheckFit(nameof(request.MemberOrGuardianContact), request.MemberOrGuardianContact, 30, 2);
        Check(nameof(request.ProgramNameAndAddress), request.ProgramNameAndAddress, 300);
        CheckFit(nameof(request.ProgramNameAndAddress), request.ProgramNameAndAddress, 70, 2);
        Check(nameof(request.ProgramContacts), request.ProgramContacts, 200);
        CheckFit(nameof(request.ProgramContacts), request.ProgramContacts, 32, 3);
        Check(nameof(request.ProgramContactNumbers), request.ProgramContactNumbers, 200);
        CheckFit(nameof(request.ProgramContactNumbers), request.ProgramContactNumbers, 30, 2);
        Check(nameof(request.ProgramContactEmails), request.ProgramContactEmails, 200);
        CheckFit(nameof(request.ProgramContactEmails), request.ProgramContactEmails, 70, 2);
        Check(nameof(request.MedicalProviderName), request.MedicalProviderName, 200);
        CheckFit(nameof(request.MedicalProviderName), request.MedicalProviderName, 28, 1);
        Check(nameof(request.LessRestrictiveStrategies), request.LessRestrictiveStrategies, 1500);
        CheckFit(nameof(request.LessRestrictiveStrategies), request.LessRestrictiveStrategies, 68, 2);
        Check(nameof(request.EvaluationPlan), request.EvaluationPlan, 1500);
        CheckFit(nameof(request.EvaluationPlan), request.EvaluationPlan, 68, 2);
        Check(nameof(request.OtherResidentsAccommodations), request.OtherResidentsAccommodations, 1500);
        CheckFit(nameof(request.OtherResidentsAccommodations), request.OtherResidentsAccommodations, 68, 2);
        if (request.Devices?.Count > MaximumDevices)
            errors[nameof(request.Devices)] = [$"The OADS form has room for {MaximumDevices} devices."];
        for (var i = 0; i < (request.Devices?.Count ?? 0); i++)
        {
            var device = request.Devices![i];
            if (device is null)
            {
                errors[$"Devices[{i}]"] = ["A device row cannot be null."];
                continue;
            }
            Check($"Devices[{i}].NameAndType", device.NameAndType, 200);
            CheckFit($"Devices[{i}].NameAndType", device.NameAndType, 17, 3);
            Check($"Devices[{i}].Purpose", device.Purpose, 300);
            CheckFit($"Devices[{i}].Purpose", device.Purpose, 12, 3);
            Check($"Devices[{i}].WhenUsed", device.WhenUsed, 200);
            CheckFit($"Devices[{i}].WhenUsed", device.WhenUsed, 15, 3);
            if (!string.IsNullOrWhiteSpace(device.Level) && device.Level is not ("1" or "2"))
                errors[$"Devices[{i}].Level"] = ["Safety device level must be 1 or 2."];
        }
        if (request.PlanningTeamMeetingDate is DateOnly date && date > today)
            errors[nameof(request.PlanningTeamMeetingDate)] = ["The planning team meeting date cannot be in the future."];
        return errors;

        void CheckFit(string name, string? value, int width, int lines)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (WrapForPdf(value, width).Split('\n').Length > lines)
                errors[name] = [$"{name} will not fit its PDF box. Use at most {lines} lines of about {width} characters."];
        }
    }

    /// <summary>Keep the printed appearance within the state's fixed-size AcroForm boxes.</summary>
    public static string WrapForPdf(string value, int width)
    {
        var output = new List<string>();
        foreach (var paragraph in value.Trim().Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var remainder = paragraph.Trim();
            if (remainder.Length == 0) { output.Add(""); continue; }
            while (remainder.Length > width)
            {
                var breakAt = remainder.LastIndexOf(' ', width);
                if (breakAt <= 0) breakAt = width;
                output.Add(remainder[..breakAt].TrimEnd());
                remainder = remainder[breakAt..].TrimStart();
            }
            output.Add(remainder);
        }
        return string.Join("\n", output);
    }

    public static IReadOnlyList<string> FindReviewItems(SafetyDeviceSubject subject, SafetyDeviceRequest request)
    {
        var items = new List<string>();
        if (string.IsNullOrWhiteSpace(subject.MaineCareId)) items.Add("MaineCare number");
        if (string.IsNullOrWhiteSpace(request.ProgramNameAndAddress)) items.Add("Service or waiver program name and address");
        if (string.IsNullOrWhiteSpace(request.MedicalProviderName)) items.Add("Medical provider name");
        if (request.Devices is null || !request.Devices.Any(device => !string.IsNullOrWhiteSpace(device.NameAndType)))
            items.Add("Safety device recommendations");
        if (string.IsNullOrWhiteSpace(request.LessRestrictiveStrategies)) items.Add("Less restrictive strategies");
        if (string.IsNullOrWhiteSpace(request.EvaluationPlan)) items.Add("Evaluation and team review plan");
        if (request.PlanningTeamMeetingDate is null) items.Add("Planning team meeting date");
        items.Add("Medical provider date and signature for each device");
        items.Add("Member or guardian signature and date");
        items.Add("Planning team notes in Evergreen; HCBS modification if applicable");
        return items;
    }
}
