using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

internal sealed class ConsumerProviderOrderConflictFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (Microsoft.Data.SqlClient.SqlException error) when (error.Number == 1205)
        { return Conflict(); }
        catch (DbUpdateException error) when (error is DbUpdateConcurrencyException ||
            error.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number == 1205)
        { return Conflict(); }
    }

    private static IResult Conflict() => Results.Conflict(new ApiErrorDto(
        ConsumerProviderOrder.ConflictCode, ConsumerProviderOrder.ConflictMessage, string.Empty));
}
