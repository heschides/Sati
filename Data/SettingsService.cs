using Microsoft.EntityFrameworkCore;
using Sati.Models;
using Sati.Services;
using Sati.Services.Billing;
using Sati.Contracts.V1;
using System.Text.Json;
using System.Data;

namespace Sati.Data
{
    public class SettingsService : ISettingsService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISessionService _sessionService;

        public SettingsService(IDbContextFactory<SatiContext> contextFactory, ISessionService sessionService)
        {
            _contextFactory = contextFactory;
            _sessionService = sessionService;
        }

        public async Task<Settings> LoadAsync()
        {
            await using var context = _contextFactory.CreateDbContext();
            await LocalTenantAccess.EnsureSessionAsync(context, _sessionService);
            var agencyId = CurrentAgencyId();
            var settings = await context.Settings.SingleOrDefaultAsync(x => x.AgencyId == agencyId);

            if (settings is null)
            {
                settings = new Settings
                {
                    AgencyId = agencyId,
                    BillingComplianceRequirements = BillingComplianceGate.DefaultRequirements,
                    ReviewOpenDaysBefore = 10,
                    ReviewDaysAfterDue = 10,
                    PcpOpenDaysBefore = 90,
                    PcpDaysAfterDue = 30,
                    CompAssessmentOpenDaysBefore = 30,
                    CompAssessmentDaysAfterDue = 30,
                    ReclassificationOpenDaysBefore = 60,
                    ReclassificationDaysAfterDue = 0,
                    SafetyPlanOpenDaysBefore = 90,
                    SafetyPlanDaysAfterDue = 30,
                    PrivacyPracticesOpenDaysBefore = 90,
                    PrivacyPracticesDaysAfterDue = 30,
                    ReleaseAgencyOpenDaysBefore = 90,
                    ReleaseAgencyDaysAfterDue = 30,
                    ReleaseDhhsOpenDaysBefore = 90,
                    ReleaseDhhsDaysAfterDue = 30,
                    ReleaseMedicalOpenDaysBefore = 90,
                    ReleaseMedicalDaysAfterDue = 30,
                    // Anniversary offsets (anniversary − N days = due date).
                    // PCP, Safety Plan, Privacy Practices, and Releases are due
                    // on the anniversary itself. Comp Assessment is due 90 days
                    // earlier and becomes available another 30 days before that.
                    // Reclassification is due 30 days earlier (Evergreen workflow).
                    Q4RDaysBeforeAnniversary = 5,
                    PcpDaysBeforeAnniversary = 0,
                    CompAssessmentDaysBeforeAnniversary = 90,
                    ReclassificationDaysBeforeAnniversary = 30,
                    SafetyPlanDaysBeforeAnniversary = 0,
                    PrivacyPracticesDaysBeforeAnniversary = 0,
                    ReleaseAgencyDaysBeforeAnniversary = 0,
                    ReleaseDhhsDaysBeforeAnniversary = 0,
                    ReleaseMedicalDaysBeforeAnniversary = 0,
                };

                context.Settings.Add(settings);
                await context.SaveChangesAsync();
            }

            settings.AbandonedAfterDays =
                ProductivityForecast.NormalizeDocumentationWindowDays(settings.AbandonedAfterDays);

            // The legacy Settings column is only the fallback before an agency has
            // appended its first effective-dated policy. Once history exists, the
            // version in force today is what ordinary settings readers see.
            var policyRows = await context.BillingCompliancePolicyVersions.AsNoTracking()
                .Where(version => version.AgencyId == agencyId)
                .ToListAsync();
            settings.BillingComplianceRequirements =
                BillingCompliancePolicyRules.ResolveForServiceDate(
                    policyRows.Select(version => version.ToSnapshot()),
                    agencyId,
                    BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow))
                    ?.Requirements ?? settings.BillingComplianceRequirements;

            return settings;
        }

        public async Task SaveAsync(Settings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            if (!SettingsAccessPolicy.CanManageAgencySettings(
                    _sessionService.CurrentUser?.Permissions ?? Sati.Contracts.V1.UserPermissions.None))
                throw new SettingsSaveException(
                    "Only an agency administrator can change operational settings.",
                    new UnauthorizedAccessException());
            if (!BillingComplianceGate.IsSupported(settings.BillingComplianceRequirements))
                throw new SettingsSaveException(
                    "The billing-compliance requirement selection is invalid.",
                    new ArgumentOutOfRangeException(nameof(settings)));

            if (settings.AbandonedAfterDays <= 0)
                throw new SettingsSaveException(
                    "The documentation window must be at least one day.",
                    new ArgumentOutOfRangeException(nameof(settings)));
            if (settings.AnnualPacketOpenDaysBefore is < 0 or > 180)
                throw new ArgumentException("Annual packet opening must be 0–180 days.");
            if (string.IsNullOrWhiteSpace(settings.VrAssistantTitle) ||
                settings.VrAssistantTitle.Trim().Length > VocationalRehabilitationProfile.AssistantTitleMaxLength)
                throw new SettingsSaveException(
                    $"The VR assistant title is required and must not exceed {VocationalRehabilitationProfile.AssistantTitleMaxLength} characters.",
                    new ArgumentOutOfRangeException(nameof(settings)));

            settings.VrAssistantTitle =
                VocationalRehabilitationProfile.NormalizeAssistantTitle(settings.VrAssistantTitle);

            await using var context = _contextFactory.CreateDbContext();
            await LocalTenantAccess.EnsureSessionAsync(context, _sessionService);
            var agencyId = CurrentAgencyId();
            var tracked = await context.Settings.SingleOrDefaultAsync(x => x.Id == settings.Id && x.AgencyId == agencyId)
                ?? throw new InvalidOperationException("The settings record is outside the current agency.");

            if (tracked.Revision != settings.Revision)
                throw new SettingsConcurrencyException();

            var policyRows = await context.BillingCompliancePolicyVersions.AsNoTracking()
                .Where(version => version.AgencyId == agencyId)
                .ToListAsync();
            var activeRequirements = BillingCompliancePolicyRules.ResolveForServiceDate(
                    policyRows.Select(version => version.ToSnapshot()),
                    agencyId,
                    BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow))
                ?.Requirements ?? tracked.BillingComplianceRequirements;
            if (settings.BillingComplianceRequirements != activeRequirements)
            {
                throw new SettingsSaveException(
                    "Billing-compliance checkboxes are applied separately and require an enforcement date.",
                    new ArgumentException("An enforcement date is required."));
            }

            var storedFallbackRequirements = tracked.BillingComplianceRequirements;
            context.Entry(tracked).CurrentValues.SetValues(settings);
            tracked.Id = settings.Id;
            tracked.AgencyId = agencyId;
            // Only AppendBillingCompliancePolicyAsync may change official policy.
            // This column remains a compatibility fallback for agencies with no
            // policy history and is never overwritten by an ordinary settings save.
            tracked.BillingComplianceRequirements = storedFallbackRequirements;
            tracked.Revision++;
            try
            {
                await context.SaveChangesAsync();
                settings.Revision = tracked.Revision;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new SettingsConcurrencyException(ex);
            }
        }

        public async Task<BillingComplianceRequirements>
            ResolveBillingComplianceRequirementsAsync(DateTime serviceDate)
        {
            await using var context = _contextFactory.CreateDbContext();
            var actor = await LocalTenantAccess.EnsureSessionAsync(context, _sessionService);
            var policy = await BillingCompliancePolicyContextLoader.LoadAsync(
                context, actor.AgencyId);
            return policy.Resolve(serviceDate.Date);
        }

        public async Task<IReadOnlyList<BillingCompliancePolicyVersionDto>>
            LoadBillingCompliancePolicyHistoryAsync()
        {
            if (!SettingsAccessPolicy.CanManageAgencySettings(
                    _sessionService.CurrentUser?.Permissions ?? UserPermissions.None))
            {
                throw new SettingsSaveException(
                    "Only an agency administrator can view billing-policy history.",
                    new UnauthorizedAccessException());
            }

            await using var context = _contextFactory.CreateDbContext();
            var actor = await LocalTenantAccess.EnsureSessionAsync(context, _sessionService);
            var rows = await context.BillingCompliancePolicyVersions.AsNoTracking()
                .Where(version => version.AgencyId == actor.AgencyId)
                .OrderByDescending(version => version.EffectiveOn)
                .ThenByDescending(version => version.Id)
                .ToListAsync();
            return rows.Select(ToPolicyDto).ToList();
        }

        public async Task<BillingCompliancePolicyImpactPreviewDto>
            PreviewBillingCompliancePolicyAsync(
                PreviewBillingCompliancePolicyRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!SettingsAccessPolicy.CanManageAgencySettings(
                    _sessionService.CurrentUser?.Permissions ?? UserPermissions.None))
            {
                throw new SettingsSaveException(
                    "Only an agency administrator can preview billing-policy changes.",
                    new UnauthorizedAccessException());
            }
            if (request.EffectiveOn is null)
            {
                throw new SettingsSaveException(
                    "An enforcement date is required before impact can be previewed.",
                    new ArgumentException(nameof(request)));
            }
            if (!BillingComplianceGate.IsSupported(request.Requirements))
            {
                throw new SettingsSaveException(
                    "The billing-compliance policy contains unsupported requirements.",
                    new ArgumentOutOfRangeException(nameof(request)));
            }

            await using var context = _contextFactory.CreateDbContext();
            var actor = await LocalTenantAccess.EnsureSessionAsync(context, _sessionService);
            var evaluation = await BuildBillingCompliancePolicyImpactAsync(
                context,
                actor.AgencyId,
                request.EffectiveOn.Value,
                request.Requirements);
            return evaluation.Preview;
        }

        public async Task<IReadOnlyList<BillingCompliancePolicyReviewFlagDto>>
            LoadBillingCompliancePolicyReviewFlagsAsync()
        {
            var permissions = _sessionService.CurrentUser?.Permissions ?? UserPermissions.None;
            if (!UserPermissionRules.HasAdminPermissions(permissions) &&
                !UserPermissionRules.HasBillingPermissions(permissions))
            {
                throw new SettingsSaveException(
                    "Only agency administrators and billing staff can view billing-policy review flags.",
                    new UnauthorizedAccessException());
            }

            await using var context = _contextFactory.CreateDbContext();
            var actor = await LocalTenantAccess.EnsureSessionAsync(context, _sessionService);
            var flags = await context.BillingCompliancePolicyReviewFlags.AsNoTracking()
                .Include(flag => flag.PolicyVersion)
                .Where(flag => flag.AgencyId == actor.AgencyId)
                .OrderByDescending(flag => flag.CreatedAtUtc)
                .ThenByDescending(flag => flag.Id)
                .ToListAsync();
            return flags.Select(flag => flag.ToContract()).ToArray();
        }

        public async Task<BillingCompliancePolicyVersionDto> AppendBillingCompliancePolicyAsync(
            AppendBillingCompliancePolicyRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!SettingsAccessPolicy.CanManageAgencySettings(
                    _sessionService.CurrentUser?.Permissions ?? UserPermissions.None))
            {
                throw new SettingsSaveException(
                    "Only an agency administrator can change billing policy.",
                    new UnauthorizedAccessException());
            }

            await using var context = _contextFactory.CreateDbContext();
            var actor = await LocalTenantAccess.EnsureSessionAsync(context, _sessionService);
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);
            var settings = await context.Settings.SingleOrDefaultAsync(
                candidate => candidate.AgencyId == actor.AgencyId)
                ?? throw new SettingsSaveException(
                    "Agency settings must be initialized before billing policy can be changed.",
                    new InvalidOperationException());

            if (request.ChangeId == Guid.Empty)
            {
                throw new SettingsSaveException(
                    "The billing-policy change id is required.",
                    new ArgumentException(nameof(request.ChangeId)));
            }

            var existing = await context.BillingCompliancePolicyVersions.AsNoTracking()
                .SingleOrDefaultAsync(version => version.VersionId == request.ChangeId);
            if (existing is not null)
            {
                var normalizedExplanation = NormalizeExplanation(request.Explanation);
                if (existing.AgencyId == actor.AgencyId &&
                    existing.EffectiveOn.Date == request.EffectiveOn?.Date &&
                    existing.Requirements == request.Requirements &&
                    string.Equals(existing.Explanation, normalizedExplanation, StringComparison.Ordinal))
                {
                    return ToPolicyDto(existing);
                }

                throw new SettingsSaveException(
                    "That billing-policy change id was already used for different values.",
                    new InvalidOperationException());
            }

            var today = BillingRules.MaineBusinessDate(DateTimeOffset.UtcNow);
            var decision = BillingCompliancePolicyRules.ValidateChange(
                request.Requirements,
                request.EffectiveOn,
                today,
                new BillingCompliancePolicyOptions(settings.AllowPastBillingPolicyEffectiveDates),
                request.Explanation);
            if (!decision.Accepted)
            {
                throw new SettingsSaveException(
                    string.Join(" ", decision.Errors),
                    new ArgumentException(nameof(request)));
            }

            var recordedAtUtc = DateTimeOffset.UtcNow.UtcDateTime;
            var impact = await BuildBillingCompliancePolicyImpactAsync(
                context,
                actor.AgencyId,
                request.EffectiveOn!.Value,
                request.Requirements);
            var version = BillingCompliancePolicyVersion.Create(
                actor.AgencyId,
                request.Requirements,
                request.EffectiveOn!.Value,
                today,
                actor.Id,
                recordedAtUtc,
                settings.AllowPastBillingPolicyEffectiveDates,
                request.Explanation,
                request.ChangeId);
            context.BillingCompliancePolicyVersions.Add(version);
            var reviewFlags = CreateReviewFlags(version, impact.Impacts, recordedAtUtc);
            context.BillingCompliancePolicyReviewFlags.AddRange(reviewFlags);
            await RecalculateUnsubmittedNoteStatesAsync(context, impact.Impacts);
            LocalAuditTrail.Record(
                context,
                actor,
                LocalAuditActions.BillingCompliancePolicyAppended,
                "BillingCompliancePolicyVersion",
                metadataJson: JsonSerializer.Serialize(new
                {
                    changeId = version.VersionId,
                    effectiveOn = version.EffectiveOn.ToString("yyyy-MM-dd"),
                    requirements = (int)version.Requirements,
                    isPastCorrection = version.EffectiveOn.Date < today,
                    unresolvedReviewFlags = reviewFlags.Count
                }));
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return ToPolicyDto(version);
        }

        private static async Task<BillingPolicyImpactEvaluation>
            BuildBillingCompliancePolicyImpactAsync(
                SatiContext context,
                int agencyId,
                DateTime effectiveOn,
                BillingComplianceRequirements proposedRequirements,
                CancellationToken cancellationToken = default)
        {
            var policy = await BillingCompliancePolicyContextLoader.LoadAsync(
                context, agencyId, cancellationToken);
            var enforcementDate = effectiveOn.Date;
            var nextPolicyDate = policy.Versions
                .Where(version => version.EffectiveOn.Date > enforcementDate)
                .Select(version => (DateTime?)version.EffectiveOn.Date)
                .Min();
            var relevantStatuses = new[]
            {
                NoteStatus.HeldForCompliance,
                NoteStatus.Pending,
                NoteStatus.Logged,
                NoteStatus.Approved,
                NoteStatus.ComplianceBlocked
            };
            var notes = await context.Notes.AsNoTracking()
                .Where(note => note.EventDate != null &&
                               note.EventDate.Value >= enforcementDate &&
                               (nextPolicyDate == null ||
                                note.EventDate.Value < nextPolicyDate.Value) &&
                               note.Person.AgencyId == agencyId &&
                               note.Person.Status != PersonStatus.Ghost &&
                               (relevantStatuses.Contains(note.Status!.Value) ||
                                context.ClaimLines.Any(line => line.NoteId == note.Id)))
                .Select(note => new
                {
                    note.Id,
                    note.PersonId,
                    ServiceDate = note.EventDate!.Value,
                    note.Status
                })
                .ToListAsync(cancellationToken);
            var noteIds = notes.Select(note => note.Id).ToArray();
            var personIds = notes.Select(note => note.PersonId).Distinct().ToArray();
            var people = await context.People.AsNoTracking()
                .Include(person => person.Forms)
                    .ThenInclude(form => form.Attestations)
                .Include(person => person.ReleaseObligations)
                    .ThenInclude(obligation => obligation.Attestations)
                .Where(person => personIds.Contains(person.Id) &&
                                 person.AgencyId == agencyId)
                .ToListAsync(cancellationToken);
            await ReleaseComplianceProjectionLoader.PopulateAsync(
                context, people, agencyId, cancellationToken);
            var claims = await (from line in context.ClaimLines.AsNoTracking()
                                join period in context.BillingPeriods.AsNoTracking()
                                    on line.BillingPeriodId equals period.Id
                                join owner in context.Users.AsNoTracking()
                                    on period.UserId equals owner.Id
                                where noteIds.Contains(line.NoteId) &&
                                      owner.AgencyId == agencyId
                                select new
                                {
                                    line.Id,
                                    line.NoteId,
                                    IsFinalized = period.SubmittedAt != null ||
                                                  period.Status != BillingStatus.Draft
                                })
                .ToListAsync(cancellationToken);
            var peopleById = people.ToDictionary(person => person.Id);
            var claimsByNote = claims.ToLookup(claim => claim.NoteId);
            var facts = notes.Select(note =>
            {
                var noteClaims = claimsByNote[note.Id].ToArray();
                var finalizedCount = noteClaims.Count(claim => claim.IsFinalized);
                return new BillingCompliancePolicyImpactRecordSnapshot(
                    note.Id,
                    note.PersonId,
                    note.ServiceDate.Date,
                    note.Status is NoteStatus.Logged or NoteStatus.Approved ||
                        finalizedCount > 0,
                    noteClaims.Length - finalizedCount,
                    finalizedCount,
                    BillingService.BuildRecoveryObligations(
                        peopleById[note.PersonId],
                        policy.Schedule,
                        note.ServiceDate.Date),
                    noteClaims.Where(claim => claim.IsFinalized)
                        .Select(claim => claim.Id)
                        .ToArray());
            }).ToArray();
            var impacts = BillingCompliancePolicyImpactRules.Analyze(
                agencyId,
                policy.FallbackRequirements,
                policy.Versions,
                enforcementDate,
                proposedRequirements,
                facts);
            var preview = BillingCompliancePolicyImpactRules.Preview(
                agencyId,
                policy.FallbackRequirements,
                policy.Versions,
                enforcementDate,
                proposedRequirements,
                facts);
            return new BillingPolicyImpactEvaluation(preview, impacts);
        }

        private static async Task RecalculateUnsubmittedNoteStatesAsync(
            SatiContext context,
            IEnumerable<BillingCompliancePolicyRecordImpact> impacts)
        {
            var changes = impacts
                .Where(impact => !impact.IsSubmittedOrFinalized &&
                                 impact.ChangeKind is
                                     BillingCompliancePolicyImpactChangeKind.NewlyBlocked or
                                     BillingCompliancePolicyImpactChangeKind.NewlyUnblocked)
                .ToDictionary(impact => impact.NoteId, impact => impact.ChangeKind);
            if (changes.Count == 0)
                return;

            var noteIds = changes.Keys.ToArray();
            var notes = await context.Notes
                .Where(note => noteIds.Contains(note.Id))
                .ToListAsync();
            foreach (var note in notes)
            {
                note.Status = changes[note.Id] switch
                {
                    BillingCompliancePolicyImpactChangeKind.NewlyBlocked
                        when note.Status == NoteStatus.Pending => NoteStatus.ComplianceBlocked,
                    BillingCompliancePolicyImpactChangeKind.NewlyUnblocked
                        when note.Status == NoteStatus.ComplianceBlocked => NoteStatus.Pending,
                    _ => note.Status
                };
            }
        }

        private static List<BillingCompliancePolicyReviewFlag> CreateReviewFlags(
            BillingCompliancePolicyVersion version,
            IEnumerable<BillingCompliancePolicyRecordImpact> impacts,
            DateTime createdAtUtc)
        {
            var flags = new List<BillingCompliancePolicyReviewFlag>();
            foreach (var impact in impacts.Where(item => item.IsSubmittedOrFinalized))
            {
                flags.Add(BillingCompliancePolicyReviewFlag.ForNote(
                    version, impact, createdAtUtc));
                flags.AddRange(impact.SubmittedOrFinalizedClaimRecordIds.Select(
                    claimLineId => BillingCompliancePolicyReviewFlag.ForClaimLine(
                        version, impact, claimLineId, createdAtUtc)));
            }

            return flags;
        }

        private sealed record BillingPolicyImpactEvaluation(
            BillingCompliancePolicyImpactPreviewDto Preview,
            IReadOnlyList<BillingCompliancePolicyRecordImpact> Impacts);

        private static BillingCompliancePolicyVersionDto ToPolicyDto(
            BillingCompliancePolicyVersion version) => new(
            version.Id,
            version.VersionId,
            version.EffectiveOn,
            version.Requirements,
            version.CreatedByUserId,
            version.RecordedAtUtc,
            version.Explanation);

        private static string? NormalizeExplanation(string? explanation) =>
            string.IsNullOrWhiteSpace(explanation) ? null : explanation.Trim();

        private int CurrentAgencyId() => _sessionService.CurrentUser?.AgencyId
            ?? throw new InvalidOperationException("A signed-in user is required to access agency settings.");
    }
}
