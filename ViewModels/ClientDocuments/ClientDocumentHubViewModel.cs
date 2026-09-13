using Sati.Contracts.V1;

namespace Sati.ViewModels.ClientDocuments;

public enum ClientDocumentHubMode
{
    AuthorizedRepresentative,
    Releases,
    CwicPacket,
    HousingSupportFunds
}

/// <summary>
/// A dashboard-level doorway into the existing per-client document workspaces.
/// It owns no form logic; both destinations share the exact view models used on
/// the Clients page.
/// </summary>
public sealed class ClientDocumentHubViewModel
{
    public ClientDocumentHubViewModel(
        NewClientViewModel clients,
        ClientDocumentHubMode mode)
    {
        Clients = clients;
        Mode = mode;
    }

    public NewClientViewModel Clients { get; }
    public ClientDocumentHubMode Mode { get; }
    public bool IsAuthorizedRepresentative =>
        Mode == ClientDocumentHubMode.AuthorizedRepresentative;
    public bool IsReleases => Mode == ClientDocumentHubMode.Releases;
    public bool IsCwicPacket => Mode == ClientDocumentHubMode.CwicPacket;
    public bool IsHousingSupportFunds => Mode == ClientDocumentHubMode.HousingSupportFunds;
    public string Title => IsAuthorizedRepresentative
        ? "DHHS Authorized Representative"
        : IsCwicPacket ? "CWIC Referral Packet"
        : IsHousingSupportFunds ? "Housing Support Funds Application" : "Releases";
    public string Description => IsAuthorizedRepresentative
        ? "Prepare Maine DHHS's Appointment of Authorized Representative form for the selected consumer."
        : IsCwicPacket
            ? "Prepare MaineHealth's ten-page Benefits Counseling Services referral packet from consumer profile information and answers entered here."
        : IsHousingSupportFunds
            ? "Prepare Maine DHHS OADS's editable three-page Housing Support Funds application from verified profile facts and answers entered here."
        : "Prepare either the official Maine DHHS release or Sati's agency release for the selected consumer.";

    public void Prepare()
    {
        if (IsCwicPacket || IsHousingSupportFunds)
            return;
        var key = IsAuthorizedRepresentative
            ? DhhsFormDefinition.FormKey.AuthorizedRepresentative
            : DhhsFormDefinition.FormKey.AuthorizationToRelease;
        Clients.DhhsForms.SelectForm(key);
    }
}
