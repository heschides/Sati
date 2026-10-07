using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models.Assessments;
using Sati.Persistence.Migrations;
using Xunit;

namespace Sati.Api.Tests;

[Collection("Synthetic pipeline SQL Server")]
public sealed class AssessmentReviewSqlTests
{
    [SqlServerFact]
    public async Task ControlledMigrationRollsBackRerunsDetectsDriftAndRefusesLossyDowngrade()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true); await database.InitializeAsync();
        await using var factory = new SyntheticPipelineFactory(database); var actors = await factory.SeedAsync();
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer(database.ConnectionString).Options;
        await using var db = new ApiDbContext(options);
        var migration = new AddAssessmentReviewCycles { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" };
        await Apply(migration.DownOperations);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.__EFMigrationsHistory(MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);
            DECLARE @n int=1; WHILE @n<=126 BEGIN
             INSERT dbo.__EFMigrationsHistory VALUES(CONCAT(N'20260000000000_Synthetic_',@n),N'10.0.5'); SET @n+=1;
            END;
            INSERT dbo.__EFMigrationsHistory VALUES(N'20261007004626_AddRecordsGovernance',N'10.0.5');
            """);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SatiLogica.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var sql = await File.ReadAllTextAsync(Path.Combine(root.FullName, "scripts/Apply-AssessmentReviewMigration.guarded.sql"));
        await using var connection = new SqlConnection(database.ConnectionString); await connection.OpenAsync();
        await Run(rollback: true); await Run(rollback: false); await Run(rollback: false);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.AssessmentReviewEvents ALTER COLUMN Text nvarchar(3999) NOT NULL;");
        Assert.Equal(53812, (await Assert.ThrowsAsync<SqlException>(() => Run(false))).Number);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.AssessmentReviewEvents ALTER COLUMN Text nvarchar(4000) NOT NULL;");
        await SeedSubmission(db, actors);
        Assert.Equal(53814, (await Assert.ThrowsAsync<SqlException>(() => Apply(migration.DownOperations))).Number);
        Assert.Single(await db.Set<AssessmentSubmission>().AsNoTracking().ToListAsync());
        async Task Run(bool rollback)
        {
            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
            await using var command = connection.CreateCommand(); command.Transaction = tx;
            command.CommandText = sql; command.Parameters.AddWithValue("@expectedDatabase", database.Name);
            await command.ExecuteNonQueryAsync(); command.CommandText = "SELECT COUNT(*) FROM dbo.__EFMigrationsHistory;";
            Assert.Equal(128, Convert.ToInt32(await command.ExecuteScalarAsync()));
            if (rollback) await tx.RollbackAsync(); else await tx.CommitAsync();
        }
        async Task Apply(IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations)
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.Model))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
            await tx.CommitAsync();
        }
    }

    [SqlServerFact]
    public async Task IndependentConnectionWaitsForReviewCommitAndRejectsTheOlderApprovalRevision()
    {
        await using var database = new SyntheticPipelineDatabase(sqlServer: true); await database.InitializeAsync();
        await using var factory = new SyntheticPipelineFactory(database); var actors = await factory.SeedAsync();
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer(database.ConnectionString).Options;
        AssessmentSubmission snapshot;
        await using (var db = new ApiDbContext(options)) snapshot = await SeedSubmission(db, actors);
        var actor = new AgencyActor(actors.SupervisorId, actors.AgencyId, UserPermissions.Supervision);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var comment = Comment(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(15)); var approval = Approve();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10); var waiting = false;
            do
            {
                await using var connection = new SqlConnection(database.ConnectionString); await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_database_id=DB_ID() AND request_status='WAIT';";
                waiting = Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
                if (!waiting) await Task.Delay(25);
            } while (!waiting && DateTime.UtcNow < deadline && !approval.IsCompleted);
            Assert.True(waiting, "The second connection must wait on the uncommitted assessment revision.");
        }
        finally { release.TrySetResult(); }
        await comment; var error = await approval; Assert.Equal(409, error.Status); Assert.Equal("stale_assessment", error.Code);
        await using var verify = new ApiDbContext(options);
        Assert.Equal("Comment", Assert.Single(await verify.Set<AssessmentReviewEvent>().ToListAsync()).Action);
        Assert.Equal("ReadyForReview", (await verify.ComprehensiveAssessments.SingleAsync()).Status);
        async Task Comment()
        {
            await using var db = new ApiDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var row = await db.ComprehensiveAssessments.SingleAsync();
            var review = await AssessmentReviewWorkflow.ReviewAsync(db, actor, State(row),
                new(snapshot.Id, row.Revision, snapshot.ContentSha256, "Comment", Text: "Synthetic concurrent comment"), true, false, DateTime.UtcNow);
            db.Add(review); row.Revision++; await db.SaveChangesAsync(); ready.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20)); await tx.CommitAsync();
        }
        async Task<AssessmentWorkflowException> Approve()
        {
            await using var db = new ApiDbContext(options); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var row = await db.ComprehensiveAssessments.SingleAsync();
            return await Assert.ThrowsAsync<AssessmentWorkflowException>(() => AssessmentReviewWorkflow.ReviewAsync(db, actor, State(row),
                new(snapshot.Id, 2, snapshot.ContentSha256, "Approve"), true, false, DateTime.UtcNow));
        }
    }
    private static AssessmentWorkflowState State(ServerComprehensiveAssessment row) => new(row.Id, row.PersonId,
        row.AuthorUserId, row.Version, row.Revision, row.Status, row.DocumentJson);
    private static async Task<AssessmentSubmission> SeedSubmission(ApiDbContext db, PipelineActors actors)
    {
        var target = DateTime.Today.AddDays(100);
        var form = new ServerForm { PersonId = actors.FirstPersonId, Type = "ComprehensiveAssessment", TargetEffectiveDate = target, DueDate = target.AddDays(-90) };
        var row = new ServerComprehensiveAssessment { PersonId = actors.FirstPersonId, AuthorUserId = actors.AuthorId, Status = "Draft",
            Version = 1, Revision = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            DocumentJson = JsonSerializer.Serialize(AssessmentReviewApiTests.CompleteDocument(), AssessmentReviewRules.JsonOptions) };
        db.AddRange(form, row); await db.SaveChangesAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var snapshot = await AssessmentReviewWorkflow.SubmitAsync(db, new(actors.AuthorId, actors.AgencyId, UserPermissions.CaseManagement), State(row),
            new(1, AssessmentReviewRules.Hash(row.DocumentJson), form.Id, target, form.DueDate), "Synthetic consumer", DateTime.UtcNow);
        row.Status = "ReadyForReview"; row.Revision = 2; await db.SaveChangesAsync(); await tx.CommitAsync(); return snapshot;
    }
}
