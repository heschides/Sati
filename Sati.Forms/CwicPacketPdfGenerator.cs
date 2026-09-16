using System.Globalization;
using System.Reflection;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Sati.Contracts.V1;

namespace Sati.Forms;

/// <summary>
/// Places profile facts and explicit referral answers over MaineHealth's published
/// ten-page Benefits Counseling Services packet. The source page artwork is copied
/// without redrawing its legal text, logos, revision labels, or fixed selections.
/// </summary>
public sealed class CwicPacketPdfGenerator
{
    private const string BlankResource = "Sati.Forms.BCS-Referral-Packet-2020.pdf";
    private static readonly XFont FieldFont = new("Arial", 8, XFontStyleEx.Regular);
    private static readonly XFont SmallFieldFont = new("Arial", 7, XFontStyleEx.Regular);
    private static readonly XPen MarkPen = new(XColors.Black, 1.15);

    public byte[] Generate(CwicPacketSubject subject, CwicPacketRequest request, DateTime generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(request);
        var errors = CwicPacketRules.Validate(request);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors.SelectMany(entry => entry.Value)), nameof(request));

        using var source = OpenBlank();
        using var background = XPdfForm.FromStream(source);
        var document = new PdfDocument();
        document.Info.Title = $"CWIC referral packet - {subject.FullName}";
        document.Info.Subject = CwicPacketRules.SourceRevision;
        document.Info.Author = "Sati";
        document.Info.CreationDate = DateTime.SpecifyKind(generatedAtUtc, DateTimeKind.Utc);
        document.Info.ModificationDate = document.Info.CreationDate;

        for (var pageNumber = 1; pageNumber <= background.PageCount; pageNumber++)
        {
            background.PageNumber = pageNumber;
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(612);
            page.Height = XUnit.FromPoint(792);
            using var graphics = XGraphics.FromPdfPage(page);
            graphics.DrawImage(background, 0, 0, page.Width.Point, page.Height.Point);
            DrawPage(graphics, pageNumber, subject, request, generatedAtUtc);
        }

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static void DrawPage(
        XGraphics graphics,
        int pageNumber,
        CwicPacketSubject subject,
        CwicPacketRequest request,
        DateTime generatedAtUtc)
    {
        switch (pageNumber)
        {
            case 1: DrawReferralPageOne(graphics, subject, request, generatedAtUtc); break;
            case 2: DrawReferralPageTwo(graphics, request); break;
            case 3: Text(graphics, subject.FullName, 113, 661, 300); break;
            case 4:
            case 6: DrawSsaRelease(graphics, subject, request); break;
            case 8: DrawDhhsRelease(graphics, subject, request); break;
            case 10: DrawDolRelease(graphics, subject, request); break;
        }
    }

    private static void DrawReferralPageOne(
        XGraphics graphics,
        CwicPacketSubject subject,
        CwicPacketRequest request,
        DateTime generatedAtUtc)
    {
        Text(graphics, subject.FullName, 121, 218, 300);
        Text(graphics, AgeOn(subject.BirthDate, generatedAtUtc.ToLocalTime().Date).ToString(CultureInfo.InvariantCulture), 459, 218, 82);
        Text(graphics, request.MailingAddress, 149, 243, 415);
        Text(graphics, request.City, 86, 268, 180);
        Text(graphics, request.Zip, 299, 268, 92);
        Text(graphics, request.County, 443, 268, 120);
        Text(graphics, request.HomePhone, 130, 293, 180);
        Text(graphics, request.CellPhone, 365, 293, 198);
        Text(graphics, request.Email, 244, 318, 319);
        CheckChoice(graphics, request.MaritalStatus, new Dictionary<string, (double X, double Y)>
        {
            ["Single"] = (140, 344), ["Widowed"] = (190, 344), ["Married"] = (258, 344)
        });
        Text(graphics, request.Gender, 412, 343, 151);
        CheckBoolean(graphics, request.SpouseReceivesDisabilityBenefits, (326, 369), (363, 369));
        Text(graphics, request.ReferringOrganization, 305, 416, 255);
        Mark(graphics, request.ScheduleWithConsumer ? 423 : 459, 441);
        if (!request.ScheduleWithConsumer)
        {
            Text(graphics, Join(request.SchedulingContactName, request.SchedulingContactRelationship), 162, 458, 246);
            Text(graphics, request.SchedulingContactPhone, 417, 458, 143);
        }
        CheckChoices(graphics, request.MeetingMethods, new Dictionary<string, (double X, double Y)>
        {
            ["Phone/Mail"] = (78, 495), ["Virtual (Zoom)"] = (186, 495), ["In-person"] = (294, 495)
        });
        CheckBoolean(graphics, request.HasRepresentativePayee, (83, 533), (115, 533));
        if (request.HasRepresentativePayee == true)
            Text(graphics, Join(request.RepresentativePayeeName, request.RepresentativePayeePhone), 332, 530, 228);
        CheckBoolean(graphics, request.HasLegalGuardian, (83, 570), (115, 570));
        if (request.HasLegalGuardian == true)
            Text(graphics, Join(request.GuardianName, request.GuardianPhone), 332, 567, 228);
        Text(graphics, request.GuardianCommunicationPermission, 44, 613, 208);

        var employment = new Dictionary<string, (double X, double Y)>
        {
            ["Thinking about work"] = (83, 678), ["Applied or interviewed"] = (83, 693),
            ["Self-employed"] = (83, 708), ["Working"] = (83, 724), ["Job offer"] = (83, 739)
        };
        CheckChoices(graphics, request.EmploymentSituations, employment);
        Text(graphics, request.SelfEmploymentMonthlyProfit, 282, 704, 45);
        Text(graphics, request.SelfEmploymentHoursPerMonth, 477, 704, 57);
        Text(graphics, request.WorkingHoursPerWeek, 164, 720, 57);
        Text(graphics, request.WorkingHourlyWage, 334, 720, 45);
        Text(graphics, FormatDate(request.WorkBeganOn), 451, 714, 72, SmallFieldFont);
        Text(graphics, request.OfferedHoursPerWeek, 177, 735, 47);
        Text(graphics, request.OfferedHourlyWage, 289, 735, 47);
    }

    private static void DrawReferralPageTwo(XGraphics graphics, CwicPacketRequest request)
    {
        CheckChoice(graphics, request.JobSatisfaction, new Dictionary<string, (double X, double Y)>
        {
            ["Very dissatisfied"] = (79, 56), ["Dissatisfied"] = (79, 72), ["Not sure"] = (79, 87),
            ["Satisfied"] = (79, 103), ["Very satisfied"] = (79, 118)
        });
        CheckChoices(graphics, request.Benefits, new Dictionary<string, (double X, double Y)>
        {
            ["SSI"] = (79, 153), ["Title II"] = (79, 169), ["MaineCare"] = (79, 184), ["Medicare"] = (79, 200),
            ["SNAP"] = (323, 153), ["Housing"] = (323, 169), ["Veterans Benefits"] = (323, 184), ["Other"] = (323, 200)
        });
        Text(graphics, request.OtherBenefit, 372, 196, 180);
        CheckBoolean(graphics, request.ChildrenUnder21ReceiveMaineCare, (401, 222), (440, 222));
        Wrapped(graphics, request.BenefitsQuestion, 371, 247, 186, 28, 2);

        CheckChoice(graphics, request.InPersonLocation, LocationMarks);
        CheckChoices(graphics, request.Accommodations, new Dictionary<string, (double X, double Y)>
        {
            ["Sign Language Interpreter"] = (79, 471), ["Foreign Language Interpreter"] = (269, 471),
            ["Large print documents"] = (79, 487), ["Other"] = (269, 487)
        });
        Text(graphics, request.ForeignLanguage, 433, 468, 115);
        Text(graphics, request.OtherAccommodation, 397, 484, 154);
        CheckBoolean(graphics, request.HasVrCounselor, (79, 523), (79, 539));
        Text(graphics, request.VrCounselorName, 128, 584, 190);
        Text(graphics, request.VrCounselorPhone, 409, 584, 151);
        CheckChoice(graphics, request.VrStatus, new Dictionary<string, (double X, double Y)>
        {
            ["In application"] = (142, 605), ["Eligible"] = (228, 605), ["Service"] = (292, 605), ["Employed"] = (363, 605)
        });
        Text(graphics, FormatDate(request.VrStatusDate), 487, 601, 72);
        CheckChoice(graphics, request.EstimatedReturnToWork, new Dictionary<string, (double X, double Y)>
        {
            ["Next two months"] = (177, 630), ["Next six months"] = (278, 630),
            ["Next year or two"] = (377, 630), ["Not sure"] = (476, 630)
        });
        CheckBoolean(graphics, request.HasIpe, (187, 656), (228, 656));
        Wrapped(graphics, request.IpeGoal, 64, 674, 493, 34, 3);
        Text(graphics, request.EstimatedHoursPerWeek, 179, 709, 60);
        Text(graphics, FormatDate(request.IpeDate), 289, 709, 67);
        Text(graphics, FormatDate(request.IpeExpectedEndDate), 437, 709, 120);
    }

    private static void DrawSsaRelease(XGraphics graphics, CwicPacketSubject subject, CwicPacketRequest request)
    {
        Text(graphics, subject.FullName, 20, 99, 190);
        Text(graphics, subject.BirthDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture), 222, 99, 165);
        Text(graphics, subject.SocialSecurityNumber, 406, 99, 185);
        Text(graphics, ComposeAddress(request), 76, 620, 315);
        Text(graphics, request.HomePhone ?? request.CellPhone, 489, 620, 102);
    }

    private static void DrawDhhsRelease(XGraphics graphics, CwicPacketSubject subject, CwicPacketRequest request)
    {
        Text(graphics, subject.FullName, 29, 226, 315);
        Text(graphics, subject.BirthDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture), 354, 226, 90);
        Text(graphics, subject.SocialSecurityNumber, 452, 226, 136);
        Text(graphics, request.MailingAddress, 29, 259, 235);
        Text(graphics, request.City, 271, 259, 86);
        Text(graphics, request.State, 370, 259, 48);
        Text(graphics, request.Zip, 421, 259, 88);
        Text(graphics, request.HomePhone ?? request.CellPhone, 30, 284, 140);
        Text(graphics, request.Email, 290, 284, 298);
    }

    private static void DrawDolRelease(XGraphics graphics, CwicPacketSubject subject, CwicPacketRequest request)
    {
        Text(graphics, subject.FullName, 65, 91, 366);
        Text(graphics, subject.BirthDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture), 477, 91, 78);
        CheckChoice(graphics, request.VrDivision, new Dictionary<string, (double X, double Y)>
        {
            ["Vocational Rehabilitation"] = (252, 119), ["Blind and Visually Impaired"] = (486, 119)
        });
        Text(graphics, request.VrCounselorName, 214, 132, 367);
        Text(graphics, request.VrOfficeAddress, 80, 148, 500);
        Text(graphics, FormatDate(request.DolReleaseStart), 176, 354, 105);
        Text(graphics, FormatDate(request.DolReleaseEnd), 311, 354, 105);
        CheckBoolean(graphics, request.AuthorizeSubstanceUseDisclosure, (34, 556), (34, 540));
        CheckBoolean(graphics, request.AuthorizeMentalHealthDisclosure, (34, 587), (34, 571));
        CheckBoolean(graphics, request.ReviewBeforeRelease, (34, 618), (34, 602));
        CheckBoolean(graphics, request.AuthorizeHivDisclosure, (34, 649), (34, 633));
    }

    private static void Text(XGraphics graphics, string? value, double x, double y, double width, XFont? font = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        graphics.DrawString(value.Trim(), font ?? FieldFont, XBrushes.Black,
            new XRect(x, y, width, 13), XStringFormats.TopLeft);
    }

    private static void Wrapped(XGraphics graphics, string? value, double x, double y, double width, double height, int maximumLines)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        var words = value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = "";
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (graphics.MeasureString(candidate, SmallFieldFont).Width <= width)
            {
                current = candidate;
                continue;
            }
            if (current.Length > 0) lines.Add(current);
            current = word;
            if (lines.Count == maximumLines - 1) break;
        }
        if (current.Length > 0 && lines.Count < maximumLines) lines.Add(current);
        var lineHeight = Math.Min(10, height / Math.Max(1, maximumLines));
        for (var index = 0; index < lines.Count; index++)
            Text(graphics, lines[index], x, y + index * lineHeight, width, SmallFieldFont);
    }

    private static void CheckChoices(
        XGraphics graphics,
        IReadOnlyList<string>? choices,
        IReadOnlyDictionary<string, (double X, double Y)> positions)
    {
        foreach (var choice in choices ?? [])
            if (positions.TryGetValue(choice, out var position)) Mark(graphics, position.X, position.Y);
    }

    private static void CheckChoice(
        XGraphics graphics,
        string? choice,
        IReadOnlyDictionary<string, (double X, double Y)> positions)
    {
        if (choice is not null && positions.TryGetValue(choice, out var position))
            Mark(graphics, position.X, position.Y);
    }

    private static void CheckBoolean(XGraphics graphics, bool? value, (double X, double Y) no, (double X, double Y) yes)
    {
        if (value is null) return;
        var point = value.Value ? yes : no;
        Mark(graphics, point.X, point.Y);
    }

    private static void Mark(XGraphics graphics, double x, double y)
    {
        graphics.DrawLine(MarkPen, x, y, x + 6, y + 6);
        graphics.DrawLine(MarkPen, x + 6, y, x, y + 6);
    }

    private static string? Join(string? first, string? second)
    {
        var values = new[] { first, second }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim());
        var result = string.Join(" — ", values);
        return result.Length == 0 ? null : result;
    }

    private static string? ComposeAddress(CwicPacketRequest request)
    {
        var locality = string.Join(" ", new[] { request.City, request.State, request.Zip }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
        return Join(request.MailingAddress, locality);
    }

    private static string? FormatDate(DateOnly? value) =>
        value?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);

    private static int AgeOn(DateTime birthDate, DateTime onDate)
    {
        var age = onDate.Year - birthDate.Year;
        if (birthDate.Date > onDate.Date.AddYears(-age)) age--;
        return Math.Max(0, age);
    }

    private static Stream OpenBlank() =>
        typeof(CwicPacketPdfGenerator).GetTypeInfo().Assembly.GetManifestResourceStream(BlankResource)
        ?? throw new InvalidOperationException($"Embedded CWIC packet '{BlankResource}' is missing.");

    private static readonly IReadOnlyDictionary<string, (double X, double Y)> LocationMarks =
        new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal)
        {
            ["Bangor"] = (189, 316), ["Dover-Foxcroft"] = (189, 331), ["Machias"] = (189, 347),
            ["Calais"] = (296, 316), ["Fort Kent"] = (296, 331), ["Millinocket"] = (296, 347),
            ["Caribou"] = (394, 316), ["Houlton"] = (394, 331), ["Newport"] = (394, 347),
            ["Ellsworth"] = (489, 316), ["Lincoln"] = (489, 331), ["Presque Isle"] = (489, 347),
            ["Augusta"] = (189, 362), ["Rockland"] = (189, 378), ["Belfast"] = (296, 362),
            ["Skowhegan"] = (296, 378), ["Boothbay"] = (394, 362), ["Topsham"] = (394, 378),
            ["Brunswick"] = (489, 362), ["Waterville"] = (489, 378), ["Bridgton"] = (189, 394),
            ["Wilton"] = (189, 409), ["Lewiston"] = (296, 394), ["Rumford"] = (394, 394),
            ["South Paris"] = (489, 394), ["Biddeford"] = (189, 425), ["Portland"] = (296, 425),
            ["Sanford"] = (394, 425)
        };
}
