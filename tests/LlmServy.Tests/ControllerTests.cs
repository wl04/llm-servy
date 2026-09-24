using LlmServy.Configuration;
using LlmServy.Localization;
using LlmServy.Models;
using LlmServy.Runtime;
using LlmServy.Services;

namespace LlmServy.Tests;

public sealed class ControllerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "LlmServy.Tests", Guid.NewGuid().ToString("N"));
    private static AppSettings Settings => new() { PresetPath = @"C:\models\test.ini", SelectedModelId = "test" };
    private static Preset Preset => new(Settings.PresetPath, "test");
    private LauncherController Create(FakeRuntime runtime) => new(runtime, new LauncherLog(new AppPaths(directory), new Localizer()));
    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    [Fact]
    public async Task StartAsync_IgnoresDuplicateStart_WhenFirstStartPending()
    {
        var runtime = new FakeRuntime { WaitForRelease = true };
        using var controller = Create(runtime);
        var first = controller.StartAsync(Settings, Preset);
        await runtime.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await controller.StartAsync(Settings, Preset);
        runtime.Release.TrySetResult();
        await first;

        Assert.Equal(1, runtime.StartCount);
        Assert.Equal(LaunchPhase.Running, controller.Status.Phase);
    }

    [Fact]
    public async Task StopAsync_CancelsStartup_WhenStartIsPending()
    {
        var runtime = new FakeRuntime { WaitForRelease = true };
        using var controller = Create(runtime);
        var start = controller.StartAsync(Settings, Preset);
        await runtime.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await controller.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await start;

        Assert.False(runtime.HasProcesses);
        Assert.Equal("Cancelled", controller.Status.Message.Code);
        Assert.True(controller.CanStart);
        Assert.False(controller.Status.InterfaceReady);
    }

    [Fact]
    public async Task StartAsync_PreservesBothErrors_WhenStartupAndCleanupFail()
    {
        var runtime = new FakeRuntime { StartFailure = new IOException("start"), StopFailure = new IOException("stop") };
        using var controller = Create(runtime);

        await controller.StartAsync(Settings, Preset);

        var errors = Assert.IsType<AggregateException>(controller.Status.Failure);
        Assert.Contains(runtime.StartFailure, errors.InnerExceptions);
        Assert.Contains(runtime.StopFailure, errors.InnerExceptions);
        Assert.True(controller.CanStop);
        Assert.False(controller.CanStart);
    }

    [Fact]
    public async Task StopAsync_AllowsRetry_WhenCleanupFails()
    {
        var runtime = new FakeRuntime { StopFailure = new IOException("stop") };
        using var controller = Create(runtime);
        await controller.StartAsync(Settings, Preset);
        await controller.StopAsync();
        Assert.Equal("StopFailed", controller.Status.Message.Code);

        runtime.StopFailure = null;
        await controller.StopAsync();

        Assert.True(controller.CanStart);
        Assert.False(controller.CanStop);
        Assert.Equal(ServiceState.Stopped, controller.Status.Llama.State);
    }

    [Fact]
    public async Task StartAsync_ReportsStoppedServices_WhenStartupCleanupSucceeds()
    {
        var runtime = new FakeRuntime { StartFailure = new IOException("start") };
        using var controller = Create(runtime);

        await controller.StartAsync(Settings, Preset);

        Assert.Equal(ServiceState.Stopped, controller.Status.Llama.State);
        Assert.Equal(ServiceState.Stopped, controller.Status.Harness.State);
        Assert.True(controller.CanStart);
    }

    [Fact]
    public async Task StopAsync_ReportsSuccessfulCleanup_WhenAnotherServiceFails()
    {
        var runtime = new FakeRuntime { StopFailure = new IOException("stop"), PartiallyStopped = true };
        using var controller = Create(runtime);
        await controller.StartAsync(Settings, Preset);

        await controller.StopAsync();

        Assert.Equal(ServiceState.Stopped, controller.Status.Harness.State);
        Assert.Equal(ServiceState.Ready, controller.Status.Llama.State);
        Assert.True(controller.CanStop);
    }

    [Fact]
    public async Task StartAsync_UsesSnapshot_WhenSettingsEditedDuringStartup()
    {
        var runtime = new FakeRuntime { WaitForRelease = true };
        using var controller = Create(runtime);
        var settings = Settings;
        var start = controller.StartAsync(settings, Preset);
        await runtime.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        settings.DshPort = 7777;
        runtime.Release.TrySetResult();
        await start;

        Assert.Equal(3080, Assert.IsType<AppSettings>(runtime.Captured).DshPort);
    }

    [Fact]
    public async Task RefreshAsync_DisablesBrowser_WhenHarnessStopsResponding()
    {
        var runtime = new FakeRuntime();
        using var controller = Create(runtime);
        await controller.StartAsync(Settings, Preset);
        runtime.Snapshot = new(new(ServiceState.Ready), new(ServiceState.Unavailable), false);

        await controller.RefreshAsync();

        Assert.False(controller.Status.InterfaceReady);
        Assert.Null(controller.BrowserUrl);
        Assert.Equal(ServiceState.Unavailable, controller.Status.Harness.State);
    }

    [Fact]
    public async Task StopAsync_DoesNotClaimStoppedServices_WhenReusingExistingServers()
    {
        var runtime = new FakeRuntime { OwnsProcesses = false };
        using var controller = Create(runtime);
        await controller.StartAsync(Settings, Preset);

        await controller.StopAsync();

        Assert.Equal("NothingToStop", controller.Status.Message.Code);
        Assert.Empty(runtime.StoppedServices);
    }

    [Fact]
    public async Task RefreshAsync_PropagatesCancellation_WhenProbePending()
    {
        var runtime = new FakeRuntime { WaitForProbe = true };
        using var controller = Create(runtime);
        await controller.StartAsync(Settings, Preset);
        using var cancellation = new CancellationTokenSource();
        var refresh = controller.RefreshAsync(cancellation.Token);
        await runtime.ProbeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Equal(ServiceState.Ready, controller.Status.Harness.State);
    }

    private sealed class FakeRuntime : ILauncherRuntime
    {
        public event Action<RuntimeSnapshot>? Changed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ProbeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WaitForProbe
        {
            get; init;
        }
        public bool WaitForRelease
        {
            get; init;
        }
        public bool OwnsProcesses { get; init; } = true;
        public Exception? StartFailure
        {
            get; init;
        }
        public Exception? StopFailure
        {
            get; set;
        }
        public bool PartiallyStopped
        {
            get; init;
        }
        public int StartCount
        {
            get; private set;
        }
        public AppSettings? Captured
        {
            get; private set;
        }
        public string BrowserUrl => "http://127.0.0.1:3080/";
        public string RootUrl => BrowserUrl;
        public bool HasProcesses
        {
            get; private set;
        }
        public IReadOnlyList<string> StoppedServices { get; private set; } = Array.Empty<string>();
        public RuntimeSnapshot Snapshot { get; set; } = new(new(ServiceState.Ready), new(ServiceState.Ready), true);
        public async Task StartAsync(AppSettings settings, Preset preset, CancellationToken token)
        {
            Captured = settings;
            StartCount++;
            HasProcesses = OwnsProcesses;
            Entered.TrySetResult();
            if (WaitForRelease)
                await Release.Task.WaitAsync(token);
            if (StartFailure is not null)
                throw StartFailure;
            Changed?.Invoke(Snapshot);
        }
        public async Task<RuntimeSnapshot> GetSnapshotAsync(CancellationToken token)
        {
            ProbeEntered.TrySetResult();
            if (WaitForProbe)
                await Task.Delay(Timeout.Infinite, token);
            return Snapshot;
        }
        public Task StopAsync()
        {
            if (PartiallyStopped)
                StoppedServices = new[] { "DeepSeek Harness" };
            if (StopFailure is not null)
                return Task.FromException(StopFailure);
            StoppedServices = HasProcesses ? new[] { "llama.cpp", "DeepSeek Harness" } : Array.Empty<string>();
            HasProcesses = false;
            return Task.CompletedTask;
        }
        public void Dispose()
        {
        }
    }
}
