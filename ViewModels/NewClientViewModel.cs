
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;



namespace Sati
{

    public partial class NewClientViewModel : ObservableValidator
    {

        //FIELDS
        private readonly ISessionService _sessionService;
        private readonly IPersonService _personService;

        //EVENTS
        public event Func<List<Form>, bool>? ComplianceReviewRequested;

        //PROPERTIES
        [ObservableProperty][NotifyDataErrorInfo][Required(ErrorMessage = "First name is required.")] private string? firstName;
        [ObservableProperty][NotifyDataErrorInfo][Required(ErrorMessage = "Last name is required.")] private string? lastName;
        [ObservableProperty][NotifyDataErrorInfo][Required(ErrorMessage = "Birthdate is required.")] private DateTime birthDate;
        [ObservableProperty][NotifyDataErrorInfo][Required(ErrorMessage = "A short biographical description is required.")] private string? bio;
        [ObservableProperty] private WaiverType waiver;
        [ObservableProperty][NotifyDataErrorInfo][CustomValidation(typeof(NewClientViewModel), nameof(ValidateEffectiveDate))] private string effectiveDateText = string.Empty;
        [ObservableProperty] private Person? selectedPerson;
        [ObservableProperty] private bool isEditMode;

        //COMPUTED PROPERTIES
        public bool HasWaiver => Waiver != WaiverType.None;
        public string SubmitButtonLabel => IsEditMode ? "Save Changes" : "Add Client";
        public ObservableCollection<Person> People { get; } = [];
        public Array Waivers => Enum.GetValues(typeof(WaiverType));

        //PROPERTY CALLBACKS
        partial void OnWaiverChanged(WaiverType value)
        {
            OnPropertyChanged(nameof(HasWaiver));
            if (value == WaiverType.None)
                EffectiveDateText = string.Empty;
        }
        //constructor
        public NewClientViewModel(IPersonService personService, ISessionService session)
        {
            _personService = personService;
            _sessionService = session;
            _ = LoadPeopleAsync();
        }


        [RelayCommand]
        private async Task Submit()
        {
            ValidateAllProperties();
            if (HasErrors)
                return;

            var effectiveDate = TryGetEffectiveDate(EffectiveDateText)!;
            if (IsEditMode && SelectedPerson is Person existing)
            {
                existing.FirstName = FirstName!;
                existing.LastName = LastName!;
                existing.BirthDate = BirthDate;
                existing.EffectiveDate = TryGetEffectiveDate(EffectiveDateText);
                existing.Waiver = Waiver;
                existing.Bio = Bio!;
                await _personService.EditPersonAsync(existing);

                var index = People.IndexOf(existing);
                if (index >= 0)
                    People[index] = existing;
                IsEditMode = false;
                SelectedPerson = null;
                ClearFields();
                OnPropertyChanged(nameof(SubmitButtonLabel));
            }
            else
            {
                var person = Person.CreatePerson(_sessionService.CurrentUser!.Id, FirstName!, LastName!, Bio!, BirthDate, effectiveDate, Waiver);
                var confirmed = ComplianceReviewRequested?.Invoke(person.Forms) ?? true;
                if (!confirmed)
                    return;
                await _personService.AddPersonAsync(person);
                People.Add(person);
            }
        }

        [RelayCommand]
        private async Task RemoveSelectedPerson()
        {
            if (SelectedPerson is null)
                return;
            await _personService.DeletePersonAsync(SelectedPerson);
            People.Remove(SelectedPerson);
            SelectedPerson = null;
        }



        private async Task LoadPeopleAsync()
        {
            if (_sessionService.CurrentUser is null)
                return;
            var people = await _personService.GetAllPeopleAsync(_sessionService.CurrentUser.Id);
            foreach (var person in people)
                People.Add(person);
        }

        public void LoadPersonForEdit(Person person)
        {
            IsEditMode = true;
            OnPropertyChanged(nameof(SubmitButtonLabel));
            FirstName = person.FirstName;
            LastName = person.LastName;
            Bio = person.Bio;
            BirthDate = person.BirthDate;
            EffectiveDateText = person.EffectiveDate.ToString("MM/dd");
            Waiver = person.Waiver;
        }

        private void ClearFields()
        {
            FirstName = string.Empty;
            LastName = string.Empty;
            BirthDate = default;
            EffectiveDateText = string.Empty;
            Waiver = default;
            Bio = string.Empty;
        }

        //METHODS

        //validation
        public static ValidationResult? ValidateEffectiveDate(string value, ValidationContext context)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new ValidationResult("Effective date is required.");

            if (!DateTime.TryParseExact(value.Trim(), "MM/dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return new ValidationResult("Date must be in MM/DD format.");

            return ValidationResult.Success;
        }

        private static DateTime TryGetEffectiveDate(string input)
        {
            if (!DateTime.TryParseExact(input.Trim(), "MM/dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return DateTime.MinValue;

            var candidate = new DateTime(DateTime.Today.Year, parsed.Month, parsed.Day);
            return candidate > DateTime.Today ? candidate.AddYears(-1) : candidate;
        }

    }
}
