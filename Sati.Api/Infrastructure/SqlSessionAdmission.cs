namespace Sati.Api.Infrastructure;

/// <summary>Interpret only the documented Int32 outcomes, without coercing missing SQL evidence.</summary>
internal static class SqlSessionAdmission
{
    // Also used to arrange release before validation can throw on caller cancellation.
    internal static bool ConfirmedAcquired(object? result) => result is int and (0 or 1);

    internal static bool OwnsLease(object? result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (result is not int value || value is not (0 or 1 or -1))
            throw new InvalidOperationException("The database did not confirm session lock admission.");
        return value != -1;
    }
}
