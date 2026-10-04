using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Services.Billing;
using Sati.Models;
using Xunit;

namespace Sati.Tests;

public sealed class NoteAmendmentServiceTests
{
    [Fact]
    public async Task LocalAmendmentAndFinancialReviewProjectCorrectedUnitsWithoutUpdatingOriginal()
    {
        await using var f = await NotePipelineTests.PipelineFixture.CreateAsync();
        var id = await f.SeedNoteAsync(f.PersonOneId, NoteStatus.Approved, f.BillableDate);
        var author = Service(f.Factory, f.CaseManagerOne); var reviewer = Service(f.Factory, f.SupervisorOne); var finance = Service(f.Factory, f.AdminOne);
        var w = await author.GetAsync(id); var original = w.Original;
        await author.ActAsync(id, Request(w, NoteAmendmentAction.Create, w.EffectiveContent with { Minutes = 45, Narrative = "Corrected synthetic service duration" }));
        await author.ActAsync(id, Request(await author.GetAsync(id), NoteAmendmentAction.Submit));
        await reviewer.ActAsync(id, Request(await reviewer.GetAsync(id), NoteAmendmentAction.Approve));
        var billing = new BillingService(f.Factory); var actor = f.AdminOne.ToAgencyActor();
        Assert.DoesNotContain(await billing.GetApprovedUnbilledNotesAsync(actor), n => n.Id == id);
        var held = await Assert.ThrowsAsync<NoteAmendmentWorkflowException>(() => billing.CreateClaimLineAsync(actor,id)); Assert.Equal("note_amendment_financial_hold", held.Code);
        w = await author.GetAsync(id);
        await finance.ReviewFinancialAsync(id, new(Guid.NewGuid(), w.EffectiveVersionId!.Value, w.Original.Revision, "Verified the actual duration against documentation"));
        var candidate = Assert.Single(await billing.GetApprovedUnbilledNotesAsync(actor), n => n.Id == id); Assert.Equal(45,candidate.Minutes);
        var claim = await billing.CreateClaimLineAsync(actor,id); Assert.Equal(3m,claim.Units); Assert.Equal(w.EffectiveVersionId,claim.AmendedNoteVersionId);
        await billing.SubmitBillingPeriodAsync(actor,claim.BillingPeriodId);
        Assert.Equal(NoteAmendmentWorkflow.Snapshot(original),NoteAmendmentWorkflow.Snapshot((await author.GetAsync(id)).Original));
        await using var db=f.Factory.CreateDbContext(); var source=await db.Notes.FindAsync(id); Assert.Equal(original.Minutes,source!.Minutes);
        var version = await db.NoteAmendmentVersions.FirstAsync(); version.Reason="Illicit overwrite";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
        db.ChangeTracker.Clear(); source=await db.Notes.FindAsync(id); source!.Narrative="Illicit original overwrite";
        await Assert.ThrowsAsync<NoteAmendmentWorkflowException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task LocalForeignAccessAndSelfReviewAreDenied()
    {
        await using var f=await NotePipelineTests.PipelineFixture.CreateAsync(); var id=await f.SeedNoteAsync(f.PersonOneId,NoteStatus.Approved,f.BillableDate);
        await Assert.ThrowsAsync<NoteAmendmentWorkflowException>(()=>Service(f.Factory,f.CaseManagerTwo).GetAsync(id));
        await Assert.ThrowsAsync<NoteAmendmentWorkflowException>(()=>Service(f.Factory,f.SupervisorTwo).GetAsync(id));
        var author=Service(f.Factory,f.CaseManagerOne); var w=await author.GetAsync(id); await author.ActAsync(id,Request(w,NoteAmendmentAction.Create,w.EffectiveContent));
        await author.ActAsync(id,Request(await author.GetAsync(id),NoteAmendmentAction.Submit));
        await Assert.ThrowsAsync<NoteAmendmentWorkflowException>(()=>author.ActAsync(id,Request(w,NoteAmendmentAction.Approve)));
    }
    private static NoteAmendmentService Service(IDbContextFactory<SatiContext> factory, User user)
    { var session=new SessionService(); session.SetUser(user); return new(factory,session); }
    private static NoteAmendmentRequest Request(NoteAmendmentWorkspaceDto w, NoteAmendmentAction action, NoteAmendmentContent? content = null)
    {
        var a=w.Amendments.LastOrDefault(); return new(Guid.NewGuid(),action,a?.Id,a?.Revision??0,w.Original.Revision,w.EffectiveVersionId,content,content is null?null:"Correct documented service facts");
    }
}


