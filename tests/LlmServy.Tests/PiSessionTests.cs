using LlmServy.Configuration;
using LlmServy.Localization;
using LlmServy.Runtime;
using LlmServy.Services;

namespace LlmServy.Tests;

public sealed class PiSessionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private PiSession CreateSession() => new("Ubuntu", new(new AppPaths(directory), new Localizer()));

    [Fact]
    public void Observe_ExposesTerminal_WhenSessionStarts()
    {
        var session = CreateSession();

        session.Observe("stdout", """{"state":"running","socket":"/tmp/llm-servy-pi-abc/tmux.sock"}""");

        Assert.Equal(ServiceState.Ready, session.State);
        Assert.Equal(new TerminalSession("Ubuntu", "/tmp/llm-servy-pi-abc/tmux.sock"), session.Terminal);
    }

    [Theory]
    [InlineData(0, ServiceState.Stopped)]
    [InlineData(17, ServiceState.Failed)]
    public void Observe_RemovesTerminal_WhenPiExits(int code, ServiceState expected)
    {
        var session = CreateSession();
        session.Observe("stdout", """{"state":"running","socket":"/tmp/llm-servy-pi-abc/tmux.sock"}""");

        session.Observe("stdout", "{\"state\":\"exited\",\"exitCode\":" + code + "}");
        session.Observe("stdout", """{"state":"stopped"}""");

        Assert.Equal(expected, session.State);
        Assert.Null(session.Terminal);
    }

    [Theory]
    [InlineData("{\"state\":\"unexpected\"}")]
    [InlineData("{\"state\":\"running\",\"socket\":\"/tmp/other.sock\"}")]
    [InlineData("{invalid")]
    public void Observe_ReportsFailure_WhenEventInvalid(string line)
    {
        var session = CreateSession();

        session.Observe("stdout", line);

        Assert.Equal(ServiceState.Failed, session.State);
        Assert.Null(session.Terminal);
        Assert.Throws<AppException>(session.ThrowIfFailed);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}
