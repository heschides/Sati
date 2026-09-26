using System.ComponentModel;

namespace Sati.Helpers;

/// <summary>
/// Whether the signed-in user is trying the new layout. Views read it through a static
/// binding, <c>{Binding Path=(helpers:NewLayoutPreview.IsEnabled)}</c>, so a screen can
/// switch layouts without every view model carrying the flag. The shell sets it from
/// <see cref="Services.NewLayoutPreferenceService"/> at sign-in and when Settings changes it.
/// </summary>
public static class NewLayoutPreview
{
    private static bool _isEnabled;

    public static event EventHandler<PropertyChangedEventArgs>? StaticPropertyChanged;

    public static bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;

            _isEnabled = value;
            StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }
}
