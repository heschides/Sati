using Xunit;

namespace Sati.Tests.Architecture;

public sealed class ProjectGraphRulesTests
{
    private const string SatiApi = "Sati.Api/Sati.Api.csproj";
    private const string SatiContracts = "Sati.Contracts/Sati.Contracts.csproj";
    private const string KarunaApi = "karuna/Karuna.Api/Karuna.Api.csproj";
    private const string KarunaContracts = "karuna/Karuna.Contracts/Karuna.Contracts.csproj";
    private const string KarunaApplication = "karuna/Karuna.Application/Karuna.Application.csproj";
    private const string KarunaWeb = "karuna/Karuna.Web/Karuna.Web.csproj";
    private const string PlatformContracts = "platform/SatiLogica.Contracts/SatiLogica.Contracts.csproj";
    private const string PlatformHosting = "platform/SatiLogica.Hosting/SatiLogica.Hosting.csproj";
    private const string Schema = "platform/SatiLogica.Schema/SatiLogica.Schema.csproj";
    private const string Tool = "tools/SatiUpdateReport/SatiUpdateReport.csproj";

    [Fact]
    public void RejectsSatiToKarunaReference() => AssertCode("ProductBoundary",
        Node(SatiApi, "sati", "host", KarunaContracts),
        Node(KarunaContracts, "karuna", "library"));

    [Fact]
    public void RejectsKarunaToSatiReference() => AssertCode("ProductBoundary",
        Node(KarunaApi, "karuna", "host", SatiContracts),
        Node(SatiContracts, "sati", "library"));

    [Fact]
    public void RejectsPlatformToProductReference() => AssertCode("PlatformBoundary",
        Node(PlatformHosting, "platform", "library", SatiContracts),
        Node(SatiContracts, "sati", "library"));

    [Fact]
    public void AllowsSchemaToProductReference()
    {
        var diagnostics = Check(
            Node(Schema, "platform", "tool", SatiContracts),
            Node(SatiContracts, "sati", "library"));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void RejectsSchemaReferenceFromProductProject() => AssertCode("SchemaConsumer",
        Node(SatiApi, "sati", "host", Schema),
        Node(Schema, "platform", "tool"));

    [Fact]
    public void RejectsTestReferenceToOtherProduct() => AssertCode("TestBoundary",
        Node("Sati.Tests/Sati.Tests.csproj", "sati", "test", KarunaContracts),
        Node(KarunaContracts, "karuna", "library"));

    [Fact]
    public void RejectsNonToolReferenceToTool() => AssertCode("ToolConsumer",
        Node(SatiApi, "sati", "host", Tool),
        Node(Tool, "sati", "tool"));

    [Fact]
    public void RejectsKarunaWebNonContractReference() => AssertCode("KarunaWebBoundary",
        Node(KarunaWeb, "karuna", "client", KarunaApplication),
        Node(KarunaApplication, "karuna", "library"));

    [Fact]
    public void RejectsUnapprovedRepositoryEdge()
    {
        var approved = new[]
        {
            Node(SatiApi, "sati", "host"),
            Node(SatiContracts, "sati", "library")
        };
        var observed = new[]
        {
            Observed(SatiApi, SatiContracts),
            Observed(SatiContracts)
        };
        Assert.Contains(ProjectGraphGuard.Check(approved, observed),
            diagnostic => diagnostic.Code == "UnapprovedReference");
    }

    [Fact]
    public void RejectsStaleApprovedEdge()
    {
        var approved = new[]
        {
            Node(SatiApi, "sati", "host", SatiContracts),
            Node(SatiContracts, "sati", "library")
        };
        var observed = new[]
        {
            Observed(SatiApi),
            Observed(SatiContracts)
        };
        Assert.Contains(ProjectGraphGuard.Check(approved, observed),
            diagnostic => diagnostic.Code == "StaleApprovedReference");
    }

    [Fact]
    public void RejectsUnlistedProject()
    {
        var approved = new[] { Node(SatiApi, "sati", "host") };
        var observed = new[]
        {
            Observed(SatiApi),
            Observed("tools/NewTool/NewTool.csproj")
        };
        Assert.Contains(ProjectGraphGuard.Check(approved, observed),
            diagnostic => diagnostic.Code == "UnlistedProject");
    }

    [Theory]
    [InlineData("platform/SatiLogica.Contracts/SatiLogica.Contracts.csproj", "sati")]
    [InlineData("karuna/Karuna.Tests/Karuna.Tests.csproj", "sati")]
    public void RejectsMisclassifiedPrefixedProject(string path, string wrongProduct) =>
        AssertCode("ProductClassification", Node(path, wrongProduct, "test"));

    [Fact]
    public void RejectsConditionalReference()
    {
        var approved = new[]
        {
            Node(SatiApi, "sati", "host", SatiContracts),
            Node(SatiContracts, "sati", "library")
        };
        var observed = new[]
        {
            new ObservedProject(SatiApi, [new ObservedReference(SatiContracts, true)]),
            Observed(SatiContracts)
        };
        Assert.Contains(ProjectGraphGuard.Check(approved, observed),
            diagnostic => diagnostic.Code == "ConditionalReference");
    }

    private static ApprovedProject Node(string path, string product, string kind,
        params string[] references) => new(path, product, kind, references);

    private static ObservedProject Observed(string path, params string[] references) =>
        new(path, references.Select(reference => new ObservedReference(reference)).ToArray());

    private static IReadOnlyList<GraphDiagnostic> Check(params ApprovedProject[] approved) =>
        ProjectGraphGuard.Check(approved,
            approved.Select(project => Observed(project.Path, [.. project.References])).ToArray());

    private static void AssertCode(string code, params ApprovedProject[] approved) =>
        Assert.Contains(Check(approved), diagnostic => diagnostic.Code == code);
}
