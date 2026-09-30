using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.ClientDocuments;
using Xunit;

namespace Sati.Tests;

public sealed class BenefitsApplicationViewModelTests
{
    [Fact]
    public async Task A_second_editor_must_reload_after_a_conflicting_save()
    {
        var drafts = new TestFormWizardProgressService();
        var person = PersonFor(23, "Example");
        var first = new BenefitsApplicationViewModel(new RecordingService(), drafts);
        var second = new BenefitsApplicationViewModel(new RecordingService(), drafts);
        first.SetPerson(person);
        second.SetPerson(person);
        Answer(first, "person2.name").Value = "First session";
        await first.Progress.SaveCommand.ExecuteAsync(null);
        Answer(second, "person2.name").Value = "Stale session";
        await second.Progress.SaveCommand.ExecuteAsync(null);
        Assert.True(second.Progress.MustResume);
        Assert.False(second.Progress.CanSave);

        second.Progress.ReloadCommand.Execute(null);
        Assert.False(second.Progress.MustResume);
        Assert.Equal("First session", Answer(second, "person2.name").Value);
    }

    [Fact]
    public async Task Saved_answers_and_step_resume_for_the_same_consumer()
    {
        var drafts = new TestFormWizardProgressService();
        var person = PersonFor(11, "Example");
        var first = new BenefitsApplicationViewModel(new RecordingService(), drafts);
        first.SetPerson(person);
        Answer(first, "program.snap").IsChecked = true;
        Answer(first, "person2.name").Value = "Household member";
        first.NextCommand.Execute(null);
        first.NextCommand.Execute(null);
        await first.Progress.SaveCommand.ExecuteAsync(null);
        Assert.Contains("Progress saved", first.Progress.Status);
        Answer(first, "person2.name").Value = "Updated household member";
        Assert.True(first.Progress.HasUnsavedChanges);
        Answer(first, "person2.name").Value = "Household member";
        Assert.False(first.Progress.HasUnsavedChanges);

        var resumed = new BenefitsApplicationViewModel(new RecordingService(), drafts);
        resumed.SetPerson(person);
        Assert.True(Answer(resumed, "program.snap").IsChecked);
        Assert.Equal("Household member", Answer(resumed, "person2.name").Value);
        Assert.StartsWith("Step 3 of", resumed.StepProgress);

        resumed.SetPerson(PersonFor(22, "Other"));
        Assert.False(Answer(resumed, "program.snap").IsChecked);
        Assert.StartsWith("Step 1 of", resumed.StepProgress);
    }

    [Fact]
    public async Task Wizard_sends_only_current_consumers_answers_and_resets_on_selection_change()
    {
        var service = new RecordingService();
        var viewModel = new BenefitsApplicationViewModel(service, new TestFormWizardProgressService());
        viewModel.SetPerson(PersonFor(11, "First"));
        Answer(viewModel, "program.snap").IsChecked = true;
        Answer(viewModel, "person2.name").Value = "First household member";
        await viewModel.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(11, service.PersonId);
        Assert.Equal("True", service.Request!.Answers["program.snap"]);
        Assert.Equal("First household member", service.Request.Answers["person2.name"]);
        Assert.DoesNotContain("person1.name", service.Request.Answers.Keys);
        Assert.DoesNotContain("person1.ssn", service.Request.Answers.Keys);

        viewModel.SetPerson(PersonFor(22, "Second"));
        Assert.False(Answer(viewModel, "program.snap").IsChecked);
        Assert.Equal(string.Empty, Answer(viewModel, "person2.name").Value);
        Assert.Equal("Second", viewModel.PersonName.Split(' ')[^1]);
    }

    [Fact]
    public void Mutually_exclusive_choices_clear_the_other_checkboxes()
    {
        var viewModel = new BenefitsApplicationViewModel(new RecordingService(), new TestFormWizardProgressService());
        viewModel.SetPerson(PersonFor(11, "Example"));
        Answer(viewModel, "person1.male").IsChecked = true;
        Answer(viewModel, "person1.female").IsChecked = true;
        Assert.False(Answer(viewModel, "person1.male").IsChecked);
        Assert.True(Answer(viewModel, "person1.female").IsChecked);
    }

    [Fact]
    public async Task Switching_consumers_discards_a_late_pdf_result_and_unblocks_the_new_selection()
    {
        var service = new DelayedService();
        var viewModel = new BenefitsApplicationViewModel(service, new TestFormWizardProgressService());
        viewModel.SetPerson(PersonFor(11, "First"));
        Answer(viewModel, "program.snap").IsChecked = true;
        var readyCount = 0;
        viewModel.PdfReady += (_, _) => readyCount++;
        var generation = viewModel.GenerateCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsBusy);

        viewModel.SetPerson(PersonFor(22, "Second"));
        Assert.True(viewModel.CanGenerate);
        service.Complete();
        await generation;
        Assert.Equal(0, readyCount);
        Assert.Equal(string.Empty, viewModel.StatusMessage);
    }

    private static BenefitsAnswerViewModel Answer(BenefitsApplicationViewModel viewModel,
        string key) => viewModel.Fields.Single(item => item.Definition.Key == key);

    private static Person PersonFor(int id, string lastName)
    {
        var person = Person.CreatePerson(1, "Test", lastName, string.Empty,
            new DateTime(1980, 1, 1), DateTime.Today.AddYears(-1),
            WaiverType.Section21, new Settings());
        typeof(Person).GetProperty(nameof(Person.Id))!.SetValue(person, id);
        person.AgencyId = 1;
        return person;
    }

    private sealed class RecordingService : IBenefitsApplicationService
    {
        public int PersonId { get; private set; }
        public BenefitsApplicationRequest? Request { get; private set; }

        public Task<BenefitsApplicationResult> GenerateAsync(int personId,
            BenefitsApplicationRequest request, CancellationToken cancellationToken = default)
        {
            PersonId = personId;
            Request = request;
            return Task.FromResult(new BenefitsApplicationResult([1, 2, 3], "draft.pdf", []));
        }
    }

    private sealed class DelayedService : IBenefitsApplicationService
    {
        private readonly TaskCompletionSource<BenefitsApplicationResult> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<BenefitsApplicationResult> GenerateAsync(int personId,
            BenefitsApplicationRequest request, CancellationToken cancellationToken = default) =>
            completion.Task;

        public void Complete() => completion.SetResult(
            new BenefitsApplicationResult([1, 2, 3], "draft.pdf", []));
    }
}
