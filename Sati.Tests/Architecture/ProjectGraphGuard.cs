using System.Text.Json;
using System.Xml.Linq;

namespace Sati.Tests.Architecture;

internal sealed record ApprovedProject(
    string Path,
    string Product,
    string Kind,
    IReadOnlyList<string> References);

internal sealed record ObservedReference(string Target, bool IsConditional = false);

internal sealed record ObservedProject(
    string Path,
    IReadOnlyList<ObservedReference> References);

internal sealed record GraphDiagnostic(string Code, string Message);

internal static class ProjectGraphGuard
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> Products =
        new(["platform", "sati", "karuna"], StringComparer.Ordinal);
    private static readonly HashSet<string> Kinds =
        new(["library", "host", "client", "test", "tool"], StringComparer.Ordinal);

    public static IReadOnlyList<GraphDiagnostic> Check(
        IReadOnlyList<ApprovedProject> approved,
        IReadOnlyList<ObservedProject> observed)
    {
        var diagnostics = new List<GraphDiagnostic>();
        var approvedByPath = new Dictionary<string, ApprovedProject>(PathComparer);
        var observedByPath = new Dictionary<string, ObservedProject>(PathComparer);

        foreach (var project in approved)
        {
            if (!approvedByPath.TryAdd(project.Path, project))
                Add("DuplicateProject", $"Manifest repeats {project.Path}.");

            if (!Products.Contains(project.Product) || !Kinds.Contains(project.Kind))
                Add("InvalidClassification", $"{project.Path} has invalid product or kind.");

            var name = Path.GetFileNameWithoutExtension(project.Path);
            if ((name.StartsWith("SatiLogica.", StringComparison.OrdinalIgnoreCase) &&
                 project.Product != "platform") ||
                (name.StartsWith("Karuna.", StringComparison.OrdinalIgnoreCase) &&
                 project.Product != "karuna"))
                Add("ProductClassification",
                    $"{project.Path} has product {project.Product}, contrary to its project prefix.");
        }

        foreach (var project in observed)
        {
            if (!observedByPath.TryAdd(project.Path, project))
                Add("DuplicateObservedProject", $"Project scan repeats {project.Path}.");
            if (!approvedByPath.ContainsKey(project.Path))
                Add("UnlistedProject", $"{project.Path} is not in the manifest.");
        }

        foreach (var source in approved)
        {
            if (!observedByPath.TryGetValue(source.Path, out var actual))
            {
                Add("MissingProject", $"Manifest lists absent project {source.Path}.");
                continue;
            }

            var approvedEdges = new HashSet<string>(source.References, PathComparer);
            var actualEdges = new HashSet<string>(
                actual.References.Select(reference => reference.Target), PathComparer);

            foreach (var targetPath in approvedEdges.Except(actualEdges, PathComparer))
                Add("StaleApprovedReference",
                    $"Manifest approves {source.Path} -> {targetPath}, but the edge is absent.");

            foreach (var reference in actual.References)
            {
                var targetPath = reference.Target;
                if (reference.IsConditional)
                    Add("ConditionalReference",
                        $"{source.Path} -> {targetPath} is conditional; the reader cannot evaluate it.");

                if (!approvedEdges.Contains(targetPath))
                    Add("UnapprovedReference",
                        $"{source.Path} -> {targetPath} is not approved in the manifest.");

                if (!observedByPath.ContainsKey(targetPath))
                    Add("MissingReferenceTarget",
                        $"{source.Path} references absent project {targetPath}.");

                if (!approvedByPath.TryGetValue(targetPath, out var target))
                    continue;

                if ((source.Product == "sati" && target.Product == "karuna") ||
                    (source.Product == "karuna" && target.Product == "sati"))
                    Add("ProductBoundary",
                        $"{source.Path} -> {targetPath} crosses Sati and Karuna.");

                if (source.Product == "platform" && target.Product != "platform" &&
                    !IsNamed(source.Path, "SatiLogica.Schema"))
                    Add("PlatformBoundary",
                        $"{source.Path} -> {targetPath} makes platform depend on a product.");

                if (IsNamed(targetPath, "SatiLogica.Schema") &&
                    source.Kind != "test" && !IsNamed(source.Path, "SatiLogica.Migrator"))
                    Add("SchemaConsumer",
                        $"{source.Path} may not reference the composition assembly {targetPath}.");

                if (source.Kind == "test" && target.Product != source.Product &&
                    target.Product != "platform")
                    Add("TestBoundary",
                        $"Test project {source.Path} references another product: {targetPath}.");

                if (target.Kind == "tool" && source.Kind != "tool")
                    Add("ToolConsumer",
                        $"Non-tool project {source.Path} references tool {targetPath}.");

                if (IsNamed(source.Path, "Karuna.Web") &&
                    !IsNamed(targetPath, "Karuna.Contracts") &&
                    !IsNamed(targetPath, "SatiLogica.Contracts"))
                    Add("KarunaWebBoundary",
                        $"Karuna.Web may not reference {targetPath}.");
            }
        }

        return diagnostics;

        void Add(string code, string message) => diagnostics.Add(new GraphDiagnostic(code, message));
    }

    private static bool IsNamed(string projectPath, string name) =>
        Path.GetFileNameWithoutExtension(projectPath)
            .Equals(name, StringComparison.OrdinalIgnoreCase);
}

internal static class ProjectGraphReader
{
    private static readonly HashSet<string> ExcludedDirectories =
        new(["bin", "obj", ".codex-build", ".vs", "tmp", ".git"],
            StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ApprovedProject> ReadManifest(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("version").GetInt32() != 1)
            throw new InvalidDataException("Unsupported project graph manifest version.");

        return root.GetProperty("projects").EnumerateArray().Select(project =>
            new ApprovedProject(
                project.GetProperty("path").GetString()!,
                project.GetProperty("product").GetString()!,
                project.GetProperty("kind").GetString()!,
                project.GetProperty("references").EnumerateArray()
                    .Select(reference => reference.GetString()!).ToArray()))
            .ToArray();
    }

    public static IReadOnlyList<ObservedProject> ReadProjectTree(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        var projects = new List<ObservedProject>();
        var pending = new Stack<string>();
        pending.Push(fullRoot);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!ExcludedDirectories.Contains(Path.GetFileName(child)))
                    pending.Push(child);
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.csproj"))
            {
                var document = XDocument.Load(file);
                var references = document.Descendants()
                    .Where(element => element.Name.LocalName == "ProjectReference")
                    .Select(element =>
                    {
                        var include = element.Attribute("Include")?.Value
                            ?? throw new InvalidDataException($"ProjectReference lacks Include in {file}.");
                        var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,
                            include.Replace('\\', Path.DirectorySeparatorChar)
                                .Replace('/', Path.DirectorySeparatorChar)));
                        var relativeTarget = Path.GetRelativePath(fullRoot, target)
                            .Replace('\\', '/');
                        if (relativeTarget == ".." ||
                            relativeTarget.StartsWith("../", StringComparison.Ordinal))
                            throw new InvalidDataException(
                                $"ProjectReference from {file} leaves the repository: {include}.");

                        var conditional = element.AncestorsAndSelf()
                            .Any(ancestor => ancestor.Attribute("Condition") is not null);
                        return new ObservedReference(relativeTarget, conditional);
                    })
                    .ToArray();

                projects.Add(new ObservedProject(
                    Path.GetRelativePath(fullRoot, file).Replace('\\', '/'), references));
            }
        }

        return projects.OrderBy(project => project.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
