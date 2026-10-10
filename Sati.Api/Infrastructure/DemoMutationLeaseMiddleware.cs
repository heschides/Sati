using Microsoft.Data.SqlClient;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

internal static class DemoResetLease
{
    internal const string Resource = "SatiDemo.FullReset";
}

internal sealed class DemoMutationLeaseMiddleware
{
    private readonly RequestDelegate next;
    private readonly Func<string, DbConnection> connectionFactory;

    public DemoMutationLeaseMiddleware(RequestDelegate next)
        : this(next, connectionString => new SqlConnection(connectionString)) { }

    internal DemoMutationLeaseMiddleware(RequestDelegate next, Func<string, DbConnection> connectionFactory)
    {
        this.next = next;
        this.connectionFactory = connectionFactory;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IConfiguration configuration,
        ApiDbContext db,
        IOptions<SatiApiOptions> options)
    {
        var isDemo =
            string.Equals(options.Value.ExpectedEnvironment, "Demo", StringComparison.Ordinal) &&
            string.Equals(options.Value.ExpectedDatabaseName, "SatiDemo", StringComparison.Ordinal);
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) ||
            context.Request.Path == "/api/v1/admin/demo/reset" || !isDemo || !db.Database.IsSqlServer())
        {
            await next(context);
            return;
        }

        var connectionString = configuration.GetConnectionString("SatiDemo")!;
        await using var connection = connectionFactory(connectionString);
        await connection.OpenAsync(context.RequestAborted);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=@resource,
                @LockMode=N'Shared', @LockOwner=N'Session', @LockTimeout=0;
            SELECT @result;
            """;
        var resource = command.CreateParameter();
        resource.ParameterName = "@resource";
        resource.Value = DemoResetLease.Resource;
        command.Parameters.Add(resource);
        var result = await command.ExecuteScalarAsync(context.RequestAborted);
        try
        {
            if (!SqlSessionAdmission.OwnsLease(result, context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new ApiErrorDto(
                    "demo_reset_in_progress",
                    "The Demo is being restored. Sign in again in a few minutes.",
                    context.TraceIdentifier), context.RequestAborted);
                return;
            }
            await next(context);
        }
        finally
        {
            if (SqlSessionAdmission.ConfirmedAcquired(result))
            {
                await using var release = connection.CreateCommand();
                release.CommandText = "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner=N'Session';";
                var releaseResource = release.CreateParameter();
                releaseResource.ParameterName = "@resource";
                releaseResource.Value = DemoResetLease.Resource;
                release.Parameters.Add(releaseResource);
                await release.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
    }
}
