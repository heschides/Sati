using Sati.Models;
using Sati.ViewModels;
using Xunit;

namespace Sati.Tests;

public sealed class ComplianceReviewOccurrenceDateTests
{
    [Fact]
    public void CheckingANewObligationNeverInfersTheDeadlineAsCompletion()
    {
        var due = new DateTime(2027, 3, 7);
        var form = new Form(FormType.PCP, due, targetEffectiveDate: due);
        var row = new ComplianceFormRow(form);

        row.IsCompliant = true;

        Assert.Null(row.CompletedDate);
        Assert.NotNull(row.ValidationError(DateTime.Today));
        Assert.Throws<InvalidOperationException>(row.Commit);
        Assert.Null(form.CompletedDate);
    }

    [Fact]
    public void ExplicitPastOccurrenceDateCanCompleteANewObligationButFutureCannot()
    {
        var target = DateTime.Today.AddMonths(2);
        var form = new Form(FormType.PCP, target, targetEffectiveDate: target);
        var row = new ComplianceFormRow(form)
        {
            IsCompliant = true,
            CompletedDate = DateTime.Today.AddDays(-2)
        };

        Assert.Null(row.ValidationError(DateTime.Today));
        row.Commit();
        Assert.Equal(DateTime.Today.AddDays(-2), form.CompletedDate);

        var future = new ComplianceFormRow(new Form(
            FormType.PCP, target, targetEffectiveDate: target))
        {
            IsCompliant = true,
            CompletedDate = DateTime.Today.AddDays(1)
        };
        Assert.Contains("future", future.ValidationError(DateTime.Today),
            StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(future.Commit);
    }

    [Fact]
    public void PersistedObligationIsReadOnlyInBulkReview()
    {
        var completed = DateTime.Today.AddDays(-3);
        var form = new Form(FormType.PCP, DateTime.Today, completed, DateTime.Today)
        {
            Id = 41
        };
        var row = new ComplianceFormRow(form);

        Assert.False(row.IsEditable);
        row.IsCompliant = false;
        row.Commit();

        Assert.Equal(completed, form.CompletedDate);
    }
}
