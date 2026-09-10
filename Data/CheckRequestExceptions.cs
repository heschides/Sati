namespace Sati.Data;

public sealed class CheckRequestConcurrencyException(Exception? innerException = null)
    : Exception("This check request changed elsewhere. Reload it before trying again.", innerException);

public sealed class CheckRequestLockedException()
    : Exception("A PDF has already been prepared from this check request, so it is read-only. Create a new request for a correction.");
