using LlmServy.Configuration;
using LlmServy.Localization;
using LlmServy.Runtime;
using LlmServy.Services;

namespace LlmServy.Tests;

public sealed class RuntimeDisposalTests
{
    [Fact]
    public void Dispose_ReleasesSecondClient_WhenFirstClientFails()
    {
        var first = new TrackingHandler(true);
        var second = new TrackingHandler(false);
        var paths = new AppPaths(Path.GetTempPath());
        var runtime = new LauncherRuntime(paths, new LauncherLog(paths, new Localizer()),
            new WindowsProcessAdapter(TimeProvider.System), new HttpClient(first), new HttpClient(second), TimeProvider.System);

        Assert.ThrowsAny<Exception>(() => runtime.Dispose());

        Assert.True(second.Disposed);
    }

    private sealed class TrackingHandler(bool fails) : HttpMessageHandler
    {
        public bool Disposed
        {
            get; private set;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            if (fails)
                throw new IOException("dispose failed");
            base.Dispose(disposing);
        }
    }
}
