using LlmServy.Configuration;
using LlmServy.Models;
using LlmServy.Runtime;

namespace LlmServy.Services;

/// <summary>Serializes session transitions; configuration is captured before asynchronous work.</summary>
public sealed class LauncherController(ILauncherRuntime runtime, LauncherLog log) : IDisposable
{
    private readonly SemaphoreSlim transition = new(1, 1);
    private readonly object cancellationGate = new();
    private CancellationTokenSource? startupCancellation;
    public LauncherStatus Status { get; private set; } = LauncherStatus.Initial;
    public event Action<LauncherStatus>? StatusChanged;
    public bool CanStart => !Status.IsBusy && !Status.IsActive && !runtime.HasProcesses;
    public bool CanStop => Status.IsBusy || Status.IsActive || runtime.HasProcesses;
    public string? BrowserUrl => Status.BrowserReady ? runtime.BrowserUrl : null;

    private void Set(LauncherStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }
    private void OnRuntimeChanged(RuntimeSnapshot snapshot) => Set(Status with { Llama = snapshot.Llama, Dsh = snapshot.Dsh, BrowserReady = snapshot.BrowserReady });
    private LauncherStatus GetStoppedStatus() => Status with
    {
        Llama = runtime.StoppedServices.Contains("llama.cpp") ? new(ServiceState.Stopped) : Status.Llama,
        Dsh = runtime.StoppedServices.Contains("DeepSeek Harness") ? new(ServiceState.Stopped) : Status.Dsh
    };

    public async Task StartAsync(AppSettings settings, Preset preset)
    {
        if (!await transition.WaitAsync(0))
            return;
        try
        {
            if (!CanStart)
                return;
            var snapshot = settings with
            {
            };
            SettingsValidator.ValidateValues(snapshot);
            if (!Path.GetFullPath(snapshot.PresetPath).Equals(preset.IniPath, StringComparison.OrdinalIgnoreCase))
                throw new AppException(new("WrongPreset"));
            lock (cancellationGate)
                startupCancellation = new();
            runtime.Changed += OnRuntimeChanged;
            Set(LauncherStatus.Initial with
            {
                Phase = LaunchPhase.Starting,
                Message = new("Starting")
            });
            await runtime.StartAsync(snapshot, preset, startupCancellation.Token);
            Set(Status with
            {
                Phase = LaunchPhase.Running,
                BrowserReady = true,
                Message = new("Ready", runtime.RootUrl, preset.ModelId)
            });
            log.WriteMessage("launcher", new("AllReady"));
        }
        catch (Exception error)
        {
            Exception failure = error;
            try
            {
                await runtime.StopAsync();
            }
            catch (Exception stopError) { failure = new AggregateException(error, stopError); }
            var message = error is OperationCanceledException && failure == error ? new AppMessage("Cancelled") : new AppMessage("UnexpectedError", error.GetType().Name);
            Set(GetStoppedStatus() with
            {
                Phase = LaunchPhase.Failed,
                BrowserReady = false,
                Message = message,
                Failure = failure
            });
            log.WriteError("launcher", failure);
        }
        finally
        {
            runtime.Changed -= OnRuntimeChanged;
            lock (cancellationGate)
            {
                startupCancellation?.Dispose();
                startupCancellation = null;
            }
            transition.Release();
        }
    }

    public async Task StopAsync()
    {
        lock (cancellationGate)
            startupCancellation?.Cancel();
        await transition.WaitAsync();
        try
        {
            if (!CanStop)
                return;
            Set(Status with
            {
                Phase = LaunchPhase.Stopping,
                BrowserReady = false,
                Message = new("Stopping"),
                Failure = null
            });
            await runtime.StopAsync();
            var stopped = runtime.StoppedServices;
            var message = stopped.Count > 0 ? new AppMessage("StoppedServices", string.Join(", ", stopped)) : new AppMessage("NothingToStop");
            Set(GetStoppedStatus() with
            {
                Phase = LaunchPhase.Idle,
                Message = message
            });
            log.WriteMessage("launcher", message);
        }
        catch (Exception error)
        {
            Set(GetStoppedStatus() with
            {
                Phase = LaunchPhase.Failed,
                BrowserReady = false,
                Message = new("StopFailed"),
                Failure = error
            });
            log.WriteError("launcher", error);
        }
        finally { transition.Release(); }
    }

    /// <summary>Updates the presented status from current probes; cancellation leaves the last status intact.</summary>
    public async Task RefreshAsync(CancellationToken token = default)
    {
        if (!Status.IsActive || !await transition.WaitAsync(0, token))
            return;
        try
        {
            var snapshot = await runtime.GetSnapshotAsync(token);
            OnRuntimeChanged(snapshot);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) { log.WriteError("launcher", error); }
        finally { transition.Release(); }
    }

    public void Dispose() => runtime.Dispose();
}
