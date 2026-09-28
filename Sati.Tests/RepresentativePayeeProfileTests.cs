using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Sati.Contracts.V1;
using Sati.Converters;
using Sati.Data.Cloud;
using Xunit;

namespace Sati.Tests;

public sealed class RepresentativePayeeProfileTests
{
    [Fact]
    public void PayeeRequiresMonthlyIncomeAndExplicitRecurringNeeds()
    {
        var missing = RepresentativePayeeRules.Validate(true, null, null);
        var valid = RepresentativePayeeRules.Validate(
            true,
            943.50m,
            "Rent on the first and a weekly personal-needs check.");

        Assert.Contains("repPayeeMonthlyIncome", missing.Keys);
        Assert.Contains("repPayeeRegularCheckRequestNeeds", missing.Keys);
        Assert.Empty(valid);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("943.501")]
    public void MonthlyIncomeRejectsNonPositiveOrFractionalCentAmounts(string value)
    {
        var amount = decimal.Parse(value, CultureInfo.InvariantCulture);

        var errors = RepresentativePayeeRules.Validate(true, amount, "None");

        Assert.Contains("repPayeeMonthlyIncome", errors.Keys);
    }

    [Fact]
    public void NonPayeeProfileCannotRetainFinancialDetails()
    {
        var errors = RepresentativePayeeRules.Validate(
            false,
            943.50m,
            "This stale value must not survive a No selection.");

        Assert.Contains("representativePayeeDetails", errors.Keys);
    }

    [Fact]
    public void CloudSaveContractCarriesTheCompletePayeeProfile()
    {
        var person = Person.Rehydrate(41, 7);
        person.FirstName = "Profile";
        person.LastName = "Test";
        person.Bio = "Current biography.";
        person.BirthDate = new DateTime(1990, 1, 1);
        person.CaseManagerIsRepPayee = true;
        person.RepPayeeMonthlyIncome = 943.50m;
        person.RepPayeeRegularCheckRequestNeeds = "Rent and weekly spending money.";
        person.Revision = 3;

        var request = CloudContractMapper.ToSavePersonRequest(person);

        Assert.True(request.CaseManagerIsRepPayee);
        Assert.Equal(943.50m, request.RepPayeeMonthlyIncome);
        Assert.Equal("Rent and weekly spending money.", request.RepPayeeRegularCheckRequestNeeds);
        Assert.Equal(3, request.ExpectedRevision);
    }

    [Fact]
    public void MigrationAddsBoundedFinancialProfileColumns()
    {
        var migration = new Migrations.AddRepresentativePayeeProfile();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(Migrations.AddRepresentativePayeeProfile)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var columns = builder.Operations.OfType<AddColumnOperation>().ToList();
        Assert.Contains(columns, column =>
            column.Name == "CaseManagerIsRepPayee" && column.ClrType == typeof(bool));
        Assert.Contains(columns, column =>
            column.Name == "RepPayeeMonthlyIncome" && column.ColumnType == "decimal(18,2)");
        Assert.Contains(columns, column =>
            column.Name == "RepPayeeRegularCheckRequestNeeds" && column.MaxLength == 2_000);
    }

    [Fact]
    public void ProfileUsesAccessibleYesNoAndConditionalDetailControls()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "ClientsView.xaml"));

        Assert.Contains("Case manager is representative payee, yes", xaml, StringComparison.Ordinal);
        Assert.Contains("Case manager is representative payee, no", xaml, StringComparison.Ordinal);
        Assert.Contains("RepPayeeMonthlyIncomeText", xaml, StringComparison.Ordinal);
        Assert.Contains("RepPayeeRegularCheckRequestNeeds", xaml, StringComparison.Ordinal);
        Assert.Contains("It does not request or authorize a check", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxLength=\"2000\"", xaml, StringComparison.Ordinal);
        Assert.Equal(2, xaml.Split("Converter={StaticResource BooleanRadioChoiceConverter}").Length - 1);
        Assert.Contains("ConverterParameter=True", xaml, StringComparison.Ordinal);
        Assert.Contains("ConverterParameter=False", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CaseManagerIsRepPayee, Mode=TwoWay, Converter={StaticResource InverseBoolConverter}",
            xaml,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "True", true)]
    [InlineData(true, "False", false)]
    [InlineData(false, "True", false)]
    [InlineData(false, "False", true)]
    public void RadioChoiceConverterDisplaysTheMatchingBoolean(
        bool value,
        string parameter,
        bool expected)
    {
        var converter = new BooleanRadioChoiceConverter();

        var actual = converter.Convert(value, typeof(bool), parameter, CultureInfo.InvariantCulture);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("False", false)]
    public void RadioChoiceConverterWritesOnlyTheButtonThatBecameChecked(
        string parameter,
        bool expected)
    {
        var converter = new BooleanRadioChoiceConverter();

        var checkedValue = converter.ConvertBack(
            true,
            typeof(bool),
            parameter,
            CultureInfo.InvariantCulture);
        var uncheckedValue = converter.ConvertBack(
            false,
            typeof(bool),
            parameter,
            CultureInfo.InvariantCulture);

        Assert.Equal(expected, checkedValue);
        Assert.Same(Binding.DoNothing, uncheckedValue);
    }

    [Fact]
    public void RadioChoiceConverterRejectsAnInvalidChoiceParameter()
    {
        var converter = new BooleanRadioChoiceConverter();

        Assert.Same(
            DependencyProperty.UnsetValue,
            converter.Convert(true, typeof(bool), "invalid", CultureInfo.InvariantCulture));
        Assert.Same(
            Binding.DoNothing,
            converter.ConvertBack(true, typeof(bool), "invalid", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void LongLivedDatabaseRunnerIsGuardedTransactionalAndBacksUpLocalRecords()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "Apply-RepresentativePayeeProfileMigration.ps1"));

        Assert.Contains("20260822210734_AddRepresentativePayeeProfile", script, StringComparison.Ordinal);
        Assert.Contains("SatiDatabaseIdentity", script, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH(N'dbo.People', N'CaseManagerIsRepPayee')", script, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION", script, StringComparison.Ordinal);
        Assert.Contains("BACKUP DATABASE", script, StringComparison.Ordinal);
        Assert.Contains("-and $personCount -gt 0", script, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourcePath = "") =>
        Directory.GetParent(Path.GetDirectoryName(sourcePath)!)!.FullName;
}
