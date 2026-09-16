using System.Net;
using LlmServy.Runtime;
using LlmServy.Services;

namespace LlmServy.Tests;

public sealed class LlamaClientTests
{
    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"status\":123}")]
    public async Task IsReadyAsync_ReportsInvalidResponse_WhenHealthPayloadMalformed(string body)
    {
        using var http = new HttpClient(new StubHandler(body));
        var client = new LlamaClient(http);

        var error = await Assert.ThrowsAsync<AppException>(() => client.IsReadyAsync("http://localhost", CancellationToken.None));

        Assert.Equal("InvalidResponse", error.Detail.Code);
    }

    [Fact]
    public async Task LoadModelAsync_DoesNotRequestLoading_WhenModelMissing()
    {
        var handler = new StubHandler("{\"data\":[]}");
        using var http = new HttpClient(handler);
        var client = new LlamaClient(http);

        var error = await Assert.ThrowsAsync<AppException>(() => client.LoadModelAsync("http://localhost", "missing", CancellationToken.None));

        Assert.Equal("MissingModel", error.Detail.Code);
        Assert.Equal(new[] { "/v1/models" }, handler.Paths);
    }

    [Fact]
    public async Task IsReadyAsync_PropagatesCancellation_WhenCallerCancels()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var http = new HttpClient(new StubHandler("{\"status\":\"ok\"}"));
        var client = new LlamaClient(http);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.IsReadyAsync("http://localhost", cancellation.Token));
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
