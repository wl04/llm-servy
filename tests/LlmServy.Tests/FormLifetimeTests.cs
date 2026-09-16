using LlmServy.Configuration;
using LlmServy.Localization;
using LlmServy.Models;
using LlmServy.Runtime;
using LlmServy.Services;
using LlmServy.UI;

namespace LlmServy.Tests;

public sealed class FormLifetimeTests
{
    [Fact]
    public async Task Dispose_ReleasesControllerOnce_WhenWindowWasNeverShown()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                var runtime = new FakeRuntime();
                var paths = new AppPaths(directory);
                var controller = new LauncherController(runtime, new LauncherLog(paths, new Localizer()));
                using var form = new LauncherForm(paths, new AppSettings(), controller);

                form.Dispose();
                form.Dispose();

                Assert.Equal(1, runtime.DisposeCount);
                completed.SetResult();
            }
            catch (Exception error) { completed.SetException(error); }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class FakeRuntime : ILauncherRuntime
    {
        public event Action<RuntimeSnapshot>? Changed { add { } remove { } }
        public string? BrowserUrl => null;
        public string RootUrl => "";
        public bool HasProcesses => false;
        public IReadOnlyList<string> StoppedServices => Array.Empty<string>();
        public int DisposeCount
        {
            get; private set;
        }
        public Task StartAsync(AppSettings settings, Preset preset, CancellationToken token) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task<RuntimeSnapshot> GetSnapshotAsync(CancellationToken token) => Task.FromResult(new RuntimeSnapshot(new(), new(), false));
        public void Dispose() => DisposeCount++;
    }
}
