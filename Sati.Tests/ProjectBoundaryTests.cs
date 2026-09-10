using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace Sati.Tests;

public sealed class ProjectBoundaryTests
{
    [Fact]
    public void RepositorySolutionUsesThePlatformNameAndProductFolders()
    {
        var repositoryRoot = FindRepositoryRoot();
        var solutionPath = Path.Combine(repositoryRoot, "SatiLogica.slnx");

        Assert.True(File.Exists(solutionPath));
        Assert.False(File.Exists(Path.Combine(repositoryRoot, "Sati.slnx")));

        var solution = XDocument.Load(solutionPath);
        var folders = solution.Root!
            .Elements("Folder")
            .ToDictionary(folder => (string)folder.Attribute("Name")!);

        Assert.Equal(
            ["/karuna/", "/platform/", "/sati/", "/upekkha/"],
            folders.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(13, folders["/sati/"].Elements("Project").Count());
        Assert.Empty(folders["/platform/"].Elements("Project"));
        Assert.Empty(folders["/karuna/"].Elements("Project"));
        Assert.Empty(folders["/upekkha/"].Elements("Project"));
    }

    [Fact]
    public void DesktopProjectExcludesStandaloneToolProjects()
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), "Sati.csproj");
        var project = XDocument.Load(projectPath);

        AssertRemove(project, "Compile", @"tools\**\*.cs");
        AssertRemove(project, "EmbeddedResource", @"tools\**\*");
        AssertRemove(project, "None", @"tools\**\*");
    }

    private static void AssertRemove(XDocument project, string itemName, string expectedPattern)
    {
        var patterns = project
            .Descendants(itemName)
            .Select(item => (string?)item.Attribute("Remove"))
            .Where(pattern => pattern is not null);

        Assert.Contains(expectedPattern, patterns);
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourcePath = "") =>
        Directory.GetParent(Path.GetDirectoryName(sourcePath)!)!.FullName;
}
