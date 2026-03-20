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
        }

        [RelayCommand]
        private async Task SaveSettings()
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

            await _settingsService.SaveAsync(_settings);
        }
    }
}
