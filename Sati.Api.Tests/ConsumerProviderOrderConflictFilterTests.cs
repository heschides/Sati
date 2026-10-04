using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

public sealed class ConsumerProviderOrderConflictFilterTests
{
    [Fact]
    public async Task DatabaseConcurrencyIsATypedConflict()
    {
        var filter = new ConsumerProviderOrderConflictFilter();
        var context = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext());
        var result = await filter.InvokeAsync(context, _ => throw new DbUpdateConcurrencyException("Synthetic collision"));
        var conflict = Assert.IsType<Conflict<ApiErrorDto>>(result);
        Assert.Equal(ConsumerProviderOrder.ConflictCode, conflict.Value?.Code);
        Assert.DoesNotContain("Synthetic", conflict.Value?.Message);
    }

    [Fact]
    public async Task AuditStorageFailureIsNotMisreportedAsConcurrency()
    {
        var filter = new ConsumerProviderOrderConflictFilter();
        var context = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext());
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await filter.InvokeAsync(context, _ => throw new DbUpdateException("Synthetic storage failure")));
    }
}
