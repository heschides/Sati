using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace Sati.Converters;

/// <summary>Presentation labels only; signature state decisions stay in shared rules.</summary>
public sealed class SignatureLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value?.ToString() switch
    {
        "ChangesRequested" => "Changes requested",
        "Declined" => "Denied",
        "PinRejected" => "Incorrect signing code",
        "SignerRecordChanged" => "Signer details changed",
        "ExternalAccessRevoked" => "Online copy access stopped",
        "Issued" => "Pending",
        "Viewed" => "Pending — opened for review",
        "Revoked" => "Request stopped",
        "AuthorizationWithdrawn" => "Authorization withdrawn",
        "ElectronicConsentWithdrawn" => "Electronic signing declined",
        "GeneratedInSati" => "Generated in Sati",
        "RecordedAsExternal" => "Recorded from outside Sati",
        "ReleaseAgency" => "Agency release",
        "ReleaseMedical" => "Medical release",
        "ReleaseDhhs" => "DHHS release",
        "DhhsAuthorizedRepresentative" => "DHHS Authorized Representative",
        "PrivacyPractices" => "Notice of privacy practices",
        "MedicalRecordsRequest" => "Medical records request",
        "Staff" => "Staff member",
        "System" => "Sati",
        "NotQueued" => "Not prepared",
        "NeedsReview" => "Needs staff review",
        "Sent" => "Accepted by delivery service",
        { } text => Regex.Replace(text, "(?<=[a-z])(?=[A-Z])", " "),
        _ => ""
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
