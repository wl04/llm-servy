using System.Text.Json;
using LlmServy.Services;

namespace LlmServy.Configuration;

public sealed class SettingsStore(AppPaths paths)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, RespectNullableAnnotations = true };
    public AppSettings Load()
    {
        try
        {
            return Deserialize(File.ReadAllText(paths.SettingsFile));
        }
        catch (FileNotFoundException) { return new AppSettings(); }
        catch (DirectoryNotFoundException) { return new AppSettings(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new AppException(new("SettingsReadFailed"), error);
        }
    }

    private static AppSettings Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, Options)
                ?? throw new AppException(new("SettingsEmpty"));
        }
        catch (JsonException error) { throw new AppException(new("SettingsReadFailed"), error); }
    }

    public void Save(AppSettings settings) =>
        new AtomicSettingsFile().Write(paths.SettingsFile, JsonSerializer.Serialize(settings, Options), overwrite: true);

    public static AppSettings ReadLegacy(string legacyFile)
    {
        legacyFile = Path.GetFullPath(legacyFile);
        var text = File.ReadAllText(legacyFile);
        var result = Deserialize(text);
        using var document = JsonDocument.Parse(text);
        if (string.IsNullOrEmpty(result.PresetPath) && document.RootElement.TryGetProperty("SelectedPreset", out var selected))
        {
            string path = selected.GetString() ?? "";
            if (path.Length > 0)
                result.PresetPath = Path.GetFullPath(path, Path.GetDirectoryName(legacyFile) ?? throw new AppException(new("InvalidIni")));
        }
        return result;
    }
}
