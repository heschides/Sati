using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

/// <summary>Contains escaping request exceptions without publishing their content.</summary>
internal sealed class ApiExceptionBoundaryMiddleware(
    RequestDelegate next,
    ApiIncidentRecorder incidents,
    ILogger<ApiExceptionBoundaryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (Exception error) when ((error is OperationCanceledException or IOException) &&
            context.RequestAborted.IsCancellationRequested)
        {
            try
            {
                if (!context.Response.HasStarted) context.Response.StatusCode = 499;
                else AbortSafely(context);
            }
            catch { AbortSafely(context); }
        }
        catch (Exception error)
        {
            // Error handling must not send its own exceptions to the host either.
            try { await HandleFailureAsync(context, error); }
            catch { AbortSafely(context); }
        }
    }

    private async Task HandleFailureAsync(HttpContext context, Exception error)
    {
        LogFailure("Unhandled API error", error, context.TraceIdentifier);
        try { await incidents.RecordAsync(error, context, context.RequestAborted); }
        catch { /* Incident-store failure must not replace the original response. */ }
        if (context.Response.HasStarted)
        {
            AbortSafely(context);
            return;
        }
        try
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers.CacheControl = "no-cache,no-store";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "-1";
            await context.Response.WriteAsJsonAsync(new ApiErrorDto(
                "server_error", "The request could not be completed.", context.TraceIdentifier),
                context.RequestAborted);
        }
        catch (Exception responseError)
        {
            LogFailure("API error response failed", responseError, context.TraceIdentifier);
            AbortSafely(context);
        }
    }

    private void LogFailure(string operation, Exception error, string correlationId)
    {
        try
        {
            var type = error.GetType().FullName ?? "Unknown";
            var safeType = type[..Math.Min(type.Length, 160)];
            // Fixed operation labels and shape fields only; never pass the Exception object.
            logger.LogError("{Operation}. FailureType={FailureType} HResult={HResult} CorrelationId={CorrelationId}",
                operation, safeType, error.HResult, correlationId);
        }
        catch { /* A failed logger cannot prevent response/incident handling. */ }
    }

    private static void AbortSafely(HttpContext context)
    {
        try { context.Abort(); }
        catch { /* No raw secondary exception may escape this boundary. */ }
    }
}
