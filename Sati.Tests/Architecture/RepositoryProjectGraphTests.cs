using Xunit;

namespace Sati.Tests.Architecture;

public sealed class RepositoryProjectGraphTests
{
    [Fact]
    public void MatchesAllProjectFilesAndReferences()
    {
        var root = FindRepositoryRoot();
        var approved = ProjectGraphReader.ReadManifest(
            Path.Combine(root, "architecture", "project-graph.json"));
        var observed = ProjectGraphReader.ReadProjectTree(root);
        Assert.NotEmpty(approved);
        Assert.NotEmpty(observed);

        var source = approved.First(project => project.References.Count > 0);
        var modified = approved.Select(project => project.Path == source.Path
            ? project with { References = project.References.Skip(1).ToArray() }
            : project).ToArray();
        Assert.Contains(ProjectGraphGuard.Check(modified, observed),
            diagnostic => diagnostic.Code == "UnapprovedReference");

        var extra = observed.Append(new ObservedProject(
            "synthetic/Unlisted/Unlisted.csproj", [])).ToArray();
        Assert.Contains(ProjectGraphGuard.Check(approved, extra),
            diagnostic => diagnostic.Code == "UnlistedProject");

        var diagnostics = ProjectGraphGuard.Check(approved, observed);
        Assert.True(diagnostics.Count == 0,
            "Project graph violations: " + string.Join("; ", diagnostics.Select(d => d.Message)));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SatiLogica.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the Sati repository root.");
    }
}
