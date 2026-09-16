using LlmServy.Configuration;
using LlmServy.Localization;

namespace LlmServy.Services;

public sealed class LauncherLog(AppPaths paths, Localizer localizer)
{
    private const long MaximumLogBytes = 10 * 1024 * 1024;
    private readonly object gate = new();
    public event Action<string>? LineWritten;
    public void WriteMessage(string source, AppMessage message) => Write(source, $"[{message.Code}] {localizer.Format(message)}");
    public void WriteError(string source, Exception error) => Write(source, localizer.Error(error));

    public void Write(string source, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  [{source}] {DshEndpoint.Redact(message)}";
        string? failure = null;
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(paths.LogsDirectory);
                var file = Path.Combine(paths.LogsDirectory, "launcher.log");
                if (File.Exists(file) && new FileInfo(file).Length > MaximumLogBytes)
                    File.Move(file, file + ".1", true);
                File.AppendAllText(file, line + Environment.NewLine);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                failure = localizer.Get("LogWriteFailed", error.GetType().Name);
            }
        }
        LineWritten?.Invoke(line);
        if (failure is not null)
            LineWritten?.Invoke(failure);
    }
}
