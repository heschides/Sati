namespace Sati.Services;

/// <summary>Asks the biller for a response without exposing a Window to the ViewModel.</summary>
public interface IClearinghouseResponseFilePicker
{
    Task<string?> ReadResponseAsync(CancellationToken cancellationToken);
}
