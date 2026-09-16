using System.Net.NetworkInformation;
using LlmServy.Configuration;
using LlmServy.Models;
using LlmServy.Processes;
using LlmServy.Services;

namespace LlmServy.Runtime;

public sealed class LauncherRuntime(AppPaths paths, LauncherLog log, WindowsProcessAdapter windows,
    HttpClient llamaHttp, HttpClient dshHttp, TimeProvider time) : ILauncherRuntime
{
    private bool disposed;
    private readonly ServiceLifetime lifetime = new();
    private readonly LlamaClient llamaClient = new(llamaHttp);
    private AppSettings? session;
    private DshEndpoint? endpoint;
    private OwnedProcess? llama, dsh;
    private RuntimeSnapshot snapshot = new(new(), new(), false);
    public event Action<RuntimeSnapshot>? Changed;
    public bool HasProcesses => lifetime.HasProcesses;
    public IReadOnlyList<string> StoppedServices => lifetime.StoppedServices;
    public string? BrowserUrl => endpoint?.BrowserUrl;
    public string RootUrl => endpoint?.RootUrl ?? "";
    private AppSettings Session => session ?? throw new InvalidOperationException("No active session.");
    private string LlamaUrl => $"http://127.0.0.1:{Session.LlamaPort}";
    private void Update(RuntimeSnapshot value)
    {
        snapshot = value;
        Changed?.Invoke(value);
    }
    private static bool IsPortUsed(int port) => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == port);

    public async Task StartAsync(AppSettings settings, Preset preset, CancellationToken token)
    {
        SettingsValidator.Validate(settings);
        session = settings with
        {
        };
        endpoint = new(session.DshPort);
        llama = null;
        dsh = null;
        Update(new(new(ServiceState.Checking), new(ServiceState.Checking), false));
        if (!File.Exists(paths.BridgeFile))
            throw new AppException(new("MissingBridge"));
        await windows.ExecuteWslAsync(["-d", session.Distro, "--exec", "/bin/true"], token);
        var script = await windows.ExecuteWslAsync(["-d", session.Distro, "--exec", "wslpath", "-a", "-u", paths.BridgeFile], token);
        if (string.IsNullOrWhiteSpace(script))
            throw new AppException(new("MissingBridge"));
        bool ready = await IsLlamaReadyAsync(token);
        if (!ready && IsPortUsed(session.LlamaPort))
        {
            log.WriteMessage("llama.cpp", new("WaitExisting"));
            var started = time.GetTimestamp();
            while (!ready && time.GetElapsedTime(started) < TimeSpan.FromSeconds(15))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(750), time, token);
                ready = await IsLlamaReadyAsync(token);
            }
        }
        token.ThrowIfCancellationRequested();
        if (ready)
            log.WriteMessage("llama.cpp", new("ExistingLlama"));
        else
        {
            if (IsPortUsed(session.LlamaPort))
                throw new AppException(new("PortOccupied", "llama.cpp"));
            var executable = WindowsProcessAdapter.GetExecutable(SettingsValidator.GetLlamaExecutable(session), "llama.exe");
            var arguments = new List<string>();
            if (!Path.GetFileNameWithoutExtension(executable).Equals("llama-server", StringComparison.OrdinalIgnoreCase))
                arguments.Add("serve");
            arguments.AddRange(["--models-preset", preset.IniPath, "--host", session.BindAddress, "--port", session.LlamaPort.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            // INI-relative model paths must remain independent of the application's location.
            llama = OwnedProcess.Start(executable, arguments.ToArray(), preset.WorkingDirectory, (channel, line) => log.Write("llama.cpp/" + channel, line));
            lifetime.Attach("llama.cpp", new OwnedServiceProcess(llama, false));
            Update(snapshot with
            {
                Llama = new(ServiceState.Starting)
            });
            await WaitForAsync(() => IsLlamaReadyAsync(token), () => !llama.Running, "llama.cpp", token);
        }
        Update(snapshot with
        {
            Llama = new(ServiceState.Loading, ready)
        });
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(session.TimeoutSeconds));
            try
            {
                await llamaClient.LoadModelAsync(LlamaUrl, preset.ModelId, timeout.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new AppException(new("ServiceTimeout", "llama.cpp")); }
        }
        Update(snapshot with
        {
            Llama = new(ServiceState.Ready, ready)
        });
        token.ThrowIfCancellationRequested();
        var probe = await endpoint.ProbeAsync(dshHttp, false, token);
        if (probe.Ready)
        {
            log.WriteMessage("DeepSeek Harness", new("ExistingDsh"));
            if (probe.RequiresLogin)
                log.WriteMessage("DeepSeek Harness", new("ExternalLogin"));
            Update(snapshot with
            {
                Dsh = new(ServiceState.Ready, true),
                BrowserReady = true
            });
        }
        else
        {
            if (IsPortUsed(session.DshPort))
                throw new AppException(new("PortOccupied", "DeepSeek Harness"));
            Update(snapshot with
            {
                Dsh = new(ServiceState.Starting)
            });
            var currentEndpoint = endpoint;
            dsh = OwnedProcess.Start(WindowsProcessAdapter.GetExecutable("", "wsl.exe"),
                ["-d", session.Distro, "--cd", session.WslDirectory, "--exec", "bash", "-li", script, "--port", session.DshPort.ToString(System.Globalization.CultureInfo.InvariantCulture), "--package", session.DshPackage],
                AppContext.BaseDirectory, (channel, line) => { currentEndpoint.Observe(line); log.Write("DeepSeek Harness/" + channel, line); });
            lifetime.Attach("DeepSeek Harness", new OwnedServiceProcess(dsh, true));
            await WaitForAsync(async () => (await endpoint.ProbeAsync(dshHttp, true, token)).Ready, () => !dsh.Running, "DeepSeek Harness", token);
            Update(snapshot with
            {
                Dsh = new(ServiceState.Ready),
                BrowserReady = true
            });
        }
        token.ThrowIfCancellationRequested();
    }

    private async Task<bool> IsLlamaReadyAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            return await llamaClient.IsReadyAsync(LlamaUrl, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
    }

    public async Task<RuntimeSnapshot> GetSnapshotAsync(CancellationToken token)
    {
        if (session is null || endpoint is null)
            return snapshot;
        var llamaReady = (llama is null || llama.Running) && await IsLlamaReadyAsync(token);
        var probe = await endpoint.ProbeAsync(dshHttp, dsh is not null, token);
        bool dshReady = (dsh is null || dsh.Running) && probe.Ready;
        return new(new(llamaReady ? ServiceState.Ready : ServiceState.Unavailable, llama is null),
            new(dshReady ? ServiceState.Ready : ServiceState.Unavailable, dsh is null), dshReady);
    }

    public async Task StopAsync()
    {
        await lifetime.StopAsync();
        llama = null;
        dsh = null;
        endpoint = null;
        session = null;
    }

    private async Task WaitForAsync(Func<Task<bool>> isReady, Func<bool> hasExited, string service, CancellationToken token)
    {
        var started = time.GetTimestamp();
        var nextReport = TimeSpan.FromSeconds(15);
        while (time.GetElapsedTime(started) < TimeSpan.FromSeconds(Session.TimeoutSeconds))
        {
            token.ThrowIfCancellationRequested();
            if (hasExited())
                throw new AppException(new("ServiceExited", service));
            if (await isReady())
                return;
            if (time.GetElapsedTime(started) >= nextReport)
            {
                log.WriteMessage("launcher", new("WaitingService", service));
                nextReport += TimeSpan.FromSeconds(15);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(750), time, token);
        }
        throw new AppException(new("ServiceTimeout", service));
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        // Emergency disposal must finish Linux shutdown before releasing the Windows bridge.
        ResourceCleanup.Run(
            () => dsh?.StopBridgeAsync().GetAwaiter().GetResult(),
            () => dsh?.Dispose(),
            () => llama?.Dispose(),
            llamaHttp.Dispose,
            dshHttp.Dispose);
    }
}
