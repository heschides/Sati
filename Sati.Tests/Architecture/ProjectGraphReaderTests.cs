using Xunit;

namespace Sati.Tests.Architecture;

public sealed class ProjectGraphReaderTests
{
    [Theory]
    [InlineData("TestResults", false)]
    [InlineData("src/TestResults", true)]
    public void IgnoresOnlyTheRootTestEvidenceDirectory(string folder, bool shouldDiscover)
    {
        var root = Path.Combine(Path.GetTempPath(), $"sati-project-graph-{Guid.NewGuid():N}");
        try
        {
            var evidence = Path.Combine(root, folder, "snapshot");
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, "Unlisted.csproj"), "<Project />");
            var observed = ProjectGraphReader.ReadProjectTree(root);
            if (shouldDiscover)
                Assert.Single(ProjectGraphGuard.Check([], observed), diagnostic => diagnostic.Code == "UnlistedProject");
            else
                Assert.Empty(observed);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NormalizesRelativeProjectReference()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sati-project-graph-{Guid.NewGuid():N}");
        try
        {
            var first = Path.Combine(root, "A");
            var second = Path.Combine(root, "B");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, "A.csproj"),
                "<Project><ItemGroup><ProjectReference Include=\"..\\B\\B.csproj\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(second, "B.csproj"), "<Project />");

            var projects = ProjectGraphReader.ReadProjectTree(root);
            var source = Assert.Single(projects, project => project.Path == "A/A.csproj");
            Assert.Equal("B/B.csproj", Assert.Single(source.References).Target);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MarksConditionalProjectReference()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sati-project-graph-{Guid.NewGuid():N}");
        try
        {
            var first = Path.Combine(root, "A");
            var second = Path.Combine(root, "B");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, "A.csproj"),
                "<Project><ItemGroup Condition=\"'$(Configuration)' == 'Debug'\"><ProjectReference Include=\"..\\B\\B.csproj\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(second, "B.csproj"), "<Project />");

            var projects = ProjectGraphReader.ReadProjectTree(root);
            var source = Assert.Single(projects, project => project.Path == "A/A.csproj");
            Assert.True(Assert.Single(source.References).IsConditional);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
