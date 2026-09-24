namespace LlmServy.Configuration;

/// <summary>Persisted configuration. Names remain stable across UI languages.</summary>
public sealed record AppSettings
{
    public string HarnessKind { get; set; } = "dsh";
    public string PiDirectory { get; set; } = "/home";
    public string PiExecutable { get; set; } = "pi";
    public string PiProvider { get; set; } = "llama-local";
    public bool OpenPiTerminal { get; set; } = true;
    public string PresetPath { get; set; } = "";
    public string SelectedModelId { get; set; } = "";
    public string ModelsDirectory { get; set; } = "";
    public string LlamaDirectory { get; set; } = "";
    public string LlamaExecutable { get; set; } = "";
    public string Distro { get; set; } = "Ubuntu";
    public string WslDirectory { get; set; } = "/mnt/c/llm-models";
    public int LlamaPort { get; set; } = 1234;
    public string BindAddress { get; set; } = "0.0.0.0";
    public int DshPort { get; set; } = 3080;
    public string DshPackage { get; set; } = "@deepseek-ai/dsh@0.1.5-rc.1";
    public int TimeoutSeconds { get; set; } = 600;
    public bool OpenBrowser { get; set; } = true;
    public string Language { get; set; } = "system";

    public void MigratePaths()
    {
        if (string.IsNullOrEmpty(ModelsDirectory) && !string.IsNullOrEmpty(PresetPath))
            ModelsDirectory = Path.GetDirectoryName(PresetPath) ?? "";
        if (string.IsNullOrEmpty(LlamaDirectory) && !string.IsNullOrEmpty(LlamaExecutable))
            LlamaDirectory = Path.GetDirectoryName(LlamaExecutable) ?? "";
    }

    // Compatibility entry point; filesystem validation is owned by the boundary validator.
    public void Validate(bool requirePreset = true) => SettingsValidator.Validate(this, requirePreset);
    public string ResolveLlamaExecutable() => SettingsValidator.GetLlamaExecutable(this);
}
