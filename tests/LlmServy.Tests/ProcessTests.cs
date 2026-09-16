using LlmServy.Processes;

namespace LlmServy.Tests;

public sealed class ProcessTests
{
    [Fact]
    public async Task DrainAsync_ReportsOutputFailure_WhenCallbackFails()
    {
        var failure = new IOException("output failed");
        using var child = OwnedProcess.Start(Path.Combine(AppContext.BaseDirectory, "llm-servy.exe"),
            ["--test-echo"], AppContext.BaseDirectory, (_, _) => throw failure);

        var reported = await Assert.ThrowsAsync<AggregateException>(() => child.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        var outputFailure = Assert.IsType<LlmServy.Services.AppException>(Assert.Single(reported.InnerExceptions));
        Assert.Equal("ProcessOutputFailed", outputFailure.Detail.Code);
        Assert.Same(failure, outputFailure.InnerException);
    }

    [Fact]
    public async Task StopBridgeAsync_Completes_WhenChildAlreadyExited()
    {
        using var child = OwnedProcess.Start(Path.Combine(AppContext.BaseDirectory, "llm-servy.exe"),
            ["--test-echo"], AppContext.BaseDirectory, (_, _) => { });
        await child.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));

        await child.StopBridgeAsync();

        Assert.False(child.Running);
    }

    [Fact]
    public async Task StopBridgeAsync_ReportsTimeout_WhenChildDoesNotExit()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "llm-servy.exe");
        using var child = OwnedProcess.Start(executable, ["--test-sleep"], AppContext.BaseDirectory, (_, _) => { });

        await Assert.ThrowsAsync<TimeoutException>(() => child.StopBridgeAsync());

        Assert.True(child.Running);
    }
}
