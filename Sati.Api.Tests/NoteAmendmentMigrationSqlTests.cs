using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Persistence.Migrations;
using Xunit;

namespace Sati.Api.Tests;

public sealed class NoteAmendmentMigrationSqlTests
{
    [SqlServerFact]
    public async Task ControlledGuardedScriptRehearsesRerunsAndRejectsAnIncompatibleExistingColumn()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true);
        await database.InitializeAsync();
        await using var factory = new SyntheticPipelineFactory(database);
        await factory.SeedAsync();
        await using var db = new ApiDbContext(database.Options());
        var migration = new AddNoteAmendments { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.DownOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.__EFMigrationsHistory(MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);
            DECLARE @n int=1;
            WHILE @n<=123 BEGIN
              INSERT dbo.__EFMigrationsHistory VALUES(CONCAT(N'Synthetic_',@n),N'10.0.5'); SET @n+=1;
            END;
            INSERT dbo.__EFMigrationsHistory VALUES(N'20261002221023_AddScheduledNoteMoves',N'10.0.5');
            """);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SatiLogica.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var sql = await File.ReadAllTextAsync(Path.Combine(root.FullName, "scripts/Apply-NoteAmendmentsMigration.guarded.sql"));
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await Run(rollback: true);
        await using var absent = connection.CreateCommand();
        absent.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name=N'NoteAmendments';";
        Assert.Equal(0, Convert.ToInt32(await absent.ExecuteScalarAsync()));
        await Run(rollback: false);
        await Run(rollback: false);
        await using var corrupt = connection.CreateCommand();
        corrupt.CommandText = "ALTER TABLE dbo.NoteAmendmentVersions ALTER COLUMN FinancialContentJson nvarchar(999) NOT NULL;";
        await corrupt.ExecuteNonQueryAsync();
        var failure = await Assert.ThrowsAsync<SqlException>(() => Run(rollback: false));
        Assert.Equal(53603, failure.Number);

        async Task Run(bool rollback)
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("@expectedDatabase", database.Name);
            await command.ExecuteNonQueryAsync();
            command.CommandText = "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;";
            Assert.Equal(125, Convert.ToInt32(await command.ExecuteScalarAsync()));
            if (rollback) await transaction.RollbackAsync(); else await transaction.CommitAsync();
        }
    }
    [SqlServerFact]
    public async Task TwoWritersSerializeAndTheSecondStaleDraftGetsTypedConflict()
    {
        await using var database=new SyntheticPipelineDatabase(sqlServer:true); await database.InitializeAsync(); await using var factory=new SyntheticPipelineFactory(database); var actors=await factory.SeedAsync();
        var options=new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db=new ApiDbContext(options); var note=new ServerNote {AgencyId=actors.AgencyId,PersonId=actors.FirstPersonId,Narrative="Synthetic concurrent original",Status=6,EventDate=new DateTime(2026,8,1),Minutes=15}; db.Notes.Add(note); await db.SaveChangesAsync();
        var source=ContractMapper.ToNote(note); var actor=new AgencyActor(actors.AuthorId,actors.AgencyId,UserPermissions.CaseManagement);
        NoteAmendmentResultDto draft;
        await using(var tx=await ServiceTimeWriteScope.BeginAsync(db,actors.AgencyId,actors.AuthorId))
        { draft=await NoteAmendmentWorkflow.ExecuteAsync(db,actor,source,actors.AuthorId,false,new(Guid.NewGuid(),NoteAmendmentAction.Create,null,0,1,null,new("Synthetic draft",note.EventDate,15,null,false),"Correct synthetic wording"),DateTime.Today,DateTime.UtcNow,(_,_)=>Task.CompletedTask,(_,_,_)=>{}); await tx.CommitAsync(); }
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first=Write(true); await ready.Task.WaitAsync(TimeSpan.FromSeconds(15)); var second=Write(false);
        try
        {
            var deadline=DateTime.UtcNow.AddSeconds(10); while(!await database.HasWaitingApplicationLockAsync() && DateTime.UtcNow<deadline) await Task.Delay(25);
            Assert.True(await database.HasWaitingApplicationLockAsync(),"The second connection must contend on the database-owned schedule lock.");
        }
        finally { release.TrySetResult(); }
        Assert.Null(await first); var stale=Assert.IsType<NoteAmendmentWorkflowException>(await second); Assert.Equal(409,stale.StatusCode); Assert.Equal(NoteAmendmentRules.RevisionCode,stale.Code);
        db.ChangeTracker.Clear(); Assert.Equal(2,await db.NoteAmendmentVersions.CountAsync()); Assert.Equal(2,await db.NoteAmendmentEvents.CountAsync());
        async Task<Exception?> Write(bool hold)
        {
            try { await using var writer=new ApiDbContext(options); await using var tx=await ServiceTimeWriteScope.BeginAsync(writer,actors.AgencyId,actors.AuthorId);
                await NoteAmendmentWorkflow.ExecuteAsync(writer,actor,source,actors.AuthorId,false,new(Guid.NewGuid(),NoteAmendmentAction.Save,draft.Id,1,1,null,new(hold?"First corrected draft":"Stale corrected draft",note.EventDate,15,null,false),"Correct synthetic wording"),DateTime.Today,DateTime.UtcNow,(_,_)=>Task.CompletedTask,(_,_,_)=>{});
                if(hold) {ready.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20));} await tx.CommitAsync(); return null;
            } catch(Exception e) {if(hold)ready.TrySetException(e); return e;}
        }
    }
    [SqlServerFact]
    public async Task AdditiveUpgradeKeepsOriginalRowsAndRollbackRefusesWrittenHistory()
    {
        await using var database=new SyntheticPipelineDatabase(sqlServer:true); await database.InitializeAsync();
        await using var factory=new SyntheticPipelineFactory(database); var actors=await factory.SeedAsync();
        // SQL model fixture owns every table. Rehearse the exact migration down/up on that private schema.
        await using var db=new ApiDbContext(database.Options()); var migration=new AddNoteAmendments {ActiveProvider="Microsoft.EntityFrameworkCore.SqlServer"};
        var generator=db.GetService<IMigrationsSqlGenerator>();
        await Apply(migration.DownOperations); await Apply(migration.UpOperations);
        var note=new ServerNote {AgencyId=actors.AgencyId,PersonId=actors.FirstPersonId,Narrative="Synthetic original",Status=6,EventDate=DateTime.SpecifyKind(DateTime.Today.AddDays(-1),DateTimeKind.Unspecified),Minutes=15};
        db.Notes.Add(note); await db.SaveChangesAsync(); await db.Entry(note).ReloadAsync(); var original=NoteAmendmentWorkflow.Snapshot(ContractMapper.ToNote(note));
        var actor=new AgencyActor(actors.AuthorId,actors.AgencyId,UserPermissions.CaseManagement);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx=await ServiceTimeWriteScope.BeginAsync(db,actors.AgencyId,actors.AuthorId);
            await NoteAmendmentWorkflow.ExecuteAsync(db,actor,ContractMapper.ToNote(note),actors.AuthorId,false,
                new(Guid.NewGuid(),NoteAmendmentAction.Create,null,0,note.Revision,null,new("Synthetic correction",note.EventDate,15,null,false),"Correct synthetic wording"),DateTime.Today,DateTime.UtcNow,
                (content,finance)=>Task.CompletedTask,(_,_,_)=>{});
            await tx.CommitAsync();
        });
        db.ChangeTracker.Clear(); Assert.Equal(original,NoteAmendmentWorkflow.Snapshot(ContractMapper.ToNote(await db.Notes.SingleAsync(n=>n.Id==note.Id))));
        var failure=await Assert.ThrowsAsync<SqlException>(()=>Apply(migration.DownOperations)); Assert.Equal(51031,failure.Number);
        Assert.Single(await db.NoteAmendments.ToListAsync()); Assert.Single(await db.NoteAmendmentVersions.ToListAsync());
        async Task Apply(IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations)
        {
            foreach(var command in generator.Generate(operations,db.Model)) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        }
    }
}
