using System.Net;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Xunit;

namespace Sati.Api.Tests;

public sealed class NoteAbandonmentApiTests
{
    [Fact]
    public async Task DesktopTriggeredSweepAuditsTheExactChangedNotesOnce()
    {
        await using var factory = new SatiApiFactory();
        var first = await factory.CreateNoteInStatusAsync(1);
        var second = await factory.CreateNoteInStatusAsync(1);
        var foreign = await factory.CreateNoteInStatusAsync(1, personId: 201);
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        using var response = await client.PostAsync("/api/v1/notes/abandon-overdue", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((8, 2), await factory.GetNoteStateAsync(first));
        Assert.Equal((8, 2), await factory.GetNoteStateAsync(second));
        Assert.Equal((1, 1), await factory.GetNoteStateAsync(foreign));

        var events = await factory.GetAuditEventsAsync("note.abandoned-by-system");
        var audit = Assert.Single(events);
        Assert.Equal(1, audit.AgencyId);
        Assert.Equal(0, audit.ActorUserId);
        Assert.Contains("\"actorKind\":\"system\"", audit.MetadataJson);
        var changedIds = NoteIds(audit.MetadataJson);
        Assert.Contains(first, changedIds);
        Assert.Contains(second, changedIds);
        Assert.DoesNotContain(foreign, changedIds);

        using var repeat = await client.PostAsync("/api/v1/notes/abandon-overdue", null);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Single(await factory.GetAuditEventsAsync("note.abandoned-by-system"));
    }

    [Fact]
    public async Task ConcurrentEditAfterCandidateSelectionIsNotOverwritten()
    {
        var race = new ConcurrentNoteEditInterceptor();
        await using var factory = new SatiApiFactory { DatabaseCommandInterceptor = race };
        var id = await factory.CreateNoteInStatusAsync(1);
        race.NoteId = id;
        using var client = await factory.CreateAuthenticatedClientAsync("case-manager-one");

        using var response = await client.PostAsync("/api/v1/notes/abandon-overdue", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(race.Fired, "The regression requires an edit between candidate read and write.");
        Assert.Equal((1, 2), await factory.GetNoteStateAsync(id));
        Assert.DoesNotContain(await factory.GetAuditEventsAsync("note.abandoned-by-system"),
            audit => NoteIds(audit.MetadataJson).Contains(id));
    }

    private static int[] NoteIds(string metadataJson)
    {
        using var document = JsonDocument.Parse(metadataJson);
        return document.RootElement.GetProperty("noteIds").EnumerateArray()
            .Select(item => item.GetInt32()).ToArray();
    }

    private sealed class ConcurrentNoteEditInterceptor : DbCommandInterceptor
    {
        private int fired;
        public int NoteId { get; set; }
        public bool Fired => fired != 0;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (NoteId > 0 && command.CommandText.Contains("UPDATE \"Notes\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("\"Status\"", StringComparison.Ordinal) &&
                Interlocked.CompareExchange(ref fired, 1, 0) == 0)
            {
                await using var edit = command.Connection!.CreateCommand();
                edit.Transaction = command.Transaction;
                edit.CommandText = "UPDATE \"Notes\" SET \"Narrative\" = 'Concurrent edit', " +
                                   "\"Revision\" = \"Revision\" + 1 WHERE \"Id\" = @id";
                var id = edit.CreateParameter();
                id.ParameterName = "@id";
                id.Value = NoteId;
                edit.Parameters.Add(id);
                await edit.ExecuteNonQueryAsync(cancellationToken);
            }
            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
