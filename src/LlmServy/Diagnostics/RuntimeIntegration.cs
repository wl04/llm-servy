using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using LlmServy.Configuration;
using LlmServy.Localization;
using LlmServy.Models;
using LlmServy.Runtime;
using LlmServy.Services;

namespace LlmServy.Diagnostics;

/// <summary>Exercises the production controller and WSL adapter without loading a GPU model.</summary>
internal sealed class RuntimeIntegration
{
    private const int TestPort = 13083;
    public int ExitCode
    {
        get; private set;
    }

    public async Task RunAsync(AppPaths paths, bool testPi = false)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var resultFile = Path.Combine(paths.DataDirectory, testPi ? "launcher-pi-test.txt" : "launcher-runtime-test.txt");
        var localizer = new Localizer();
        var log = new LauncherLog(paths, localizer);
        LauncherController? controller = null;
        try
        {
            if (!testPi && IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == TestPort))
                throw new InvalidOperationException("Diagnostic port is already in use.");
            var settings = new SettingsStore(new AppPaths()).Load();
            settings.MigratePaths();
            settings.HarnessKind = testPi ? "pi" : "dsh";
            var windows = new WindowsProcessAdapter(TimeProvider.System);
            string? piFixture = null;
            if (testPi)
            {
                piFixture = Path.Combine(paths.DataDirectory, "pi-fixture.sh");
                File.WriteAllText(piFixture, "#!/bin/sh\nread answer\nexit 0\n");
                var linuxFixture = await windows.ExecuteWslAsync(["-d", settings.Distro, "--exec", "wslpath", "-a", "-u", piFixture], CancellationToken.None);
                await windows.ExecuteWslAsync(["-d", settings.Distro, "--exec", "chmod", "u+x", linuxFixture], CancellationToken.None);
                settings.PiExecutable = linuxFixture;
                settings.PiDirectory = "/tmp";
                settings.PiProvider = "diagnostic";
            }
            settings.DshPort = TestPort;
            settings.OpenBrowser = false;
            var presets = Preset.Read(settings.PresetPath);
            var preset = presets.FirstOrDefault(p => p.ModelId == settings.SelectedModelId) ?? presets[0];
            settings.SelectedModelId = preset.ModelId;
            using var llamaHandler = new ReadyLlama(preset.ModelId);
            var runtime = new LauncherRuntime(paths, log, windows,
                new HttpClient(llamaHandler),
                new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(4) },
                TimeProvider.System);
            controller = new LauncherController(runtime, log);

            await controller.StartAsync(settings, preset);
            if (controller.Status.Failure is { } failure)
                throw failure;
            if (!controller.Status.IsActive || !controller.Status.Llama.AlreadyRunning || controller.Status.Harness.AlreadyRunning)
                throw new InvalidOperationException("Unexpected service ownership or startup state.");
            if (testPi && controller.Terminal is null)
                throw new InvalidOperationException("Pi terminal endpoint was not captured.");
            if (!testPi && (controller.BrowserUrl is not { } url || !url.Contains("?token=", StringComparison.Ordinal)))
                throw new InvalidOperationException("Authenticated harness URL was not captured.");
            await controller.RefreshAsync();
            if (!controller.Status.InterfaceReady)
                throw new InvalidOperationException("Readiness refresh lost the harness.");

            if (testPi)
            {
                var firstTerminal = controller.Terminal ?? throw new InvalidOperationException("Missing Pi terminal.");
                await windows.ExecuteWslAsync(["-d", firstTerminal.Distro, "--exec", "tmux", "-S", firstTerminal.Socket,
                    "send-keys", "-t", "pi", "quit", "Enter"], CancellationToken.None);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                do
                {
                    await controller.RefreshAsync(deadline.Token);
                    if (controller.Status.Harness.State == ServiceState.Stopped)
                        break;
                    await Task.Delay(100, deadline.Token);
                } while (true);
                if (!controller.CanOpen)
                    throw new InvalidOperationException("Cannot reopen exited Pi.");
                await controller.PrepareInterfaceAsync();
                if (controller.Status.Failure is { } reopenFailure)
                    throw reopenFailure;
                if (controller.Terminal is not { } reopened || reopened.Socket == firstTerminal.Socket)
                    throw new InvalidOperationException("Pi was not replaced.");
                if (llamaHandler.LoadCount != 1)
                    throw new InvalidOperationException("Reopening Pi reloaded the model.");
            }

            await controller.StopAsync();
            if (controller.Status.Failure is { } stopFailure)
                throw stopFailure;
            if (!runtime.StoppedServices.SequenceEqual(new[] { testPi ? "Pi" : "DeepSeek Harness" }) || !controller.CanStart)
                throw new InvalidOperationException("Unexpected cleanup result.");
            await controller.StartAsync(settings, preset);
            if (controller.Status.Failure is { } restartFailure)
                throw restartFailure;
            if (controller.Status.Harness.AlreadyRunning)
                throw new InvalidOperationException("Diagnostic restart reused an unexpected process.");
            var terminal = controller.Terminal;
            controller.Dispose();
            controller = null;
            using var probeClient = new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
            if (terminal is not null)
            {
                await windows.ExecuteWslAsync(["-d", terminal.Distro, "--exec", "test", "!", "-e", terminal.Socket], CancellationToken.None);
                if (piFixture is not null)
                    File.Delete(piFixture);
            }
            var probe = testPi ? default : await new DshEndpoint(TestPort).ProbeAsync(probeClient, false, CancellationToken.None);
            if (probe is { Ready: true })
                throw new InvalidOperationException("Harness still responds after runtime disposal.");
            File.WriteAllText(resultFile, testPi ? "PASS production controller/runtime: simulated llama.cpp and Pi CLI fixture, real Windows -> WSL -> tmux, terminal endpoint, normal Pi exit, reopen without model reload, refresh, stop, restart, direct disposal. No GPU model loaded.\n" : "PASS production controller/runtime: simulated existing llama.cpp, real Windows -> WSL -> authenticated DeepSeek Harness, refresh, stop, restart, direct disposal, ownership preserved. No GPU model loaded.\n");
        }
        catch (Exception error)
        {
            ExitCode = 1;
            File.WriteAllText(resultFile, "FAIL " + DshEndpoint.Redact(error.ToString()));
        }
        finally
        {
            if (controller is not null)
            {
                await controller.StopAsync();
                if (controller.CanStop)
                {
                    ExitCode = 1;
                    File.AppendAllText(resultFile, "\nFAIL cleanup: " + localizer.Error(controller.Status.Failure ?? new InvalidOperationException("Cleanup did not complete.")));
                }
                controller.Dispose();
            }
        }
    }

    private sealed class ReadyLlama(string modelId) : HttpMessageHandler
    {
        public int LoadCount
        {
            get; private set;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/props")
                LoadCount++;
            var body = (request.RequestUri ?? throw new InvalidOperationException("Request URI is required.")).AbsolutePath switch
            {
                "/health" => "{\"status\":\"ok\"}",
                "/v1/models" => JsonSerializer.Serialize(new { data = new[] { new { id = modelId } } }),
                "/props" => "{\"default_generation_settings\":{}}",
                _ => throw new InvalidOperationException("Unexpected llama.cpp request.")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
