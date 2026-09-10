using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Markup;
using Sati.Contracts.V1;

namespace Sati.Services;

internal static class AppErrorLog
{
    private static readonly object Sync = new();
    private const long MaximumFileBytes = 5 * 1024 * 1024;
    private const long MaximumDirectoryBytes = 50 * 1024 * 1024;
    private const int RetentionDays = 30;

    internal static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SatiLogica",
        "Sati",
        "Logs");

    public static void EnsureReady(string? directoryOverride = null)
    {
        try
        {
            lock (Sync)
            {
                var directory = directoryOverride ?? DefaultDirectory;
                Directory.CreateDirectory(directory);
                Prune(directory);
            }
        }
        catch (Exception loggingException)
        {
            Debug.WriteLine($"Sati diagnostic folder could not be prepared. Logger failure type: {loggingException.GetType().FullName}");
        }
    }

    public static string Record(
        Exception exception,
        string area,
        string? directoryOverride = null,
        string? referenceOverride = null)
    {
        var reference = string.IsNullOrWhiteSpace(referenceOverride)
            ? Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()
            : referenceOverride;
        try
        {
            var directory = directoryOverride ?? DefaultDirectory;
            var entry = BuildEntry(exception, area, reference);
            lock (Sync)
            {
                Directory.CreateDirectory(directory);
                Prune(directory);
                var path = WritablePath(directory, DateTime.UtcNow, Environment.ProcessId);
                File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine);
                PruneToDirectoryLimit(directory);
            }
        }
        catch (Exception loggingException)
        {
            Debug.WriteLine(
                $"Sati error {reference} could not be written. Logger failure type: {loggingException.GetType().FullName}");
        }

        return reference;
    }

    /// <summary>
    /// Writes only the curated Windows metadata contract. Event 1026 message text and Event 1000
    /// paths are absent by construction and therefore cannot leak into this workstation record.
    /// </summary>
    public static void RecordCrashDiagnostic(
        string reference,
        CrashDiagnosticDto diagnostic,
        string? directoryOverride = null)
    {
        try
        {
            if (!CrashDiagnosticRules.IsValid(diagnostic))
                return;
            var directory = directoryOverride ?? DefaultDirectory;
            var entry = new
            {
                timestampUtc = DateTime.UtcNow,
                reference,
                area = "application.previous-session-unclean.windows-readback",
                applicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                crashDiagnostic = diagnostic
            };
            lock (Sync)
            {
                Directory.CreateDirectory(directory);
                Prune(directory);
                var path = WritablePath(directory, DateTime.UtcNow, Environment.ProcessId);
                File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine);
                PruneToDirectoryLimit(directory);
            }
        }
        catch (Exception loggingException)
        {
            Debug.WriteLine(
                $"Sati crash diagnostic {reference} could not be written. Logger failure type: {loggingException.GetType().FullName}");
        }
    }

    private static string WritablePath(string directory, DateTime nowUtc, int processId)
    {
        var stem = $"sati-{nowUtc:yyyyMMdd}-{processId}";
        var path = Path.Combine(directory, stem + ".jsonl");
        if (!File.Exists(path) || new FileInfo(path).Length < MaximumFileBytes)
            return path;

        for (var part = 1; part < 10_000; part++)
        {
            path = Path.Combine(directory, $"{stem}-{part:000}.jsonl");
            if (!File.Exists(path) || new FileInfo(path).Length < MaximumFileBytes)
                return path;
        }

        throw new IOException("The Sati diagnostic log reached its rotation limit.");
    }

    private static void Prune(string directory)
    {
        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
        foreach (var file in DiagnosticFiles(directory).Where(file => file.LastWriteTimeUtc < cutoff))
            TryDelete(file.FullName);
        PruneToDirectoryLimit(directory);
    }

    private static void PruneToDirectoryLimit(string directory)
    {
        var files = DiagnosticFiles(directory)
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();
        var total = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (total <= MaximumDirectoryBytes)
                break;
            var length = file.Length;
            if (TryDelete(file.FullName))
                total -= length;
        }
    }

    private static IEnumerable<FileInfo> DiagnosticFiles(string directory) =>
        Directory.EnumerateFiles(directory, "sati-*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path));

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static object BuildEntry(Exception exception, string area, string reference) => new
    {
        timestampUtc = DateTime.UtcNow,
        reference,
        area = SafeArea(area),
        applicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
        exceptionType = exception.GetType().FullName,
        hResult = $"0x{exception.HResult:X8}",
        target = exception.TargetSite?.DeclaringType?.FullName,
        stackTrace = exception.StackTrace,
        innerExceptionType = exception.InnerException?.GetType().FullName,
        innerTarget = exception.InnerException?.TargetSite?.DeclaringType?.FullName,
        innerHResult = exception.InnerException is null
            ? null
            : $"0x{exception.InnerException.HResult:X8}",
        xamlLineNumber = exception is XamlParseException xaml ? (int?)xaml.LineNumber : null,
        xamlLinePosition = exception is XamlParseException xamlPosition ? (int?)xamlPosition.LinePosition : null
    };

    internal static string CreateFingerprint(Exception exception)
    {
        var shape = string.Join('|',
            exception.GetType().FullName,
            exception.HResult.ToString("X8"),
            exception.TargetSite?.DeclaringType?.FullName,
            exception.TargetSite?.Name,
            exception.InnerException?.GetType().FullName,
            exception.InnerException?.HResult.ToString("X8"),
            exception.InnerException?.TargetSite?.DeclaringType?.FullName,
            exception.InnerException?.TargetSite?.Name);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shape)));
    }

    internal static string SafeArea(string area)
    {
        var safe = new string((area ?? string.Empty)
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            .Take(80)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }
}
