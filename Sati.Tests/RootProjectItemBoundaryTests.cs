using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Sati.Tests;

public sealed class RootProjectItemBoundaryTests
{
    private static readonly string[] ItemTypes =
        ["Compile", "EmbeddedResource", "None", "Page", "Content"];

    [Theory]
    [InlineData("Debug")]
    [InlineData("Demo")]
    public async Task ExcludesKarunaAndPlatformItems(string configuration)
    {
        var root = FindRepositoryRoot();
        var temporaryRoot = Path.Combine(Path.GetTempPath(),
            $"sati-root-items-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(temporaryRoot);
            File.Copy(Path.Combine(root, "Sati.csproj"),
                Path.Combine(temporaryRoot, "Sati.csproj"));

            foreach (var directory in new[] { "karuna", "platform" })
            {
                var path = Path.Combine(temporaryRoot, directory);
                Directory.CreateDirectory(path);
                foreach (var extension in new[] { ".cs", ".resx", ".xaml", ".txt" })
                    File.WriteAllText(Path.Combine(path, "Sentinel" + extension), string.Empty);
            }

            File.WriteAllText(Path.Combine(temporaryRoot, "RootSentinel.cs"),
                "internal sealed class RootSentinel { }");

            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (string.IsNullOrWhiteSpace(dotnet))
                dotnet = "dotnet";

            var start = new ProcessStartInfo(dotnet)
            {
                WorkingDirectory = temporaryRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("msbuild");
            start.ArgumentList.Add(Path.Combine(temporaryRoot, "Sati.csproj"));
            start.ArgumentList.Add("-getItem:Compile,EmbeddedResource,None,Page,Content");
            start.ArgumentList.Add($"-property:Configuration={configuration}");

            using var process = Process.Start(start)!;
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("MSBuild item evaluation exceeded 30 seconds.");
            }

            var output = await standardOutput;
            var error = await standardError;
            Assert.True(process.ExitCode == 0,
                $"MSBuild evaluation failed ({process.ExitCode}). stderr: {error}\nstdout: {output}");

            using var document = JsonDocument.Parse(output);
            var items = document.RootElement.GetProperty("Items");
            var leaked = new List<string>();
            var rootControlPresent = false;

            foreach (var itemType in ItemTypes)
            {
                foreach (var item in items.GetProperty(itemType).EnumerateArray())
                {
                    var identity = item.GetProperty("Identity").GetString() ?? string.Empty;
                    var normalizedIdentity = identity.Replace('\\', '/');
                    var fullPath = item.TryGetProperty("FullPath", out var metadata)
                        ? metadata.GetString() ?? string.Empty
                        : string.Empty;
                    var normalizedFullPath = fullPath.Replace('\\', '/');

                    if (itemType == "Compile" &&
                        normalizedIdentity.Equals("RootSentinel.cs", StringComparison.OrdinalIgnoreCase))
                        rootControlPresent = true;

                    if (IsUnderSentinelDirectory(normalizedIdentity) ||
                        IsUnderSentinelDirectory(normalizedFullPath))
                        leaked.Add($"{itemType}: {identity}");
                }
            }

            Assert.True(rootControlPresent, "The root Compile control was absent.");
            Assert.True(leaked.Count == 0,
                $"{configuration} included nested sentinel items: {string.Join("; ", leaked)}");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static bool IsUnderSentinelDirectory(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("karuna/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("platform/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/karuna/Sentinel.", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/platform/Sentinel.", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Sati.csproj")) &&
                File.Exists(Path.Combine(directory.FullName, "SatiLogica.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the Sati repository root.");
    }
}
