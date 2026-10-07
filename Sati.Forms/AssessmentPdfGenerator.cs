using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using Sati.Contracts.V1;
using Sati.Models.Assessments;

namespace Sati.Forms;

public static class AssessmentPdfGenerator
{
    public static byte[] Generate(AssessmentSubmissionDto snapshot, bool approved)
    {
        var answers = AssessmentReviewRules.Parse(snapshot.DocumentJson);
        var document = new Document();
        document.Info.Title = $"Comprehensive Assessment {snapshot.AssessmentId} v{snapshot.AssessmentVersion} review {snapshot.CycleNumber}";
        document.Info.Subject = $"Submission {snapshot.Id}; SHA256 {snapshot.ContentSha256}";
        var normal = document.Styles[StyleNames.Normal]!; normal.Font.Name = "Arial"; normal.Font.Size = 10;
        var section = document.AddSection(); section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.TopMargin = Unit.FromInch(.65); section.PageSetup.BottomMargin = Unit.FromInch(.65);
        section.PageSetup.LeftMargin = Unit.FromInch(.75); section.PageSetup.RightMargin = Unit.FromInch(.75);
        void Heading(string text)
        { var p = section.AddParagraph(text); p.Format.Font.Bold = true; p.Format.SpaceBefore = Unit.FromPoint(9); p.Format.KeepWithNext = true; }
        Heading("Comprehensive Assessment");
        section.AddParagraph($"Consumer: {snapshot.ConsumerName}\nAnnual target: {snapshot.TargetEffectiveDate:yyyy-MM-dd}\n" +
            $"Assessment {snapshot.AssessmentId}, version {snapshot.AssessmentVersion}, review cycle {snapshot.CycleNumber}, submission {snapshot.Id}\n" +
            $"Saved revision {snapshot.DocumentRevision}; completeness rules {snapshot.RulesVersion}\nContent SHA256: {snapshot.ContentSha256}\n" +
            (approved ? "Supervisor approved. Signing and external acceptance are separate." : "SUBMITTED FOR REVIEW — NOT APPROVED"));
        Heading("Contributors");
        foreach (var contributor in answers.Contributors) section.AddParagraph($"{contributor.Name} — {contributor.Relationship}");
        foreach (var group in AssessmentCatalog.Sections)
        {
            Heading(group.Title);
            foreach (var question in group.Questions)
            {
                Heading(AssessmentCatalog.DisplayPrompt(question.Prompt, snapshot.ConsumerName));
                var a = answers.Answers[question.Key];
                section.AddParagraph($"Disposition: {a.Status}\n{a.Narrative}\n{a.ExceptionReason}".Trim());
                if (question.UsesSupports) section.AddParagraph($"Supports: {a.Supports}\n{a.SupportDetails}");
                if (question.Kind is AssessmentQuestionKind.YesNo or AssessmentQuestionKind.HealthConcern or AssessmentQuestionKind.Therapy)
                    section.AddParagraph($"Response: {a.YesNoResponse}; follow-up: {a.FollowUpYesNoResponse}\n{a.Details}");
                if (question.Kind == AssessmentQuestionKind.Therapy)
                    section.AddParagraph($"Format: {a.TherapySessionFormat}; change format: {a.WantsOtherSessionFormat}; " +
                        $"change frequency: {a.WantsFrequencyChange}; direction: {a.TherapyFrequencyDirection}");
                foreach (var activity in a.ActivitySupportLevels)
                    section.AddParagraph($"{activity.Key}: {activity.Value}; skills training: {a.ActivitySkillsTraining.GetValueOrDefault(activity.Key)}");
                if (!string.IsNullOrWhiteSpace(a.DissentingOpinion))
                    section.AddParagraph($"Differing perspective ({a.DissentContributor}): {a.DissentingOpinion}\n" +
                        $"Discussion: {a.DissentDiscussion}\nRemains unresolved: {a.DissentUnresolved}");
            }
        }
        Heading("Identified needs");
        if (answers.Needs.Count == 0) section.AddParagraph(answers.NoIdentifiedNeedsReason);
        foreach (var need in answers.Needs)
            section.AddParagraph($"{need.Type}: {need.Description}\nDesired result: {need.DesiredResult}\n{need.DescribeProvider()}");
        var footer = section.Footers.Primary.AddParagraph($"CONFIDENTIAL | Submission {snapshot.Id} | Page ");
        footer.AddPageField(); footer.Format.Alignment = ParagraphAlignment.Center;
        var renderer = new PdfDocumentRenderer { Document = document }; renderer.RenderDocument();
        renderer.PdfDocument.Info.CreationDate = DateTime.SpecifyKind(snapshot.SubmittedAtUtc, DateTimeKind.Utc);
        using var stream = new MemoryStream(); renderer.PdfDocument.Save(stream, false); return stream.ToArray();
    }
}
