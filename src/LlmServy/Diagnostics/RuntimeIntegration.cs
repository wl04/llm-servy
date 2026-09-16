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

    public async Task RunAsync(AppPaths paths)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var resultFile = Path.Combine(paths.DataDirectory, "launcher-runtime-test.txt");
        var localizer = new Localizer();
        var log = new LauncherLog(paths, localizer);
        LauncherController? controller = null;
        try
        {
            if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == TestPort))
                throw new InvalidOperationException("Diagnostic port is already in use.");
            var settings = new SettingsStore(new AppPaths()).Load();
            settings.MigratePaths();
            settings.DshPort = TestPort;
            settings.OpenBrowser = false;
            var presets = Preset.Read(settings.PresetPath);
            var preset = presets.FirstOrDefault(p => p.ModelId == settings.SelectedModelId) ?? presets[0];
            settings.SelectedModelId = preset.ModelId;
            using var llamaHandler = new ReadyLlama(preset.ModelId);
            var runtime = new LauncherRuntime(paths, log, new WindowsProcessAdapter(TimeProvider.System),
                new HttpClient(llamaHandler),
                new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(4) },
                TimeProvider.System);
            controller = new LauncherController(runtime, log);

            await controller.StartAsync(settings, preset);
            if (controller.Status.Failure is { } failure)
                throw failure;
            if (!controller.Status.IsActive || !controller.Status.Llama.AlreadyRunning || controller.Status.Dsh.AlreadyRunning)
                throw new InvalidOperationException("Unexpected service ownership or startup state.");
            if (controller.BrowserUrl is not { } url || !url.Contains("?token=", StringComparison.Ordinal))
                throw new InvalidOperationException("Authenticated harness URL was not captured.");
            await controller.RefreshAsync();
            if (!controller.Status.BrowserReady)
                throw new InvalidOperationException("Readiness refresh lost the harness.");

            await controller.StopAsync();
            if (controller.Status.Failure is { } stopFailure)
                throw stopFailure;
            if (!runtime.StoppedServices.SequenceEqual(new[] { "DeepSeek Harness" }) || !controller.CanStart)
                throw new InvalidOperationException("Unexpected cleanup result.");
            await controller.StartAsync(settings, preset);
            if (controller.Status.Failure is { } restartFailure)
                throw restartFailure;
            if (controller.Status.Dsh.AlreadyRunning)
                throw new InvalidOperationException("Diagnostic restart reused an unexpected process.");
            controller.Dispose();
            controller = null;
            using var probeClient = new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
            var probe = await new DshEndpoint(TestPort).ProbeAsync(probeClient, false, CancellationToken.None);
            if (probe.Ready)
                throw new InvalidOperationException("Harness still responds after runtime disposal.");
            File.WriteAllText(resultFile, "PASS production controller/runtime: simulated existing llama.cpp, real Windows -> WSL -> authenticated DeepSeek Harness, refresh, stop, restart, direct disposal, ownership preserved. No GPU model loaded.\n");
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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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
