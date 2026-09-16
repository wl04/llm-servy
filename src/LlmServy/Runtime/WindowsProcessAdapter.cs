using LlmServy.Processes;
using LlmServy.Services;

namespace LlmServy.Runtime;

/// <summary>Adapts the atomic native process creation API, whose result is an owned handle.</summary>
public sealed class WindowsProcessAdapter(TimeProvider time)
{
    public static string GetExecutable(string explicitPath, string name)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : throw new AppException(new("MissingExecutable", Path.GetFileName(explicitPath)));
        if (name == "wsl.exe")
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Concat(new[] { Path.Combine(local, "Microsoft", "WinGet", "Links"), Path.Combine(local, "Microsoft", "WindowsApps") });
        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;
            var candidate = Path.Combine(directory.Trim('"'), name);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }
        throw new AppException(new("MissingExecutable", name));
    }

    // External command execution inherently returns output; kept inside this OS adapter.
    public async Task<string> ExecuteWslAsync(string[] arguments, CancellationToken token)
    {
        var output = new System.Text.StringBuilder();
        using var child = OwnedProcess.Start(GetExecutable("", "wsl.exe"), arguments, AppContext.BaseDirectory,
            (channel, line) => { if (channel == "stdout") lock (output) output.AppendLine(line); });
        var started = time.GetTimestamp();
        while (child.Running)
        {
            token.ThrowIfCancellationRequested();
            if (time.GetElapsedTime(started) > TimeSpan.FromSeconds(30))
                throw new AppException(new("WslTimeout"));
            await Task.Delay(TimeSpan.FromMilliseconds(100), time, token);
        }
        await child.DrainAsync(token).WaitAsync(TimeSpan.FromSeconds(5), time, token);
        if (child.ExitCode != 0)
            throw new AppException(new("WslFailed", child.ExitCode));
        lock (output)
            return output.ToString().Trim();
    }
}

public sealed class OwnedServiceProcess(OwnedProcess process, bool bridge) : IServiceLifetime
{
    public async Task StopAsync()
    {
        if (bridge)
            await process.StopBridgeAsync();
        else
            process.Dispose();
        await process.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }
}
