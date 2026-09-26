using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Sati.Helpers;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Tests;

public sealed class Professional837FormatterTests
{
    private static readonly DateTime GeneratedAt = new(2026, 8, 29, 9, 30, 0);
    private static readonly TradingPartnerProfile ClaimMd = TradingPartnerProfile.ClaimMd("ACCT123", "DEMO01");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothPersistencePathsUseTheSharedFormatter(bool claimMd)
    {
        var profile = claimMd ? ClaimMd : TradingPartnerProfile.OfficeAlly;
        var desktop = DesktopPeriod();
        var server = ServerPeriod();
        var desktopContent = EdiGenerator.Generate(desktop, true, GeneratedAt, "123456789", profile);
        var serverContent = ServerEdiGenerator.Generate(server, true, GeneratedAt, "123456789", profile);
        var sharedContent = Professional837Formatter.Generate(77, 2026, 8,
            [new Professional837Claim(99, ContractMapper.ToReadinessFacts(server.Lines.Single()), "1", null)],
            profile, true, GeneratedAt, "123456789");

        Assert.Equal(sharedContent, desktopContent);
        Assert.Equal(sharedContent, serverContent);
    }

    [Fact]
    public void ClaimMdEnvelopeUsesTheConfiguredAccountAndDocumentedReceiver()
    {
        var content = ServerEdiGenerator.Generate(ServerPeriod(), true, GeneratedAt, "123456789", ClaimMd);
        var isa = Segment(content, "ISA").Split('*');
        var gs = Segment(content, "GS").Split('*');

        Assert.Equal(106, Segment(content, "ISA").Length + 1);
        Assert.Equal("ZZ", isa[5]);
        Assert.Equal("ACCT123        ", isa[6]);
        Assert.Equal("ZZ", isa[7]);
        Assert.Equal("CLAIMMD        ", isa[8]);
        Assert.Equal("ACCT123", gs[2]);
        Assert.Equal("CLAIMMD", gs[3]);
        Assert.Contains("REF*D9*SATI1-DEMO01-1-77-99~", content);
        Assert.DoesNotContain("330897513", content);
        Assert.Equal("123456789-77-99", Assert.Single(ClaimResponseReader.ReadSubmission(content).Claims).ClaimReference);
    }

    [Fact]
    public void ClaimMdRemoteIdentitySurvivesEveryClaimAttempt()
    {
        var period = ServerPeriod();
        var line = period.Lines.Single();
        var files = new[]
        {
            ServerEdiGenerator.Generate(period, true, GeneratedAt, "123456789", ClaimMd),
            ServerEdiGenerator.GenerateCorrections(period, [new EdiClaim(line, "1", null)],
                true, GeneratedAt, "223456789", ClaimMd),
            ServerEdiGenerator.GenerateCorrections(period, [new EdiClaim(line, "7", "PAYER-456")],
                true, GeneratedAt, "323456789", ClaimMd),
            ServerEdiGenerator.GenerateCorrections(period, [new EdiClaim(line, "8", "PAYER-456")],
                true, GeneratedAt, "423456789", ClaimMd)
        };

        for (var index = 0; index < files.Length; index++)
        {
            var file = files[index];
            Assert.Equal($"{index + 1}23456789-77-99", Segment(file, "CLM").Split('*')[1]);
            Assert.Equal("SATI1-DEMO01-1-77-99", Segment(file, "REF*D9").Split('*')[2]);
            Assert.Equal("99", Segment(file, "REF*6R").Split('*')[2]);
            Assert.Equal(index >= 2 ? "PAYER-456" : null,
                file.Split('~').FirstOrDefault(segment => segment.TrimStart().StartsWith("REF*F8*", StringComparison.Ordinal))?
                    .TrimStart().Split('*')[2]);
            Assert.Contains($"*11::{(index == 2 ? "7" : index == 3 ? "8" : "1")}*", file);
        }

        Assert.Equal(4, files.Select(file => Segment(file, "CLM").Split('*')[1]).Distinct().Count());
    }

    [Theory]
    [InlineData("BAD*ACCT", "DEMO01")]
    [InlineData("ACCT123", "demo")]
    [InlineData("ACCT123", "NAMESPACE9")]
    public void ClaimMdProfileRejectsUnsafeOrUnstableIdentifiers(string account, string claimNamespace)
    {
        Assert.Throws<ArgumentException>(() => TradingPartnerProfile.ClaimMd(account, claimNamespace));
    }

    [Fact]
    public void RemoteClaimIdentitySeparatesAgenciesPeriodsNotesAndNamespaces()
    {
        var identity = ClaimSubmissionIdentity.RemoteClaimId("DEMO01", 1, 77, 99);

        Assert.Equal("SATI1-DEMO01-1-77-99", identity);
        Assert.NotEqual(identity, ClaimSubmissionIdentity.RemoteClaimId("DEMO02", 1, 77, 99));
        Assert.NotEqual(identity, ClaimSubmissionIdentity.RemoteClaimId("DEMO01", 2, 77, 99));
        Assert.NotEqual(identity, ClaimSubmissionIdentity.RemoteClaimId("DEMO01", 1, 78, 99));
        Assert.NotEqual(identity, ClaimSubmissionIdentity.RemoteClaimId("DEMO01", 1, 77, 100));
    }

    private static string Segment(string file, string prefix) =>
        file.Split('~').Select(item => item.TrimStart('\r', '\n'))
            .Single(item => item.StartsWith(prefix + "*", StringComparison.Ordinal));

    private static ProfessionalClaimSnapshot Snapshot() => new(
        ProfessionalClaimSnapshotCodec.CurrentVersion,
        1, 101, "Alex", "Example", new DateTime(1990, 2, 3), "U", "987654321",
        "10 Claim Street", "Portland", "ME", "04101",
        "Example Agency", "1999999984", "111111111", "1 Provider Way",
        "Portland", "ME", "04101", "SATITEST1", "Billing Desk", "2075550101",
        "SYNTHETIC PAYER", "MCDME");

    private static BillingPeriod DesktopPeriod() => new()
    {
        Id = 77, UserId = 12, Month = 8, Year = 2026,
        Status = BillingStatus.Submitted,
        Lines = [new ClaimLine
        {
            Id = 88, NoteId = 99, DateOfService = new DateTime(2026, 8, 12),
            ProcedureCode = "G9012", ProcedureModifier = "HI", Units = 1.33m,
            ChargeAmount = 33.25m, ClientMaineCareId = "987654321",
            RenderingProviderNpi = "1999999984", DiagnosisCode = "F89", PlaceOfService = 11,
            ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(Snapshot())
        }]
    };

    private static ServerBillingPeriod ServerPeriod()
    {
        var period = new ServerBillingPeriod { Id = 77, UserId = 12, Month = 8, Year = 2026, Status = 1 };
        period.Lines.Add(new ServerClaimLine
        {
            Id = 88, BillingPeriodId = 77, NoteId = 99,
            DateOfService = new DateTime(2026, 8, 12),
            ProcedureCode = "G9012", ProcedureModifier = "HI", Units = 1.33m,
            ChargeAmount = 33.25m, ClientMaineCareId = "987654321",
            RenderingProviderNpi = "1999999984", DiagnosisCode = "F89", PlaceOfService = 11,
            ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(Snapshot())
        });
        return period;
    }
}
