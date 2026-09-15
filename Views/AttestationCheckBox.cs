namespace Sati.Views;

/// <summary>
/// Displays the authoritative completion state while using activation only to
/// open the attestation command. A user click must never manufacture a checked
/// state before the dated attestation has been saved.
/// </summary>
public class AttestationCheckBox : System.Windows.Controls.CheckBox
{
    protected override void OnToggle()
    {
        // Intentionally do not change IsChecked. ToggleButton.OnClick still runs
        // the bound command, and the one-way binding updates only after the form's
        // recorded completion date changes through attestation or revocation.
    }
}
