using Sati.Data;
using System.IO;
using System.Text.Json;

namespace Sati.Services;

/// <summary>The three ways the Annual Forms overview can show a plan year.</summary>
public enum AnnualFormsView
{
    List,
    Timeline,
    ByPurpose
}

/// <summary>
/// Remembers which Annual Forms overview view each Sati user last chose, per environment,
/// in the current Windows profile. Local presentation state, not agency data. Its own file
/// for the reason ConsumerPickerSortPreferenceService gives: a damaged file costs only this
/// one setting. Failures are never surfaced as errors; the overview simply opens on List.
/// </summary>
public class AnnualFormsViewPreferenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly DataEnvironmentInfo _environment;
    private readonly string _preferencePath;
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    public AnnualFormsViewPreferenceService(DataEnvironmentInfo environment)
        : this(
            environment,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sati",
                "annual-forms-view-preferences.json"))
    {
    }

    internal AnnualFormsViewPreferenceService(DataEnvironmentInfo environment, string preferencePath)
    {
        _environment = environment;
        _preferencePath = preferencePath;
    }

    public virtual async Task<AnnualFormsView> LoadForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            return AnnualFormsView.List;
        await _fileGate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentAsync(cancellationToken);
            return document.Profiles.TryGetValue(ProfileKey(userId), out var value) &&
                   Enum.TryParse<AnnualFormsView>(value, ignoreCase: false, out var view) &&
                   Enum.IsDefined(view)
                ? view
                : AnnualFormsView.List;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return AnnualFormsView.List;
        }
        finally
        {
            _fileGate.Release();
        }
    }

    /// <summary>Returns false when the choice could not be saved; the view still changes for this session.</summary>
    public virtual async Task<bool> SaveForUserAsync(int userId, AnnualFormsView view, CancellationToken cancellationToken = default)
    {
        if (userId <= 0 || !Enum.IsDefined(view))
            return false;
        await _fileGate.WaitAsync(cancellationToken);
        try
        {
            PreferenceDocument document;
            try
            {
                document = await ReadDocumentAsync(cancellationToken);
            }
            catch (JsonException)
            {
                document = new PreferenceDocument(); // A damaged file is replaced; it held only this setting.
            }

            document.Profiles[ProfileKey(userId)] = view.ToString();
            var directory = Path.GetDirectoryName(_preferencePath)
                ?? throw new IOException("The Annual Forms preference folder is unavailable.");
            Directory.CreateDirectory(directory);
            var temporary = _preferencePath + $".pending-{Guid.NewGuid():N}";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                    await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                File.Move(temporary, _preferencePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private async Task<PreferenceDocument> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_preferencePath))
            return new PreferenceDocument();
        await using var stream = File.OpenRead(_preferencePath);
        var document = await JsonSerializer.DeserializeAsync<PreferenceDocument>(stream, JsonOptions, cancellationToken)
            ?? new PreferenceDocument();
        document.Profiles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return document;
    }

    private string ProfileKey(int userId) => $"{_environment.Environment}:{userId}";

    private sealed class PreferenceDocument
    {
        public Dictionary<string, string> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
