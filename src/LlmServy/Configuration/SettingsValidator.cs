using System.Net;
using System.Text.RegularExpressions;
using LlmServy.Services;

namespace LlmServy.Configuration;

public static class SettingsValidator
{
    public static void ValidateValues(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.PresetPath is null || settings.SelectedModelId is null || settings.ModelsDirectory is null ||
            settings.LlamaDirectory is null || settings.LlamaExecutable is null || settings.WslDirectory is null)
            throw new AppException(new("SettingsReadFailed"));
        if (settings.HarnessKind is not ("dsh" or "pi"))
            throw new AppException(new("InvalidHarness"));
        if (settings.HarnessKind == "pi" && (string.IsNullOrWhiteSpace(settings.PiDirectory) || !settings.PiDirectory.StartsWith('/') ||
            string.IsNullOrWhiteSpace(settings.PiExecutable) || string.IsNullOrWhiteSpace(settings.PiProvider)))
            throw new AppException(new("InvalidPiSettings"));
        if (settings.LlamaPort is < 1 or > 65535 || (settings.HarnessKind == "dsh" && (settings.DshPort is < 1 or > 65535 || settings.LlamaPort == settings.DshPort)))
            throw new AppException(new("InvalidPorts"));
        if (!IPAddress.TryParse(settings.BindAddress, out _))
            throw new AppException(new("InvalidAddress"));
        if (string.IsNullOrWhiteSpace(settings.Distro) || (settings.HarnessKind == "dsh" && !settings.WslDirectory.StartsWith('/')))
            throw new AppException(new("InvalidWsl"));
        if (settings.TimeoutSeconds is < 10 or > 3600)
            throw new AppException(new("InvalidTimeout"));
        if (settings.HarnessKind == "dsh" && !Regex.IsMatch(settings.DshPackage ?? "", @"^@deepseek-ai/dsh@\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$"))
            throw new AppException(new("InvalidPackage"));
        if (settings.Language is not ("system" or "en" or "ru"))
            throw new AppException(new("InvalidLanguage"));
        if (!string.IsNullOrEmpty(settings.LlamaExecutable) && !Path.IsPathFullyQualified(settings.LlamaExecutable))
            throw new AppException(new("InvalidExecutable"));
    }

    public static void Validate(AppSettings settings, bool requirePreset = true)
    {
        ValidateValues(settings);
        if (!string.IsNullOrEmpty(settings.ModelsDirectory) && (!Path.IsPathFullyQualified(settings.ModelsDirectory) || !Directory.Exists(settings.ModelsDirectory)))
            throw new AppException(new("InvalidModelsDirectory"));
        if (!string.IsNullOrEmpty(settings.LlamaDirectory))
            _ = GetLlamaExecutable(settings);
        if ((requirePreset || !string.IsNullOrEmpty(settings.PresetPath)) &&
            (!Path.IsPathFullyQualified(settings.PresetPath) || !File.Exists(settings.PresetPath) || !Path.GetExtension(settings.PresetPath).Equals(".ini", StringComparison.OrdinalIgnoreCase)))
            throw new AppException(new("InvalidIni"));
    }

    public static string GetLlamaExecutable(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.LlamaDirectory))
            return settings.LlamaExecutable;
        if (!Path.IsPathFullyQualified(settings.LlamaDirectory))
            throw new AppException(new("InvalidExecutable"));
        if (!string.IsNullOrEmpty(settings.LlamaExecutable) && string.Equals(Path.GetDirectoryName(settings.LlamaExecutable), settings.LlamaDirectory, StringComparison.OrdinalIgnoreCase) && File.Exists(settings.LlamaExecutable))
            return settings.LlamaExecutable;
        foreach (var name in new[] { "llama-server.exe", "llama.exe" })
        {
            var candidate = Path.Combine(settings.LlamaDirectory, name);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new AppException(new("NoLlamaExecutable"));
    }
}
