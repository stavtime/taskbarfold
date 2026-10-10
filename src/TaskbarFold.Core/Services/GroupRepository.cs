using System.Text;
using System.Text.Json;
using TaskbarFold.Core.Models;
using TaskbarFold.Core.Serialization;

namespace TaskbarFold.Core.Services;

/// <summary>
/// Reads and writes <c>groups.json</c>. Writes go through a temporary file in the same
/// directory followed by <see cref="File.Replace(string, string, string)"/>, so the config
/// is either the old document or the new one and never a half-written mix.
/// </summary>
public sealed class GroupRepository : IGroupRepository
{
    private readonly AppPaths _paths;
    private readonly object _gate = new();
    private bool _isReadOnly;

    public GroupRepository(AppPaths? paths = null) => _paths = paths ?? AppPaths.Default;

    public string ConfigFilePath => _paths.ConfigFile;

    /// <summary>
    /// The configuration as it was before the most recent save. Kept so a mistake is
    /// always one file copy away from being undone.
    /// </summary>
    public string PreviousConfigFilePath => _paths.ConfigFile + ".bak";

    public ConfigLoadResult Load()
    {
        lock (_gate)
        {
            if (!File.Exists(ConfigFilePath))
            {
                _isReadOnly = false;
                return new ConfigLoadResult(new GroupsDocument(), ConfigLoadStatus.Created);
            }

            string json;
            try
            {
                json = ReadWithRetry(ConfigFilePath);
            }
            catch (IOException ex)
            {
                return new ConfigLoadResult(new GroupsDocument(), ConfigLoadStatus.Recovered, null, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return new ConfigLoadResult(new GroupsDocument(), ConfigLoadStatus.Recovered, null, ex.Message);
            }

            GroupsDocument? document;
            try
            {
                document = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.GroupsDocument);
            }
            catch (JsonException ex)
            {
                var backup = QuarantineCorruptFile();
                return new ConfigLoadResult(new GroupsDocument(), ConfigLoadStatus.Recovered, backup, ex.Message);
            }

            if (document is null)
            {
                var backup = QuarantineCorruptFile();
                return new ConfigLoadResult(
                    new GroupsDocument(),
                    ConfigLoadStatus.Recovered,
                    backup,
                    "The configuration file contained no document.");
            }

            var outcome = ConfigMigrator.Migrate(document);
            _isReadOnly = outcome == MigrationOutcome.TooNew;

            return outcome switch
            {
                // Return a usable empty document, but block all saves until a compatible
                // configuration has been loaded, including background update-check saves.
                MigrationOutcome.TooNew => new ConfigLoadResult(
                    new GroupsDocument(),
                    ConfigLoadStatus.TooNew,
                    null,
                    $"Config version {document.Version} was written by a newer version of TaskbarFold."),
                MigrationOutcome.Upgraded => new ConfigLoadResult(document, ConfigLoadStatus.Migrated),
                _ => new ConfigLoadResult(document, ConfigLoadStatus.Loaded),
            };
        }
    }

    public void Save(GroupsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (_gate)
        {
            if (_isReadOnly || document.Version > GroupsDocument.CurrentVersion)
            {
                throw new ConfigReadOnlyException();
            }

            document.Version = GroupsDocument.CurrentVersion;

            var directory = Path.GetDirectoryName(ConfigFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(document, ConfigJsonContext.Default.GroupsDocument);

            // The temporary file must sit next to the target: File.Replace cannot cross volumes.
            var temp = ConfigFilePath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (File.Exists(ConfigFilePath))
            {
                // File.Replace can retain the outgoing file for free, so always keep one
                // previous version. Groups represent real effort to assemble, and a bad
                // write — or something outside this application clobbering the file —
                // should never be unrecoverable.
                File.Replace(temp, ConfigFilePath, PreviousConfigFilePath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, ConfigFilePath);
            }
        }
    }

    /// <summary>
    /// The resident writes while a settings window or an editor may still hold the file
    /// open for a moment; a few short retries avoid a spurious failure.
    /// </summary>
    private static string ReadWithRetry(string path)
    {
        const int attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private string? QuarantineCorruptFile()
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var backup = Path.Combine(
                Path.GetDirectoryName(ConfigFilePath) ?? _paths.Root,
                $"groups.corrupt-{stamp}.json");

            File.Move(ConfigFilePath, backup, overwrite: true);
            return backup;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
