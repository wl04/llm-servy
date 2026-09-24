using LlmServy.Configuration;
using LlmServy.Models;
using LlmServy.Services;

namespace LlmServy.Runtime;

public sealed record RuntimeSnapshot(ServiceStatus Llama, ServiceStatus Harness, bool InterfaceReady);

/// <summary>OS/network boundary for one session. Only acquired process handles may be stopped.</summary>
public interface ILauncherRuntime : IDisposable
{
    event Action<RuntimeSnapshot>? Changed;
    TerminalSession? Terminal => null;
    string? BrowserUrl
    {
        get;
    }
    string RootUrl
    {
        get;
    }
    bool HasProcesses
    {
        get;
    }
    IReadOnlyList<string> StoppedServices
    {
        get;
    }
    /// <summary>Starts one session; the caller serializes transitions and must clean up partial startup on failure.</summary>
    /// <exception cref="OperationCanceledException">Startup was cancelled.</exception>
    Task StartAsync(AppSettings settings, Preset preset, CancellationToken token);
    /// <summary>Reads current readiness without publishing state changes. The token cancels probes.</summary>
    Task<RuntimeSnapshot> GetSnapshotAsync(CancellationToken token);
    /// <summary>Stops acquired processes, attempts each cleanup, and retains failures for retry.</summary>
    /// <remarks>Cleanup uses bounded shutdown waits and is not cancelled with startup.</remarks>
    Task StopAsync();
}
