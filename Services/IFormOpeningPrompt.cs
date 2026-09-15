namespace Sati.Services;

/// <summary>
/// UI boundary for capturing when a compliance form was actually opened. Keeping
/// the window behind this interface lets the ViewModel remain presentation-only
/// and makes the blank-by-default date choice testable.
/// </summary>
public interface IFormOpeningPrompt
{
    DateTime? SelectActualOpeningDate(
        string formLabel,
        DateTime availableOn,
        DateTime latestAllowedDate);
}
