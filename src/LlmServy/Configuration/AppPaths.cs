namespace LlmServy.Configuration;

public sealed class AppPaths
{
    public string DataDirectory
    {
        get;
    }
    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    public string BridgeFile => Path.Combine(AppContext.BaseDirectory, "wsl", "wsl-bridge.sh");

    public AppPaths(string? dataDirectory = null)
    {
        DataDirectory = Path.GetFullPath(dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "llm-servy"));
    }
}
