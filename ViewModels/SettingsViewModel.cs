using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private Settings? _settings;

        public SettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            _ = LoadAsync();
        }

        [ObservableProperty] private int abandonedAfterDays;
        [ObservableProperty] private int productivityThreshold;
        [ObservableProperty] private decimal baseIncentive;
        [ObservableProperty] private decimal perUnitIncentive;
        [ObservableProperty] private string visitTemplate = string.Empty;
        [ObservableProperty] private string contactTemplate = string.Empty;
        [ObservableProperty] private string documentationTemplate = string.Empty;

        [ObservableProperty] private bool excludeMonday;
        [ObservableProperty] private bool excludeTuesday;
        [ObservableProperty] private bool excludeWednesday;
        [ObservableProperty] private bool excludeThursday;
        [ObservableProperty] private bool excludeFriday;

        [ObservableProperty] private bool excludeNewYearsDay;
        [ObservableProperty] private bool excludeMLKDay;
        [ObservableProperty] private bool excludePresidentsDay;
        [ObservableProperty] private bool excludeMemorialDay;
        [ObservableProperty] private bool excludeJuneteenth;
        [ObservableProperty] private bool excludeIndependenceDay;
        [ObservableProperty] private bool excludeLaborDay;
        [ObservableProperty] private bool excludeIndigenousPeoplesDay;
        [ObservableProperty] private bool excludeVeteransDay;
        [ObservableProperty] private bool excludeThanksgiving;
        [ObservableProperty] private bool excludeDayAfterThanksgiving;
        [ObservableProperty] private bool excludeChristmas;
        
        [ObservableProperty] private int reviewOpenDaysBefore;
        [ObservableProperty] private int reviewDaysAfterDue;

        [ObservableProperty] private int pcpOpenDaysBefore;
        [ObservableProperty] private int pcpDaysAfterDue;

        [ObservableProperty] private int compAssessmentOpenDaysBefore;
        [ObservableProperty] private int compAssessmentDaysAfterDue;

        [ObservableProperty] private int reclassificationOpenDaysBefore;
        [ObservableProperty] private int reclassificationDaysAfterDue;

        [ObservableProperty] private int safetyPlanOpenDaysBefore;
        [ObservableProperty] private int safetyPlanDaysAfterDue;

        [ObservableProperty] private int privacyPracticesOpenDaysBefore;
        [ObservableProperty] private int privacyPracticesDaysAfterDue;

        [ObservableProperty] private int releaseAgencyOpenDaysBefore;
        [ObservableProperty] private int releaseAgencyDaysAfterDue;
        [ObservableProperty] private int releaseDhhsOpenDaysBefore;
        [ObservableProperty] private int releaseDhhsDaysAfterDue;
        [ObservableProperty] private int releaseMedicalOpenDaysBefore;
        [ObservableProperty] private int releaseMedicalDaysAfterDue;

        private async Task LoadAsync()
        {
            _settings = await _settingsService.LoadAsync();

            AbandonedAfterDays = _settings.AbandonedAfterDays;
            ProductivityThreshold = _settings.ProductivityThreshold;
            BaseIncentive = _settings.BaseIncentive;
            PerUnitIncentive = _settings.PerUnitIncentive;
            VisitTemplate = _settings.VisitTemplate;
            ContactTemplate = _settings.ContactTemplate;
            DocumentationTemplate = _settings.DocumentationTemplate;

            ExcludeMonday = _settings.ExcludeMonday;
            ExcludeTuesday = _settings.ExcludeTuesday;
            ExcludeWednesday = _settings.ExcludeWednesday;
            ExcludeThursday = _settings.ExcludeThursday;
            ExcludeFriday = _settings.ExcludeFriday;

            ExcludeNewYearsDay = _settings.ExcludeNewYearsDay;
            ExcludeMLKDay = _settings.ExcludeMLKDay;
            ExcludePresidentsDay = _settings.ExcludePresidentsDay;
            ExcludeMemorialDay = _settings.ExcludeMemorialDay;
            ExcludeJuneteenth = _settings.ExcludeJuneteenth;
            ExcludeIndependenceDay = _settings.ExcludeIndependenceDay;
            ExcludeLaborDay = _settings.ExcludeLaborDay;
            ExcludeIndigenousPeoplesDay = _settings.ExcludeIndigenousPeoplesDay;
            ExcludeVeteransDay = _settings.ExcludeVeteransDay;
            ExcludeThanksgiving = _settings.ExcludeThanksgiving;
            ExcludeDayAfterThanksgiving = _settings.ExcludeDayAfterThanksgiving;
            ExcludeChristmas = _settings.ExcludeChristmas;
           
            ReviewOpenDaysBefore = _settings.ReviewOpenDaysBefore;
            ReviewDaysAfterDue = _settings.ReviewDaysAfterDue;
            PcpOpenDaysBefore = _settings.PcpOpenDaysBefore;
            PcpDaysAfterDue = _settings.PcpDaysAfterDue;
            CompAssessmentOpenDaysBefore = _settings.CompAssessmentOpenDaysBefore;
            CompAssessmentDaysAfterDue = _settings.CompAssessmentDaysAfterDue;
            ReclassificationOpenDaysBefore = _settings.ReclassificationOpenDaysBefore;
            ReclassificationDaysAfterDue = _settings.ReclassificationDaysAfterDue;
            SafetyPlanOpenDaysBefore = _settings.SafetyPlanOpenDaysBefore;
            SafetyPlanDaysAfterDue = _settings.SafetyPlanDaysAfterDue;
            PrivacyPracticesOpenDaysBefore = _settings.PrivacyPracticesOpenDaysBefore;
            PrivacyPracticesDaysAfterDue = _settings.PrivacyPracticesDaysAfterDue;
            ReleaseAgencyOpenDaysBefore = _settings.ReleaseAgencyOpenDaysBefore;
            ReleaseAgencyDaysAfterDue = _settings.ReleaseAgencyDaysAfterDue;
            ReleaseDhhsOpenDaysBefore = _settings.ReleaseDhhsOpenDaysBefore;
            ReleaseDhhsDaysAfterDue = _settings.ReleaseDhhsDaysAfterDue;
            ReleaseMedicalOpenDaysBefore = _settings.ReleaseMedicalOpenDaysBefore;
            ReleaseMedicalDaysAfterDue = _settings.ReleaseMedicalDaysAfterDue;
        }

        [RelayCommand]
        public async Task SaveSettingsAsync()
        {
            if (_settings is null)
                return;

            _settings.AbandonedAfterDays = AbandonedAfterDays;
            _settings.ProductivityThreshold = ProductivityThreshold;
            _settings.BaseIncentive = BaseIncentive;
            _settings.PerUnitIncentive = PerUnitIncentive;
            _settings.VisitTemplate = VisitTemplate;
            _settings.ContactTemplate = ContactTemplate;
            _settings.DocumentationTemplate = DocumentationTemplate;

            _settings.ExcludeMonday = ExcludeMonday;
            _settings.ExcludeTuesday = ExcludeTuesday;
            _settings.ExcludeWednesday = ExcludeWednesday;
            _settings.ExcludeThursday = ExcludeThursday;
            _settings.ExcludeFriday = ExcludeFriday;

            _settings.ExcludeNewYearsDay = ExcludeNewYearsDay;
            _settings.ExcludeMLKDay = ExcludeMLKDay;
            _settings.ExcludePresidentsDay = ExcludePresidentsDay;
            _settings.ExcludeMemorialDay = ExcludeMemorialDay;
            _settings.ExcludeJuneteenth = ExcludeJuneteenth;
            _settings.ExcludeIndependenceDay = ExcludeIndependenceDay;
            _settings.ExcludeLaborDay = ExcludeLaborDay;
            _settings.ExcludeIndigenousPeoplesDay = ExcludeIndigenousPeoplesDay;
            _settings.ExcludeVeteransDay = ExcludeVeteransDay;
            _settings.ExcludeThanksgiving = ExcludeThanksgiving;
            _settings.ExcludeDayAfterThanksgiving = ExcludeDayAfterThanksgiving;

            _settings.ExcludeChristmas = ExcludeChristmas;

            _settings.ReviewOpenDaysBefore = ReviewOpenDaysBefore;
            _settings.ReviewDaysAfterDue = ReviewDaysAfterDue;
            _settings.PcpOpenDaysBefore = PcpOpenDaysBefore;
            _settings.PcpDaysAfterDue = PcpDaysAfterDue;
            _settings.CompAssessmentOpenDaysBefore = CompAssessmentOpenDaysBefore;
            _settings.CompAssessmentDaysAfterDue = CompAssessmentDaysAfterDue;
            _settings.ReclassificationOpenDaysBefore = ReclassificationOpenDaysBefore;
            _settings.ReclassificationDaysAfterDue = ReclassificationDaysAfterDue;
            _settings.SafetyPlanOpenDaysBefore = SafetyPlanOpenDaysBefore;
            _settings.SafetyPlanDaysAfterDue = SafetyPlanDaysAfterDue;
            _settings.PrivacyPracticesOpenDaysBefore = PrivacyPracticesOpenDaysBefore;
            _settings.PrivacyPracticesDaysAfterDue = PrivacyPracticesDaysAfterDue;
            _settings.ReleaseAgencyOpenDaysBefore = ReleaseAgencyOpenDaysBefore;
            _settings.ReleaseAgencyDaysAfterDue = ReleaseAgencyDaysAfterDue;
            _settings.ReleaseDhhsOpenDaysBefore = ReleaseDhhsOpenDaysBefore;
            _settings.ReleaseDhhsDaysAfterDue = ReleaseDhhsDaysAfterDue;
            _settings.ReleaseMedicalOpenDaysBefore = ReleaseMedicalOpenDaysBefore;
            _settings.ReleaseMedicalDaysAfterDue = ReleaseMedicalDaysAfterDue;

            await _settingsService.SaveAsync(_settings);
        }
    }
}
