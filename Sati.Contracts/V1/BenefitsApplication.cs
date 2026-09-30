namespace Sati.Contracts.V1;

/// <summary>
/// Positions are measured on the published 612 x 792 point form, represented here
/// in its 850 x 1100 review image. The generator applies one scale to both axes.
/// The catalog is shared by the desktop wizard and the authoritative PDF writer.
/// </summary>
public enum BenefitsAnswerKind { Text, YesNo, Check }

public sealed record BenefitsAnswerField(
    string Key, string Label, int Page, BenefitsAnswerKind Kind,
    double X, double Y, double Width = 0, double OtherX = 0,
    int MaxLength = 80, bool Sensitive = false);

public sealed record BenefitsApplicationStep(string Title, int Page);

public sealed record BenefitsApplicationRequest(IReadOnlyDictionary<string, string> Answers);

public sealed record BenefitsApplicationResult(byte[] Pdf, string FileName,
    IReadOnlyList<string> ReviewItems);

public static class BenefitsApplicationRules
{
    public const string SourceRevision = "Maine DHHS OFI Application for Benefits (4/30/2024)";
    public const string SourceSha256 = "E6538B5434E3A9FF143C4FB9447DA436F88FDA818317CC63A59302CAFC633F8A";

    public static IReadOnlyList<BenefitsApplicationStep> Steps { get; } =
    [
        new("Programs and initial questions", 3),
        new("Applicant and contact", 4),
        new("Household people 1 and 2", 5),
        new("Household people 3 and 4", 6),
        new("Household people 5 and 6", 7),
        new("Relationships and household", 8),
        new("Income", 9),
        new("Assets", 10),
        new("Expenses and deductions", 11),
        new("Tax and health insurance", 12),
        new("TANF and emergency assistance", 13),
        new("Tribal household appendix", 14),
        new("Authorized representative appendix", 15),
    ];

    private static BenefitsAnswerField T(string key, string label, int page,
        double x, double y, double width, int max = 80, bool sensitive = false) =>
        new(key, label, page, BenefitsAnswerKind.Text, x, y, width, MaxLength: max,
            Sensitive: sensitive);
    private static BenefitsAnswerField Y(string key, string label, int page,
        double yesX, double noX, double y) =>
        new(key, label, page, BenefitsAnswerKind.YesNo, yesX, y, OtherX: noX);
    private static BenefitsAnswerField C(string key, string label, int page,
        double x, double y) =>
        new(key, label, page, BenefitsAnswerKind.Check, x, y);

    public static IReadOnlyList<BenefitsAnswerField> Fields { get; } = BuildFields();
    public static IReadOnlyDictionary<string, BenefitsAnswerField> ByKey { get; } =
        Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string[]> Validate(BenefitsApplicationRequest? request)
    {
        if (request?.Answers is null)
            return new Dictionary<string, string[]> { ["answers"] = ["Answers are required."] };
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.Answers.Count > Fields.Count)
            errors["answers"] = ["Too many answers were supplied."];
        foreach (var (key, value) in request.Answers)
        {
            if (!ByKey.TryGetValue(key, out var field))
            {
                errors[key] = ["This is not an application question."];
                continue;
            }
            if (value is null || value.Length > field.MaxLength ||
                value.Any(character => char.IsControl(character) && character is not '\r' and not '\n'))
            {
                errors[key] = ["The answer is too long or contains a control character."];
                continue;
            }
            if (field.Kind == BenefitsAnswerKind.YesNo &&
                value is not ("" or "Yes" or "No") ||
                field.Kind == BenefitsAnswerKind.Check &&
                value is not ("" or "True" or "False"))
                errors[key] = ["Select a listed answer."];
        }
        if (!new[] { "program.snap", "program.tanf", "program.childCare",
                "program.maineCare", "program.medicareSavings", "program.emergency" }
            .Any(key => request.Answers.GetValueOrDefault(key) == "True"))
            errors["program"] = ["Select at least one benefit program."];
        for (var index = 1; index <= 6; index++)
        {
            var prefix = $"person{index}.";
            CheckExclusive(request.Answers, errors, prefix + "gender",
                prefix + "male", prefix + "female", prefix + "nonbinary");
            CheckExclusive(request.Answers, errors, prefix + "marital",
                prefix + "single", prefix + "married");
            CheckExclusive(request.Answers, errors, prefix + "ethnicity",
                prefix + "hispanic", prefix + "notHispanic");
        }
        if (request.Answers.GetValueOrDefault("person1.noHomeAddress") == "True" &&
            !string.IsNullOrWhiteSpace(request.Answers.GetValueOrDefault("person1.homeAddress")))
            errors["person1.homeAddress"] = ["Clear the home address when no home address is selected."];
        return errors;
    }

    private static void CheckExclusive(IReadOnlyDictionary<string, string> answers,
        Dictionary<string, string[]> errors, string key, params string[] options)
    {
        if (options.Count(option => answers.GetValueOrDefault(option) == "True") > 1)
            errors[key] = ["Select only one answer in this group."];
    }

    public static IReadOnlyList<string> ReviewItems(BenefitsApplicationRequest request)
    {
        var items = new List<string>
        {
            "Applicant or authorized representative signature and date on page 3",
            "All pages and program-specific questions before submission",
            "For more than six household members, copy and complete page 7",
            "For additional health insurance plans, attach the requested information"
        };
        if (request.Answers.TryGetValue("appendixB.authorized", out var authorized) && authorized == "True")
            items.Add("Both signatures and dates on Appendix B, page 15");
        return items;
    }

    private static IReadOnlyList<BenefitsAnswerField> BuildFields()
    {
        var fields = new List<BenefitsAnswerField>
        {
            // Page 3. Signing is intentionally outside the catalog.
            C("program.snap", "SNAP food assistance", 3, 40, 70),
            C("program.tanf", "TANF", 3, 40, 96),
            C("program.childCare", "TANF related child care", 3, 40, 122),
            C("program.maineCare", "MaineCare or CHIP", 3, 434, 70),
            C("program.medicareSavings", "Medicare Savings Program", 3, 434, 96),
            C("program.emergency", "Emergency Assistance", 3, 434, 122),
            Y("initial.medicalBills", "Help with medical bills from the past three months", 3, 707, 760, 700),
            T("initial.medicalBillsDetails", "Who and which months", 3, 240, 719, 560),
            Y("initial.deductible", "Six-month deductible quote", 3, 707, 760, 743),
            Y("initial.familyPlanning", "Limited Family Planning Services review", 3, 707, 760, 769),
            T("initial.familyPlanningWho", "Who requests family planning", 3, 144, 810, 650),
            Y("initial.snapCash", "SNAP expedited: $100 or less and less than $150 income", 3, 707, 760, 931),
            Y("initial.snapShelter", "SNAP expedited: income and cash below shelter cost", 3, 707, 760, 979),
            Y("initial.migrant", "Migrant or seasonal farm worker", 3, 707, 760, 1021),

            // Page 4. Name, birth date, and any on-file SSN are server-derived.
            Y("person1.maineCare", "Applicant applying for MaineCare", 4, 692, 745, 716),
            C("person1.male", "Applicant: male", 4, 94, 821),
            C("person1.female", "Applicant: female", 4, 150, 821),
            C("person1.nonbinary", "Applicant: non-binary", 4, 222, 821),
            C("person1.single", "Applicant: single", 4, 543, 821),
            C("person1.married", "Applicant: married", 4, 608, 821),
            T("person1.homeAddress", "Home address", 4, 40, 853, 770, 180),
            C("person1.noHomeAddress", "No home address", 4, 39, 885),
            T("person1.mailingAddress", "Mailing address", 4, 40, 918, 770, 180),
            T("person1.phone", "Phone number", 4, 40, 956, 245),
            C("person1.phoneCell", "Phone type: cell", 4, 310, 971),
            C("person1.phoneHome", "Phone type: home", 4, 370, 971),
            C("person1.phoneWork", "Phone type: work", 4, 441, 971),
            T("person1.language", "Preferred language", 4, 510, 956, 300),
            T("person1.email", "Email address", 4, 40, 1037, 770, 180),
        };

        AddHouseholdPerson(fields, 1, 5, 50);
        AddHouseholdPerson(fields, 2, 5, 497);
        AddHouseholdPerson(fields, 3, 6, 41);
        AddHouseholdPerson(fields, 4, 6, 562);
        AddHouseholdPerson(fields, 5, 7, 41);
        AddHouseholdPerson(fields, 6, 7, 562);

        AddRelationships(fields);
        AddIncome(fields);
        AddAssets(fields);
        AddExpenses(fields);
        AddTaxAndInsurance(fields);
        AddTanfAndEmergency(fields);
        AddTribal(fields);
        AddRepresentative(fields);
        return fields;
    }

    private static void AddHouseholdPerson(List<BenefitsAnswerField> fields, int index, int page, double top)
    {
        var prefix = $"person{index}.";
        // Page 5 uses a taller household block than pages 6 and 7. Every
        // coordinate below refers to a visible box on that exact source page.
        var compact = page >= 6;
        if (index != 1)
        {
            fields.Add(Y(prefix + "maineCare", $"Person {index}: applying for MaineCare", page, 693, 746, top + 8));
            fields.Add(T(prefix + "name", $"Person {index}: full name", page, 40, top + 83, 350));
            fields.Add(T(prefix + "ssn", $"Person {index}: Social Security number", page, 413, top + 83, 220, 11, true));
            fields.Add(T(prefix + "birthDate", $"Person {index}: birth date", page, 650, top + 83, 160, 20));
            foreach (var (key, label, x) in new[] { ("male", "male", 94d), ("female", "female", 151d), ("nonbinary", "non-binary", 225d), ("single", "single", 544d), ("married", "married", 608d) })
                fields.Add(C(prefix + key, $"Person {index}: {label}", page, x, top + 112));
        }
        var baseY = index switch
        {
            1 => 73d,
            2 => 630d,
            3 or 5 => 174d,
            _ => 696d
        };
        var laterOffset = index == 1 ? -6 : 0;
        fields.Add(Y(prefix + "school", $"Person {index}: enrolled full-time", page, 272, 321, baseY));
        fields.Add(T(prefix + "schoolName", $"Person {index}: school name", page, 143, baseY + 29, 390));
        fields.Add(T(prefix + "grade", $"Person {index}: grade or year", page, 678, baseY + 29, 125));
        fields.Add(Y(prefix + "pregnant", $"Person {index}: pregnant", page, 164, 215, baseY + 52));
        fields.Add(T(prefix + "dueDate", $"Person {index}: estimated due date", page, 438, baseY + 52, 109, 20));
        fields.Add(T(prefix + "babies", $"Person {index}: babies expected", page, 769, baseY + 52, 37, 4));
        fields.Add(Y(prefix + "citizen", $"Person {index}: US citizen or national", page, 281, 333, baseY + 96 + laterOffset));
        fields.Add(Y(prefix + "naturalized", $"Person {index}: naturalized or derived citizen", page,
            compact ? (index is 4 or 6 ? 350 : 338) : 58,
            compact ? (index is 4 or 6 ? 696 : 683) : 405,
            baseY + (compact ? 118 : 136) + laterOffset));
        fields.Add(T(prefix + "alienNumber", $"Person {index}: alien number", page, 130,
            baseY + (compact ? 141 : 160) + laterOffset, 224, 40, true));
        fields.Add(T(prefix + "certificateNumber", $"Person {index}: certificate number", page, 503,
            baseY + (compact ? 141 : 160) + laterOffset, 299, 40, true));
        fields.Add(C(prefix + "immigrationStatus", $"Person {index}: has immigration status", page, 58,
            baseY + (compact ? 181 : 200) + laterOffset));
        fields.Add(T(prefix + "immigrationDescription", $"Person {index}: immigration status", page, 170,
            baseY + (compact ? 210 : 231) + laterOffset, 275, 80, true));
        fields.Add(T(prefix + "uscis", $"Person {index}: USCIS number", page, 587,
            baseY + (compact ? 210 : 231) + laterOffset, 218, 40, true));
        fields.Add(T(prefix + "documentType", $"Person {index}: immigration document type", page, 151,
            baseY + (compact ? 239 : 259) + laterOffset, 234, 80, true));
        fields.Add(T(prefix + "documentNumber", $"Person {index}: document number", page, 588,
            baseY + (compact ? 239 : 259) + laterOffset, 218, 40, true));
        fields.Add(Y(prefix + "enteredBefore1996", $"Person {index}: entered US before August 22, 1996", page,
            395, 448, baseY + (compact ? 261 : 282) + laterOffset));
        fields.Add(Y(prefix + "veteran", $"Person {index}: veteran or active-duty family", page,
            570, 623, baseY + (compact ? 283 : 306) + laterOffset));
        fields.Add(C(prefix + "hispanic", $"Person {index}: Hispanic or Latino", page, 166,
            baseY + (compact ? 309 : 330) + laterOffset));
        fields.Add(C(prefix + "notHispanic", $"Person {index}: non-Hispanic or Latino", page, 309,
            baseY + (compact ? 309 : 330) + laterOffset));
        foreach (var (key, label, x, y) in new[]
        {
            ("white", "White", 278d, baseY + (compact ? 333 : 353) + laterOffset),
            ("black", "Black/African American", 346d, baseY + (compact ? 333 : 353) + laterOffset),
            ("pacific", "Native Hawaiian/Pacific Islander", 519d, baseY + (compact ? 333 : 353) + laterOffset),
            ("asian", "Asian", 745d, baseY + (compact ? 333 : 353) + laterOffset),
            ("native", "American Indian or Alaska Native", 40d, baseY + (compact ? 352 : 372) + laterOffset),
            ("otherRace", "Other race", 270d, baseY + (compact ? 352 : 372) + laterOffset)
        }) fields.Add(C(prefix + key, $"Person {index}: {label}", page, x, y));
        fields.Add(T(prefix + "otherRaceText", $"Person {index}: other race description", page, 325,
            baseY + (compact ? 352 : 372) + laterOffset, 370));
    }

    private static void AddRelationships(List<BenefitsAnswerField> fields)
    {
        for (var row = 1; row <= 5; row++)
        {
            var y = 191 + (row - 1) * 27;
            fields.Add(T($"relationship.{row}.name", $"Relationship {row}: household member", 8, 34, y, 375));
            fields.Add(T($"relationship.{row}.type", $"Relationship {row}: relationship to applicant", 8, 426, y, 385));
        }
        fields.AddRange([
            Y("household.specialNeed", "Applicant has a special health or disability need", 8, 631, 686, 398),
            T("household.specialNeedWho", "Who has a special need", 8, 144, 428, 660),
            Y("household.tribal", "American Indian or Alaska Native applicant", 8, 73, 460, 507),
            Y("household.foster", "Applicant in foster care or state custody", 8, 334, 389, 537),
            T("household.fosterWho", "Who is in foster care", 8, 502, 538, 305),
            Y("household.formerFoster", "Under 26 and formerly in foster care at age 18", 8, 604, 658, 558),
            T("household.formerFosterWho", "Who was formerly in foster care", 8, 143, 582, 275),
            T("household.formerFosterState", "State of former foster care", 8, 665, 582, 142),
            Y("household.incarcerated", "Applicant currently in jail or prison", 8, 305, 359, 614),
            T("household.incarceratedWho", "Who is incarcerated", 8, 520, 614, 290),
            T("household.incarcerationDate", "Incarceration date", 8, 162, 637, 213, 20),
            T("household.releaseDate", "Anticipated release date", 8, 638, 637, 169, 20),
            Y("snap.hadEbt", "Had an EBT or P-EBT card", 8, 470, 520, 708),
            Y("snap.stillHasEbt", "Still has EBT card", 8, 726, 778, 708),
            T("snap.mealHousehold", "Number living and preparing meals together", 8, 739, 735, 70, 8),
            Y("snap.otherState", "SNAP in another state in past three years", 8, 635, 689, 762),
            T("snap.otherStateWho", "Who received SNAP in another state", 8, 145, 790, 266),
            T("snap.otherStateName", "State where SNAP was received", 8, 531, 790, 280),
            Y("snap.parole", "Fleeing felony or parole or probation violation", 8, 95, 145, 827),
            T("snap.paroleWho", "Who has parole or probation issue", 8, 146, 859, 660),
            Y("snap.multiStateConviction", "Conviction for receiving benefits in multiple states", 8, 638, 691, 894),
            T("snap.multiStateWho", "Who has multi-state conviction", 8, 144, 923, 664),
            Y("snap.disqualifyingOffense", "Specified adult conviction after February 7, 2014", 8, 692, 743, 978),
            Y("snap.sentenceCompliance", "Complying with sentence terms", 8, 446, 493, 1000),
            T("snap.noncompliantWho", "Who is not complying with sentence", 8, 320, 1028, 487)
        ]);
    }

    private static void AddIncome(List<BenefitsAnswerField> fields)
    {
        for (var row = 1; row <= 4; row++)
        {
            var y = 257 + (row - 1) * 30;
            fields.Add(T($"employment.{row}.person", $"Employment {row}: employed person", 9, 35, y, 142));
            fields.Add(T($"employment.{row}.employer", $"Employment {row}: employer", 9, 190, y, 225));
            fields.Add(T($"employment.{row}.hours", $"Employment {row}: hours per week", 9, 427, y, 98, 20));
            fields.Add(T($"employment.{row}.frequency", $"Employment {row}: pay frequency", 9, 537, y, 125));
            fields.Add(T($"employment.{row}.wages", $"Employment {row}: wages before taxes", 9, 686, y, 122, 30));
        }
        fields.AddRange([
            Y("employment.leftJob", "Left a job in last 60 days", 9, 588, 642, 385),
            T("employment.leftWho", "Who left the job", 9, 154, 412, 160),
            T("employment.leftReason", "Reason for leaving", 9, 401, 412, 218),
            T("employment.lastPaid", "Last paid date", 9, 755, 412, 52, 20),
            Y("employment.strike", "Household member on strike", 9, 478, 531, 439),
            T("employment.strikeWho", "Who is on strike", 9, 670, 439, 140),
            T("selfEmployment.person", "Self-employed person", 9, 280, 598, 138),
            T("selfEmployment.work", "Type of self-employment", 9, 510, 598, 295),
            T("selfEmployment.net", "Net self-employment income this month", 9, 568, 640, 235, 30),
            T("selfEmployment.business", "Business name", 9, 170, 701, 260),
            T("selfEmployment.hours", "Average hours per week", 9, 634, 701, 170, 20),
            Y("selfEmployment.taxReturn", "Business filed taxes", 9, 221, 274, 729),
            T("selfEmployment.taxYear", "Most recent business tax year", 9, 719, 729, 87, 10),
            Y("selfEmployment.changed", "Significant change in business income or expenses", 9, 447, 503, 751),
        ]);
        for (var row = 1; row <= 5; row++)
        {
            var y = 928 + (row - 1) * 28;
            fields.Add(T($"otherIncome.{row}.person", $"Other income {row}: person", 9, 37, y, 217));
            fields.Add(T($"otherIncome.{row}.source", $"Other income {row}: source", 9, 266, y, 242));
            fields.Add(T($"otherIncome.{row}.amount", $"Other income {row}: amount", 9, 535, y, 105, 30));
            fields.Add(T($"otherIncome.{row}.frequency", $"Other income {row}: frequency", 9, 677, y, 130));
        }
    }

    private static void AddAssets(List<BenefitsAnswerField> fields)
    {
        fields.AddRange([
            Y("income.expectedChange", "Expected income change", 10, 273, 326, 57),
            T("income.changeExplanation", "Expected income change explanation", 10, 429, 57, 375, 160),
            Y("income.assistance", "Outside money or assistance", 10, 669, 721, 82),
            Y("income.lumpSum", "Recent or expected lump sum", 10, 40, 93, 152),
            T("income.lumpSumExplanation", "Lump sum explanation", 10, 257, 152, 550, 160)
        ]);
        for (var row = 1; row <= 5; row++)
        {
            var y = 349 + (row - 1) * 28;
            fields.Add(T($"asset.{row}.owners", $"Asset {row}: owners", 10, 37, y, 180));
            fields.Add(T($"asset.{row}.type", $"Asset {row}: type", 10, 235, y, 164));
            fields.Add(T($"asset.{row}.institution", $"Asset {row}: institution", 10, 421, y, 243));
            fields.Add(T($"asset.{row}.value", $"Asset {row}: current value", 10, 691, y, 117, 30));
        }
        for (var row = 1; row <= 4; row++)
        {
            var y = 611 + (row - 1) * 28;
            fields.Add(T($"vehicle.{row}.owners", $"Vehicle {row}: owners", 10, 37, y, 169));
            fields.Add(T($"vehicle.{row}.type", $"Vehicle {row}: type", 10, 223, y, 176));
            fields.Add(T($"vehicle.{row}.year", $"Vehicle {row}: year", 10, 421, y, 47, 4));
            fields.Add(T($"vehicle.{row}.model", $"Vehicle {row}: make and model", 10, 501, y, 197));
            fields.Add(T($"vehicle.{row}.owed", $"Vehicle {row}: amount owed", 10, 725, y, 81, 30));
        }
        for (var row = 1; row <= 3; row++)
        {
            var y = 814 + (row - 1) * 27;
            fields.Add(T($"property.{row}.owners", $"Property {row}: owners", 10, 37, y, 167));
            fields.Add(T($"property.{row}.type", $"Property {row}: type", 10, 220, y, 153));
            fields.Add(T($"property.{row}.address", $"Property {row}: address", 10, 389, y, 289));
            fields.Add(T($"property.{row}.owed", $"Property {row}: amount owed", 10, 711, y, 95, 30));
        }
    }
    private static void AddExpenses(List<BenefitsAnswerField> fields)
    {
        var expenses = new[]
        {
            ("rent", "Rent", 68d), ("heat", "Heat", 97d),
            ("airConditioning", "Air conditioning", 124d),
            ("electricity", "Other electricity", 152d),
            ("telephone", "Basic telephone", 179d),
            ("water", "Water or sewer", 207d)
        };
        var otherExpenses = new[]
        {
            ("lotRent", "Lot rent", 68d), ("mortgage", "Mortgage", 97d),
            ("propertyTaxes", "Property taxes", 124d),
            ("homeInsurance", "House insurance", 152d),
            ("cookingFuel", "Cooking fuel", 179d),
            ("trash", "Trash collection", 207d)
        };
        foreach (var (key, label, y) in expenses)
        {
            fields.Add(T($"expense.{key}.amount", $"{label}: amount", 11, 208, y, 100, 30));
            fields.Add(T($"expense.{key}.frequency", $"{label}: frequency", 11, 325, y, 108));
        }
        foreach (var (key, label, y) in otherExpenses)
        {
            fields.Add(T($"expense.{key}.amount", $"{label}: amount", 11, 583, y, 98, 30));
            fields.Add(T($"expense.{key}.frequency", $"{label}: frequency", 11, 706, y, 97));
        }
        fields.AddRange([
            Y("expense.heatInRent", "Heating cost included in rent", 11, 306, 361, 233),
            Y("expense.mortgageIncludesTax", "Mortgage includes tax and insurance", 11, 414, 466, 258),
            Y("expense.generalAssistance", "General Assistance paid shelter or utility cost", 11, 610, 663, 281),
            Y("expense.rentSubsidy", "Receives a rent subsidy", 11, 235, 291, 315),
            T("expense.rentSubsidyAmount", "Rent subsidy amount", 11, 527, 315, 102, 30),
            T("expense.rentSubsidyFrequency", "Rent subsidy frequency", 11, 692, 315, 114),
            Y("expense.outsidePayer", "Someone outside household pays expenses", 11, 552, 607, 340),
            T("expense.outsidePayerDetails", "Who pays which bills", 11, 322, 365, 483, 160),
            Y("expense.heap", "Received more than $20 HEAP in last year", 11, 637, 689, 386),
            T("expense.heapDate", "Last HEAP receipt date", 11, 260, 412, 542, 20),
            Y("expense.childSupport", "Pays child support", 11, 246, 301, 439),
            T("expense.childSupportWho", "Who pays child support", 11, 441, 439, 135),
            Y("expense.childSupportOrdered", "Child support is court ordered", 11, 717, 773, 439),
            T("expense.childSupportAmount", "Child support amount", 11, 143, 466, 92, 30),
            T("expense.childSupportFrequency", "Child support frequency", 11, 355, 466, 112),
            T("expense.childSupportFor", "Child support for whom", 11, 569, 466, 234),
            Y("expense.medicalOver35", "Over $35 per month in eligible medical expenses", 11, 672, 725, 490),
            T("expense.medicalWho", "Who has medical expenses", 11, 145, 515, 660),
            Y("care.paid", "Pays child or dependent care", 11, 484, 536, 579),
            T("care.who", "Who pays dependent care", 11, 133, 609, 213),
            T("care.amount", "Dependent care amount", 11, 466, 609, 90, 30),
            T("care.frequency", "Dependent care frequency", 11, 688, 609, 116),
            T("care.provider", "Care provider name", 11, 168, 639, 330),
            T("care.providerType", "Care provider type", 11, 642, 639, 164),
            T("care.address", "Care provider address", 11, 106, 664, 389),
            T("care.phone", "Care provider phone", 11, 658, 664, 146),
            Y("tax.willFile", "Will file federal tax return next year", 11, 599, 931, 930)
        ]);
        for (var row = 1; row <= 3; row++)
        {
            var y = 795 + (row - 1) * 27;
            fields.Add(T($"deduction.{row}.payer", $"Deduction {row}: payer", 11, 38, y, 180));
            fields.Add(T($"deduction.{row}.type", $"Deduction {row}: type", 11, 236, y, 214));
            fields.Add(T($"deduction.{row}.frequency", $"Deduction {row}: frequency", 11, 469, y, 140));
            fields.Add(T($"deduction.{row}.amount", $"Deduction {row}: amount", 11, 648, y, 154, 30));
        }
        for (var row = 1; row <= 2; row++)
        {
            var y = 984 + (row - 1) * 27;
            fields.Add(T($"tax.filer.{row}.name", $"Tax filer {row}", 11, 37, y, 376));
            fields.Add(T($"tax.filer.{row}.spouse", $"Tax filer {row}: joint spouse", 11, 432, y, 372));
        }
    }

    private static void AddTaxAndInsurance(List<BenefitsAnswerField> fields)
    {
        fields.Add(Y("tax.claimDependents", "Will claim dependents", 12, 597, 649, 67));
        for (var row = 1; row <= 4; row++)
        {
            var y = 117 + (row - 1) * 27;
            fields.Add(T($"tax.dependent.{row}.filer", $"Dependent row {row}: tax filer", 12, 37, y, 380));
            fields.Add(T($"tax.dependent.{row}.names", $"Dependent row {row}: dependents", 12, 433, y, 370));
        }
        fields.Add(Y("tax.externalDependent", "Claimed by someone outside household", 12, 150, 204, 263));
        for (var row = 1; row <= 2; row++)
        {
            var y = 336 + (row - 1) * 28;
            fields.Add(T($"tax.external.{row}.dependent", $"External claim {row}: dependent", 12, 37, y, 245));
            fields.Add(T($"tax.external.{row}.filer", $"External claim {row}: filer", 12, 305, y, 246));
            fields.Add(T($"tax.external.{row}.relationship", $"External claim {row}: relationship", 12, 572, y, 231));
        }
        fields.AddRange([
            T("insurance.holder", "Insurance policy holder", 12, 181, 452, 256),
            T("insurance.holderId", "Policy holder SSN or birth date", 12, 639, 452, 169, 30, true),
            T("insurance.company", "Health insurance company", 12, 274, 480, 236),
            T("insurance.policy", "Policy number", 12, 629, 480, 177, 40, true),
            T("insurance.start", "Coverage start date", 12, 170, 504, 250, 20),
            T("insurance.end", "Coverage end date", 12, 604, 504, 200, 20),
            C("insurance.employer", "Employer coverage", 12, 165, 527),
            C("insurance.private", "Private coverage", 12, 257, 527),
            C("insurance.longTerm", "Long term care coverage", 12, 335, 527),
            C("insurance.dental", "Dental coverage", 12, 468, 527),
            C("insurance.vision", "Vision coverage", 12, 538, 527),
            C("insurance.prescription", "Prescription coverage", 12, 614, 527),
            C("insurance.other", "Other insurance", 12, 722, 527),
            T("insurance.members", "Members covered by the plan", 12, 372, 559, 435, 200),
            Y("insurance.childLost", "Child lost health insurance in past three months", 12, 394, 446, 606),
            T("insurance.childLostWho", "Child who lost health insurance", 12, 579, 606, 228),
            C("support.risk", "Seeking other parent support would put family at risk", 12, 534, 938)
        ]);
        for (var row = 1; row <= 2; row++)
        {
            var y = 740 + (row - 1) * 27;
            fields.Add(T($"medicare.{row}.name", $"Medicare {row}: name", 12, 37, y, 181));
            fields.Add(T($"medicare.{row}.number", $"Medicare {row}: Medicare or Railroad number", 12, 238, y, 224, 30, true));
            fields.Add(T($"medicare.{row}.partA", $"Medicare {row}: Part A start", 12, 482, y, 146, 20));
            fields.Add(T($"medicare.{row}.partB", $"Medicare {row}: Part B start", 12, 661, y, 141, 20));
        }
    }

    private static void AddTanfAndEmergency(List<BenefitsAnswerField> fields)
    {
        for (var row = 1; row <= 4; row++)
        {
            var y = 127 + (row - 1) * 27;
            fields.Add(T($"tanf.parent.{row}.children", $"Other parent {row}: children", 13, 37, y, 182));
            fields.Add(T($"tanf.parent.{row}.name", $"Other parent {row}: name", 13, 236, y, 176));
            fields.Add(T($"tanf.parent.{row}.ssn", $"Other parent {row}: SSN", 13, 432, y, 180, 11, true));
            fields.Add(T($"tanf.parent.{row}.birthDate", $"Other parent {row}: birth date", 13, 629, y, 177, 20));
        }
        fields.Add(Y("tanf.otherState", "TANF received from another state", 13, 490, 654, 243));
        for (var row = 1; row <= 3; row++)
        {
            var y = 307 + (row - 1) * 28;
            fields.Add(T($"tanf.state.{row}.person", $"Other-state TANF {row}: person", 13, 37, y, 231));
            fields.Add(T($"tanf.state.{row}.state", $"Other-state TANF {row}: state", 13, 288, y, 126));
            fields.Add(T($"tanf.state.{row}.start", $"Other-state TANF {row}: start date", 13, 432, y, 106, 20));
            fields.Add(T($"tanf.state.{row}.end", $"Other-state TANF {row}: end date", 13, 551, y, 105, 20));
            fields.Add(T($"tanf.state.{row}.months", $"Other-state TANF {row}: months", 13, 671, y, 131, 10));
        }
        fields.AddRange([
            T("tanf.payee.name", "Minor parent payee name", 13, 38, 484, 183),
            T("tanf.payee.relationship", "Minor parent payee relationship", 13, 236, 484, 131),
            T("tanf.payee.address", "Minor parent payee address", 13, 386, 484, 267),
            T("tanf.payee.phone", "Minor parent payee phone", 13, 673, 484, 131),
            C("emergency.disaster", "Emergency assistance: disaster", 13, 40, 646),
            C("emergency.eviction", "Emergency assistance: eviction", 13, 40, 667),
            C("emergency.repair", "Emergency assistance: home repair or replacement", 13, 40, 713),
            C("emergency.utility", "Emergency assistance: utility shutoff", 13, 40, 732),
            C("emergency.equipment", "Emergency assistance: disability equipment", 13, 40, 750),
            T("emergency.explanation", "Why emergency assistance is needed", 13, 568, 777, 240, 700),
            Y("emergency.refusedWork", "Adult refused employment or training", 13, 510, 564, 889)
        ]);
    }

    private static void AddTribal(List<BenefitsAnswerField> fields)
    {
        fields.Add(Y("tribal.citizen", "Citizen of a federally recognized tribe", 14, 409, 461, 157));
        for (var row = 1; row <= 5; row++)
        {
            var y = 190 + (row - 1) * 28;
            fields.Add(T($"tribal.member.{row}.name", $"Tribal member {row}: name", 14, 38, y, 379));
            fields.Add(T($"tribal.member.{row}.tribe", $"Tribal member {row}: tribe", 14, 432, y, 370));
        }
        fields.AddRange([
            Y("tribal.eligibleServices", "Eligible for Indian Health Service or tribal program", 14, 197, 249, 357),
            T("tribal.eligibleWho", "Who is eligible for tribal health services", 14, 146, 384, 658),
            Y("tribal.receivedServices", "Received tribal health services", 14, 501, 554, 432),
            T("tribal.receivedWho", "Who received tribal health services", 14, 144, 460, 658),
            Y("tribal.tanfLand", "TANF household member lives on tribal land", 14, 491, 543, 765)
        ]);
        for (var row = 1; row <= 5; row++)
        {
            var y = 622 + (row - 1) * 27;
            fields.Add(T($"tribal.income.{row}.person", $"Tribal income {row}: person", 14, 37, y, 319));
            fields.Add(T($"tribal.income.{row}.amount", $"Tribal income {row}: amount", 14, 387, y, 155, 30));
            fields.Add(T($"tribal.income.{row}.frequency", $"Tribal income {row}: frequency", 14, 566, y, 236));
        }
    }

    private static void AddRepresentative(List<BenefitsAnswerField> fields)
    {
        fields.AddRange([
            C("appendixB.authorized", "Complete authorized representative appendix", 15, 0, 0),
            T("representative.name", "Authorized representative name", 15, 267, 239, 536),
            T("representative.address", "Authorized representative address", 15, 103, 264, 700),
            T("representative.phone", "Authorized representative telephone", 15, 169, 287, 213),
            T("representative.email", "Authorized representative email", 15, 502, 287, 303),
            C("representative.guardianship", "Representative legal authority: guardianship", 15, 40, 344),
            C("representative.poa", "Representative legal authority: power of attorney", 15, 301, 344),
            C("representative.directive", "Representative legal authority: advance directive", 15, 565, 344),
            C("representative.otherAuthority", "Representative legal authority: other", 15, 40, 366),
            T("representative.otherAuthorityText", "Other representative authority", 15, 152, 366, 650),
            C("representative.signApplication", "Representative may sign and submit application", 15, 40, 415),
            C("representative.signReview", "Representative may sign recertification", 15, 40, 437),
            C("representative.receiveNotices", "Representative may receive notices", 15, 40, 457),
            C("representative.obtainSnap", "Representative may obtain SNAP benefits", 15, 40, 480),
            C("representative.hearing", "Representative may attend fair hearings", 15, 40, 500),
            C("representative.otherTask", "Representative may perform other task", 15, 40, 522),
            T("representative.otherTaskText", "Other representative task", 15, 228, 522, 574),
            C("representative.allOther", "Representative may act in all other DHHS matters", 15, 40, 542)
        ]);
    }
}
