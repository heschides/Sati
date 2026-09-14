using Sati.Contracts.V1;

namespace Sati.Models;

/// <summary>
/// A representative-payee check requisition. Identity fields are snapshots so a
/// published financial document can be regenerated exactly after profile changes.
/// </summary>
public class CheckRequest
{
    public int Id { get; private set; }
    public int Revision { get; set; } = 1;
    public int PersonId { get; private set; }
    public Person? Person { get; private set; }

    public string ConsumerName { get; private set; } = string.Empty;
    public string AgencyName { get; private set; } = string.Empty;
    public string CaseManagerName { get; private set; } = string.Empty;
    public string SupervisorName { get; private set; } = string.Empty;

    public DateTime? RequestDate { get; set; }
    public string? PayableTo { get; set; }
    public string? MailingAddress { get; set; }
    public decimal Amount { get; set; }
    public DateTime? NeededByDate { get; set; }
    public string? Reason { get; set; }
    public int? TemplateId { get; private set; }
    public CheckRequestTemplate? Template { get; private set; }
    public DateTime? ScheduledForDate { get; private set; }
    public bool IsAutomaticallyGenerated => TemplateId is not null && ScheduledForDate is not null;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public int? PublishedByUserId { get; private set; }
    public string? PublishedByName { get; private set; }
    public bool IsPublished => PublishedAtUtc is not null;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public CheckRequestWorkflowStatus WorkflowStatus { get; private set; } = CheckRequestWorkflowStatus.Draft;

    protected CheckRequest() { }

    public static CheckRequest CreateForClient(Person person, User owner, DateTime createdAtUtc) => new()
    {
        PersonId = person.Id,
        ConsumerName = person.FullName,
        AgencyName = owner.Agency?.Name ?? string.Empty,
        CaseManagerName = owner.DisplayName,
        SupervisorName = owner.Supervisor?.DisplayName ?? string.Empty,
        RequestDate = createdAtUtc.Date,
        CreatedAtUtc = createdAtUtc
    };

    public static CheckRequest Rehydrate(
        int id, int personId, int revision,
        string consumerName, string agencyName, string caseManagerName, string supervisorName,
        DateTime? requestDate, string? payableTo, string? mailingAddress, decimal amount,
        DateTime? neededByDate, string? reason, DateTime createdAtUtc,
        DateTime? publishedAtUtc, int? publishedByUserId, string? publishedByName,
        CheckRequestWorkflowStatus? workflowStatus = null,
        int? templateId = null,
        DateTime? scheduledForDate = null) => new()
    {
        Id = id,
        PersonId = personId,
        Revision = revision,
        ConsumerName = consumerName,
        AgencyName = agencyName,
        CaseManagerName = caseManagerName,
        SupervisorName = supervisorName,
        RequestDate = requestDate,
        PayableTo = payableTo,
        MailingAddress = mailingAddress,
        Amount = amount,
        NeededByDate = neededByDate,
        Reason = reason,
        CreatedAtUtc = createdAtUtc,
        PublishedAtUtc = publishedAtUtc,
        PublishedByUserId = publishedByUserId,
        PublishedByName = publishedByName,
        TemplateId = templateId,
        ScheduledForDate = scheduledForDate,
        WorkflowStatus = workflowStatus ?? (publishedAtUtc is null
            ? CheckRequestWorkflowStatus.Draft
            : CheckRequestWorkflowStatus.Prepared)
    };

    public void RehydrateIdentity(int id) { if (Id == 0) Id = id; }

    public void Publish(User actor, DateTime publishedAtUtc)
    {
        if (IsPublished)
            throw new InvalidOperationException("A PDF has already been prepared from this check request.");
        PublishedAtUtc = publishedAtUtc;
        PublishedByUserId = actor.Id;
        PublishedByName = actor.DisplayName;
    }

    public void RehydratePublication(DateTime? publishedAtUtc, int? publishedByUserId, string? publishedByName)
    {
        PublishedAtUtc = publishedAtUtc;
        PublishedByUserId = publishedByUserId;
        PublishedByName = publishedByName;
        if (WorkflowStatus == CheckRequestWorkflowStatus.Draft && publishedAtUtc is not null)
            WorkflowStatus = CheckRequestWorkflowStatus.Prepared;
    }

    public void RehydrateWorkflow(CheckRequestWorkflowStatus status) => WorkflowStatus = status;

    public void MarkAutomaticallyGenerated(int templateId, DateTime scheduledForDate)
    {
        if (Id != 0 || TemplateId is not null)
            throw new InvalidOperationException("Automatic-generation provenance can be assigned only to a new draft.");
        TemplateId = templateId;
        ScheduledForDate = scheduledForDate.Date;
    }
}
