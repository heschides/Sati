namespace Sati.Services;

/// <summary>
/// Tells the shell that the signed-in session is over.
///
/// This exists so the window can offer a sign-in prompt without referencing the
/// cloud transport. Local database services also end sessions after account or
/// credential revocation; not holding a bearer token does not grant lasting access.
/// </summary>
public interface ISessionLifetime
{
    /// <summary>
    /// Raised once per ended session, possibly from a background thread. Handlers
    /// that touch UI must marshal to the dispatcher themselves.
    /// </summary>
    event EventHandler? SessionEnded;
    bool HasSessionEnded => false;
    void Invalidate() { }
    void SuspendAccess() { }
    void ResumeAccess() { }
}

/// <summary>
/// Inert lifetime for isolated hosts and tests. Production uses its SessionService
/// or CloudSessionLifetime instead.
/// </summary>
public sealed class NeverEndingSessionLifetime : ISessionLifetime
{
    public event EventHandler? SessionEnded
    {
        add { }
        remove { }
    }
}
