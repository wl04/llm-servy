using LlmServy.Services;

namespace LlmServy.Configuration;

public sealed class SettingsMigration(AppPaths paths)
{
    /// <summary>Copies legacy settings once, preserving the original and never replacing existing settings.</summary>
    public void ImportIfMissing(string legacyFile)
    {
        if (File.Exists(paths.SettingsFile) || !File.Exists(legacyFile))
            return;
        try
        {
            var imported = SettingsStore.ReadLegacy(legacyFile);
            imported.MigratePaths();
            var json = System.Text.Json.JsonSerializer.Serialize(imported,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            new AtomicSettingsFile().Write(paths.SettingsFile, json, overwrite: false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or AppException or AggregateException)
        {
            throw new AppException(new("MigrationFailed"), error);
        }
    }
}
