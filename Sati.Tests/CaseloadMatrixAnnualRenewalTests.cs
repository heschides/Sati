using Sati.Contracts.V1;
using Sati.Models;
using Sati.ViewModels;
using Xunit;

namespace Sati.Tests;

public sealed class CaseloadMatrixAnnualRenewalTests
{
    private static readonly DateTime CurrentTarget = new(2025, 12, 16);
    private static readonly DateTime RenewalTarget = new(2026, 12, 16);
    private static readonly DateTime Today = new(2026, 9, 21);

    [Fact]
    public void CompletedCurrentAssessmentCannotHideAnOverdueRenewal()
    {
        var person = PersonWith(
            new Form(FormType.ComprehensiveAssessment, new DateTime(2025, 9, 17),
                completedOn: new DateTime(2026, 9, 17), targetEffectiveDate: CurrentTarget),
            new Form(FormType.ComprehensiveAssessment, new DateTime(2026, 9, 17),
                targetEffectiveDate: RenewalTarget));

        var cell = new FormCellViewModel(person, FormType.ComprehensiveAssessment, Today);

        Assert.True(cell.IsRenewal);
        Assert.Equal(FormCellStatus.Overdue, cell.Status);
        Assert.Contains("RENEWAL", cell.CellText);
        Assert.Contains("9/17/26", cell.CellText);
        Assert.Equal(RenewalTarget, cell.Form!.TargetEffectiveDate);
    }

    [Fact]
    public void AnUnfinishedCurrentAssessmentRemainsVisibleBeforeTheRenewal()
    {
        var person = PersonWith(
            new Form(FormType.ComprehensiveAssessment, new DateTime(2025, 9, 17),
                targetEffectiveDate: CurrentTarget),
            new Form(FormType.ComprehensiveAssessment, new DateTime(2026, 9, 17),
                completedOn: new DateTime(2026, 9, 17), targetEffectiveDate: RenewalTarget));

        var cell = new FormCellViewModel(person, FormType.ComprehensiveAssessment, Today);

        Assert.False(cell.IsRenewal);
        Assert.Equal(FormCellStatus.Overdue, cell.Status);
        Assert.Equal(CurrentTarget, cell.Form!.TargetEffectiveDate);
    }

    [Fact]
    public void RenewedAssessmentBecomesCurrentOnItsPlanDate()
    {
        var person = PersonWith(
            new Form(FormType.ComprehensiveAssessment, new DateTime(2025, 9, 17),
                completedOn: new DateTime(2025, 9, 17), targetEffectiveDate: CurrentTarget),
            new Form(FormType.ComprehensiveAssessment, new DateTime(2026, 9, 17),
                completedOn: new DateTime(2026, 9, 17), targetEffectiveDate: RenewalTarget));

        var cell = new FormCellViewModel(person, FormType.ComprehensiveAssessment, RenewalTarget);

        Assert.False(cell.IsRenewal);
        Assert.Equal(FormCellStatus.Complete, cell.Status);
        Assert.Equal(RenewalTarget, cell.Form!.TargetEffectiveDate);
    }

    private static Person PersonWith(params Form[] forms)
    {
        var person = Person.CreatePerson(
            31, "Matrix", "Test", string.Empty, new DateTime(1990, 1, 1),
            CurrentTarget.AddYears(-1), WaiverType.None, new Settings());
        person.Forms.Clear();
        person.Forms.AddRange(forms);
        return person;
    }
}
