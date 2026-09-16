using Sati.Contracts.V1;

namespace Sati.Data;

public sealed class PersonValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception(PersonSaveRules.Describe(errors))
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class PersonPersistenceException(string message, Exception innerException)
    : Exception(message, innerException);

/// <summary>
/// The client was saved by someone or something else after this copy was loaded. Raised
/// before anything is written, so the caller knows the edit was not saved.
/// </summary>
public sealed class PersonConcurrencyException()
    : InvalidOperationException("This Person was changed after you opened it. Reload the Person before saving.");
