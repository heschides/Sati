using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Xunit;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Sati.Api.Tests;

public sealed class NoteAmendmentApiTests
{
    [Fact]
    public async Task SubmittedThenRejectedTimeProposalLeavesBillingProjectionOnOriginal()
    {
        await using var f=new SatiApiFactory(); var id=await f.CreateNoteInStatusAsync(6); using var a=await f.CreateAuthenticatedClientAsync("case-manager-one"); using var r=await f.CreateAuthenticatedClientAsync("supervisor-one"); using var b=await f.CreateAuthenticatedClientAsync("billing-only-one");
        var w=await Read(a,id); await Act(a,id,Request(w,NoteAmendmentAction.Create,w.EffectiveContent with {Minutes=15},"Correct synthetic duration")); await Act(a,id,Request(await Read(a,id),NoteAmendmentAction.Submit));
        var pending=(await b.GetFromJsonAsync<List<BillingCandidateDto>>("/api/v1/billing/candidates"))!; Assert.Equal(60,Assert.Single(pending,n=>n.NoteId==id).Minutes);
        await Act(r,id,Request(await Read(r,id),NoteAmendmentAction.Reject,review:"Original service duration was verified"));
        var rejected=(await b.GetFromJsonAsync<List<BillingCandidateDto>>("/api/v1/billing/candidates"))!; Assert.Equal(60,Assert.Single(rejected,n=>n.NoteId==id).Minutes); Assert.False((await Read(a,id)).RequiresFinancialReview);
    }
    [Fact]
    public async Task StaleSavedDraftCannotOverwriteAConcurrentSavedVersion()
    {
        await using var f=new SatiApiFactory(); var id=await f.CreateNoteInStatusAsync(6); using var a=await f.CreateAuthenticatedClientAsync("case-manager-one");
        var w=await Read(a,id); await Act(a,id,Request(w,NoteAmendmentAction.Create,w.EffectiveContent,"Initial corrected wording"));
        w=await Read(a,id); var stale=Request(w,NoteAmendmentAction.Save,w.EffectiveContent with {Narrative="Stale wording"},"Stale synthetic correction");
        await Act(a,id,Request(w,NoteAmendmentAction.Save,w.EffectiveContent with {Narrative="Current wording"},"Current synthetic correction"));
        using var conflict=await a.PostAsJsonAsync($"/api/v1/notes/{id}/amendments",stale); Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
        var final=await Read(a,id); Assert.Equal("Current wording",final.Amendments.Single().Versions.Last().Content.Narrative); Assert.Equal(2,final.Amendments.Single().Versions.Count);
    }
    [Fact]
    public async Task AuditFailureRollsBackAllIntermediateDraftAndVersionWrites()
    {
        var failure = new AuditFailure(); await using var f = new SatiApiFactory { DatabaseCommandInterceptor = failure };
        var id = await f.CreateNoteInStatusAsync(6); using var a = await f.CreateAuthenticatedClientAsync("case-manager-one"); var w = await Read(a,id);
        failure.Armed = true;
        using var response = await a.PostAsJsonAsync($"/api/v1/notes/{id}/amendments",Request(w,NoteAmendmentAction.Create,w.EffectiveContent,"Synthetic failure probe"));
        Assert.Equal(HttpStatusCode.InternalServerError,response.StatusCode); Assert.True(failure.Fired);
        failure.Armed = false; Assert.Empty((await Read(a,id)).Amendments);
        await using var scope = f.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Empty(await db.NoteAmendmentVersions.ToListAsync()); Assert.Empty(await db.NoteAmendmentEvents.ToListAsync());
        Assert.Equal(NoteAmendmentWorkflow.Snapshot(w.Original),NoteAmendmentWorkflow.Snapshot((await Read(a,id)).Original));
        Assert.Empty(await f.GetAuditEventsAsync("note.amendment.create"));
    }
    [Fact]
    public async Task FinancialReplacementCarriesReviewedLineageAndKeepsOriginalFileAndClaim()
    {
        await using var f = new SatiApiFactory(); using var b = await f.CreateAuthenticatedClientAsync("admin-two");
        using(var generated = await b.PostAsJsonAsync("/api/v1/billing/periods/1202/edi",new GenerateEdiRequest(true,Guid.NewGuid().ToString("N")))) generated.EnsureSuccessStatusCode();
        using(var paid = await b.PostAsJsonAsync("/api/v1/billing/periods/1202/mock-clearinghouse",new MockClearinghouseRequest(MockClearinghouseScenario.DeniedMissingInformation))) paid.EnsureSuccessStatusCode();
        string originalLine, originalFile;
        await using(var scope=f.Services.CreateAsyncScope()) { var db=scope.ServiceProvider.GetRequiredService<ApiDbContext>(); originalLine=JsonSerializer.Serialize(await db.ClaimLines.AsNoTracking().SingleAsync(l=>l.Id==1402)); originalFile=(await db.EdiGenerations.AsNoTracking().OrderBy(g=>g.Id).FirstAsync(g=>g.BillingPeriodId==1202)).Content; }
        using var a = await f.CreateAuthenticatedClientAsync("case-manager-two"); using var r = await f.CreateAuthenticatedClientAsync("supervisor-two");
        var w=await Read(a,603); await Act(a,603,Request(w,NoteAmendmentAction.Create,w.EffectiveContent with {Minutes=30},"Correct recorded duration"));
        await Act(a,603,Request(await Read(a,603),NoteAmendmentAction.Submit)); await Act(r,603,Request(await Read(r,603),NoteAmendmentAction.Approve)); w=await Read(a,603);
        using(var held=await b.PostAsJsonAsync("/api/v1/billing/periods/1202/corrections",new CreateClaimCorrectionRequest(1402,ClaimCorrectionAction.Replace,"Correct service duration"))) Assert.Equal(HttpStatusCode.Conflict,held.StatusCode);
        using(var review=await b.PostAsJsonAsync("/api/v1/billing/notes/603/amendment-review",new NoteAmendmentFinancialReviewRequest(Guid.NewGuid(),w.EffectiveVersionId!.Value,w.Original.Revision,"Verified corrected actual service duration"))) review.EnsureSuccessStatusCode();
        using(var missing=await b.PostAsJsonAsync("/api/v1/billing/periods/1202/corrections",new CreateClaimCorrectionRequest(1402,ClaimCorrectionAction.Replace,"Correct service duration"))) Assert.Equal(HttpStatusCode.Conflict,missing.StatusCode);
        using(var corrected=await b.PostAsJsonAsync("/api/v1/billing/periods/1202/corrections",new CreateClaimCorrectionRequest(1402,ClaimCorrectionAction.Replace,"Correct service duration",w.EffectiveVersionId,50m)))
            Assert.True(corrected.IsSuccessStatusCode,await corrected.Content.ReadAsStringAsync());
        using(var generated=await b.PostAsJsonAsync("/api/v1/billing/periods/1202/corrections/edi",new GenerateEdiRequest(true,Guid.NewGuid().ToString("N"))))
        { Assert.True(generated.IsSuccessStatusCode,await generated.Content.ReadAsStringAsync()); var file=(await generated.Content.ReadFromJsonAsync<EdiFileDto>())!; Assert.Contains("*11::7*",file.Content); Assert.Contains("*UN*2*",file.Content); }
        await using var finalScope=f.Services.CreateAsyncScope(); var finalDb=finalScope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Equal(originalLine,JsonSerializer.Serialize(await finalDb.ClaimLines.AsNoTracking().SingleAsync(l=>l.Id==1402)));
        Assert.Equal(originalFile,(await finalDb.EdiGenerations.AsNoTracking().OrderBy(g=>g.Id).FirstAsync(g=>g.BillingPeriodId==1202)).Content);
        var correction=Assert.Single(await finalDb.ClaimCorrections.ToListAsync()); Assert.Equal(w.EffectiveVersionId,correction.AmendedNoteVersionId); Assert.Equal(2m,correction.CorrectedUnits); Assert.Equal(50m,correction.CorrectedChargeAmount);
    }
    private sealed class AuditFailure : DbCommandInterceptor
    {
        public bool Armed { get; set; } public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct=default)
        {
            if(Armed && command.CommandText.Contains("INSERT INTO \"AuditEvents\"",StringComparison.Ordinal)) { Fired=true; throw new InvalidOperationException("Synthetic audit failure"); }
            return base.ReaderExecutingAsync(command,data,result,ct);
        }
    }
    private static async Task<NoteAmendmentWorkspaceDto> Read(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<NoteAmendmentWorkspaceDto>($"/api/v1/notes/{id}/amendments"))!;
    private static NoteAmendmentRequest Request(NoteAmendmentWorkspaceDto w, NoteAmendmentAction action,
        NoteAmendmentContent? content = null, string? reason = null, string? review = null)
    {
        var a = w.Amendments.LastOrDefault();
        if (action == NoteAmendmentAction.Create) a = null;
        return new(Guid.NewGuid(), action, a?.Id, a?.Revision ?? 0, w.Original.Revision, w.EffectiveVersionId, content, reason, review);
    }
    private static async Task<NoteAmendmentResultDto> Act(HttpClient c, int id, NoteAmendmentRequest r)
    {
        using var response = await c.PostAsJsonAsync($"/api/v1/notes/{id}/amendments", r);
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<NoteAmendmentResultDto>())!;
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReturnedResubmissionPreservesOriginalAndEveryVersion(bool claimed)
    {
        await using var f = new SatiApiFactory(); var id = await f.CreateNoteInStatusAsync(6);
        using var author = await f.CreateAuthenticatedClientAsync("case-manager-one"); using var reviewer = await f.CreateAuthenticatedClientAsync("supervisor-one");
        if (claimed) await AddClaim(f, id);
        var w = await Read(author, id); var original = JsonSerializer.Serialize(w.Original);
        var content = w.EffectiveContent with { Narrative = "Corrected synthetic narrative" };
        var create = Request(w, NoteAmendmentAction.Create, content, "Correct transcription");
        var first = await Act(author, id, create); Assert.Equal(first, await Act(author, id, create));
        await Act(author, id, Request(await Read(author,id), NoteAmendmentAction.Submit));
        await Act(reviewer, id, Request(await Read(reviewer,id), NoteAmendmentAction.Return, review: "Clarify the corrected text"));
        await Act(author, id, Request(await Read(author,id), NoteAmendmentAction.Save, content with { Narrative = "Clarified synthetic narrative" }, "Clarify transcription"));
        await Act(author, id, Request(await Read(author,id), NoteAmendmentAction.Submit));
        var approval = Request(await Read(reviewer,id), NoteAmendmentAction.Approve);
        await Act(reviewer, id, approval); await Act(reviewer,id,approval);
        var final = await Read(author,id); Assert.Equal(original, JsonSerializer.Serialize(final.Original));
        Assert.Equal("Clarified synthetic narrative", final.EffectiveContent.Narrative); Assert.False(final.RequiresFinancialReview);
        var a = Assert.Single(final.Amendments); Assert.Equal(4,a.Versions.Count); Assert.Equal(6,a.Events.Count);
        Assert.Equal(a.SubmittedVersionId,a.ApprovedVersionId);
        Assert.DoesNotContain(await f.GetAuditEventsAsync("note.amendment.approve"),e=>e.MetadataJson.Contains("synthetic narrative"));
        if (claimed) { await using var scope=f.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<ApiDbContext>(); var line=await db.ClaimLines.SingleAsync(l=>l.NoteId==id); Assert.Equal("immutable-synthetic-snapshot",line.ClaimSnapshotJson); Assert.Equal(4,line.Units); }
    }
    [Fact]
    public async Task StaleReviewAndReusedOperationCannotChangeHistory()
    {
        await using var f=new SatiApiFactory(); var id=await f.CreateNoteInStatusAsync(6);
        using var a=await f.CreateAuthenticatedClientAsync("case-manager-one"); using var r=await f.CreateAuthenticatedClientAsync("supervisor-one");
        var w=await Read(a,id); var create=Request(w,NoteAmendmentAction.Create,w.EffectiveContent with {Narrative="Synthetic corrected text"},"Correct wording"); await Act(a,id,create);
        using var reused=await a.PostAsJsonAsync($"/api/v1/notes/{id}/amendments",create with {Reason="Different reason"}); Assert.Equal(HttpStatusCode.Conflict,reused.StatusCode);
        await Act(a,id,Request(await Read(a,id),NoteAmendmentAction.Submit)); var stale=Request(await Read(r,id),NoteAmendmentAction.Approve);
        await Act(r,id,Request(await Read(r,id),NoteAmendmentAction.Return,review:"Change one detail"));
        using var response=await r.PostAsJsonAsync($"/api/v1/notes/{id}/amendments",stale); Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
        Assert.Equal(NoteAmendmentRules.RevisionCode,(await response.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
        Assert.Equal(NoteAmendmentStatus.Returned,Assert.Single((await Read(a,id)).Amendments).Status);
    }
    [Theory]
    [InlineData("case-manager-two")] [InlineData("supervisor-two")] [InlineData("demoted-supervisor-one")] [InlineData("billing-only-one")]
    public async Task ForeignOrDeniedActorsCannotDiscoverOrMutate(string user)
    {
        await using var f=new SatiApiFactory(); var id=await f.CreateNoteInStatusAsync(6); using var owner=await f.CreateAuthenticatedClientAsync("case-manager-one");
        var w=await Read(owner,id); using var denied=await f.CreateAuthenticatedClientAsync(user);
        using var read=await denied.GetAsync($"/api/v1/notes/{id}/amendments"); Assert.Equal(HttpStatusCode.NotFound,read.StatusCode);
        using var write=await denied.PostAsJsonAsync($"/api/v1/notes/{id}/amendments",Request(w,NoteAmendmentAction.Create,w.EffectiveContent,"Synthetic reason")); Assert.Equal(HttpStatusCode.NotFound,write.StatusCode);
        var queue=(await denied.GetFromJsonAsync<NoteAmendmentQueuePage>("/api/v1/note-amendments?review=true&afterNoteId=0"))!; Assert.DoesNotContain(queue.Items,n=>n.NoteId==id);
    }
    [Fact]
    public async Task OwnerWithSupervisionCannotReviewOwnProposal()
    {
        await using var f=new SatiApiFactory(); var id=await f.CreateNoteInStatusAsync(6); using(var seed=f.Services.CreateScope()) {var db=seed.ServiceProvider.GetRequiredService<ApiDbContext>(); var u=await db.Users.FindAsync(12); u!.Permissions|=UserPermissions.Supervision|UserPermissions.AgencyWideSupervision; await db.SaveChangesAsync();}
        using var a=await f.CreateAuthenticatedClientAsync("case-manager-one"); var w=await Read(a,id); await Act(a,id,Request(w,NoteAmendmentAction.Create,w.EffectiveContent,"Synthetic reason")); await Act(a,id,Request(await Read(a,id),NoteAmendmentAction.Submit));
        using var response=await a.PostAsJsonAsync($"/api/v1/notes/{id}/amendments",Request(await Read(a,id),NoteAmendmentAction.Approve)); Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
    }
    [Fact]
    public async Task FinancialHoldIsExactVersionAndDoesNotAffectPendingOrRejectedProposals()
    {
        await using var f=new SatiApiFactory(); var id=await f.CreateNoteInStatusAsync(6);
        using var a=await f.CreateAuthenticatedClientAsync("case-manager-one"); using var r=await f.CreateAuthenticatedClientAsync("supervisor-one"); using var b=await f.CreateAuthenticatedClientAsync("billing-only-one");
        var w=await Read(a,id); await Act(a,id,Request(w,NoteAmendmentAction.Create,w.EffectiveContent with {Minutes=30},"Correct actual time")); Assert.False((await Read(a,id)).RequiresFinancialReview);
        await Act(a,id,Request(await Read(a,id),NoteAmendmentAction.Submit)); await Act(r,id,Request(await Read(r,id),NoteAmendmentAction.Approve)); w=await Read(a,id); Assert.True(w.RequiresFinancialReview); Assert.Equal(60,w.Original.Minutes);
        using var held=await b.PostAsJsonAsync("/api/v1/billing/claim-lines",new CreateClaimLineRequest(id, false, null)); Assert.Equal(HttpStatusCode.Conflict,held.StatusCode);
        var queue=(await b.GetFromJsonAsync<List<NoteAmendmentFinancialItem>>("/api/v1/billing/note-amendments?afterNoteId=0"))!; var row=Assert.Single(queue,n=>n.NoteId==id); Assert.Equal("",row.CorrectedService.Narrative);
        var request=new NoteAmendmentFinancialReviewRequest(Guid.NewGuid(),w.EffectiveVersionId!.Value,w.Original.Revision,"Reviewed the corrected actual service time");
        using var approved=await b.PostAsJsonAsync($"/api/v1/billing/notes/{id}/amendment-review",request); Assert.Equal(HttpStatusCode.OK,approved.StatusCode);
        using var replay=await b.PostAsJsonAsync($"/api/v1/billing/notes/{id}/amendment-review",request); Assert.Equal(await approved.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
        Assert.False((await Read(a,id)).RequiresFinancialReview);
        await using var scope=f.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<ApiDbContext>(); var source=await db.Notes.FindAsync(id); Assert.Equal(60,source!.Minutes);
        var content=await NoteAmendmentBilling.AuthorizedContentAsync(db,1,id); Assert.Equal(30,content!.Value.Content.Minutes);
    }
    [Fact]
    public async Task ApprovedTimeAmendmentReservesTimeAgainstOtherAmendments()
    {
        await using var f=new SatiApiFactory(); var id=await f.CreateNoteInStatusAsync(6); var other=await f.CreateNoteInStatusAsync(6);
        using var a=await f.CreateAuthenticatedClientAsync("case-manager-one"); using var r=await f.CreateAuthenticatedClientAsync("supervisor-one");
        var w=await Read(a,id); await Act(a,id,Request(w,NoteAmendmentAction.Create,w.EffectiveContent with {StartTime=120,Minutes=30},"Correct service start")); await Act(a,id,Request(await Read(a,id),NoteAmendmentAction.Submit)); await Act(r,id,Request(await Read(r,id),NoteAmendmentAction.Approve));
        w=await Read(a,other); await Act(a,other,Request(w,NoteAmendmentAction.Create,w.EffectiveContent with {StartTime=130,Minutes=15},"Correct service start"));
        using var conflict=await a.PostAsJsonAsync($"/api/v1/notes/{other}/amendments",Request(await Read(a,other),NoteAmendmentAction.Submit)); Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode); Assert.Equal("note_amendment_time_overlap",(await conflict.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
    }
    private static async Task AddClaim(SatiApiFactory f,int id)
    {
        await using var scope=f.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var p=new ServerBillingPeriod {UserId=12,Month=8,Year=2026,Status=1}; p.Lines.Add(new ServerClaimLine {NoteId=id,DateOfService=new(2026,8,3),Units=4,ChargeAmount=40,ClaimSnapshotJson="immutable-synthetic-snapshot"}); db.BillingPeriods.Add(p); await db.SaveChangesAsync();
    }
}


