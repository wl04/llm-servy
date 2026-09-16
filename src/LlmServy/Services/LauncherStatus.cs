namespace LlmServy.Services;

public enum ServiceState
{
    NotStarted, Checking, Starting, Loading, Ready, Unavailable, Stopping, Stopped, Failed
}
public enum LaunchPhase
{
    Idle, Starting, Running, Stopping, Failed
}
public sealed record ServiceStatus(ServiceState State = ServiceState.NotStarted, bool AlreadyRunning = false);
public sealed record LauncherStatus(
    ServiceStatus Llama, ServiceStatus Dsh, LaunchPhase Phase, AppMessage Message,
    bool BrowserReady = false, Exception? Failure = null)
{
    public bool IsBusy => Phase is LaunchPhase.Starting or LaunchPhase.Stopping;
    public bool IsActive => Phase == LaunchPhase.Running;
    public static LauncherStatus Initial => new(new(), new(), LaunchPhase.Idle, new("Idle"));
}
