using System.Net;
using LlmServy.Services;

namespace LlmServy.Tests;

public sealed class DshEndpointTests
{
    [Fact]
    public async Task ProbeAsync_DoesNotExchangeBrowserToken_WhenAuthenticationRequired()
    {
        var endpoint = new DshEndpoint(3080);
        endpoint.Observe("dsh web: http://127.0.0.1:3080/?token=test-token_123");
        Assert.True(endpoint.HasToken);
        using var handler = new ProbeHandler();
        using var http = new HttpClient(handler);
        var result = await endpoint.ProbeAsync(http, true, CancellationToken.None);
        Assert.True(result.Ready);
        Assert.True(result.RequiresLogin);
        Assert.Equal("http://127.0.0.1:3080/", handler.Url);
        Assert.EndsWith("?token=test-token_123", endpoint.BrowserUrl);
        Assert.DoesNotContain("test-token_123", DshEndpoint.Redact(endpoint.BrowserUrl));
    }

    [Theory]
    [InlineData("http://example.com:3080/?token=bad")]
    [InlineData("http://127.0.0.1:1234/?token=bad")]
    [InlineData("http://user@127.0.0.1:3080/?token=bad")]
    [InlineData("http://127.0.0.1:3080/admin?token=bad")]
    public void GetAnnouncedUrl_RejectsUrl_WhenOutsideExpectedLocalRoot(string url) => Assert.Null(DshEndpoint.GetAnnouncedUrl("dsh web: " + url, 3080));

    [Theory]
    [InlineData(200, "<title>DeepSeek Harness</title>", false, true)]
    [InlineData(401, "dsh web authentication required", true, true)]
    [InlineData(401, "dsh web authentication required", false, false)]
    [InlineData(503, "dsh web authentication required", true, false)]
    [InlineData(401, "unauthorized", true, false)]
    public void Classify_ReportsReadiness_WhenResponseRequiresAuthenticationOrFails(int status, string body, bool token, bool ready) =>
        Assert.Equal(ready, DshEndpoint.Classify(status, body, true, token).Ready);

    private sealed class ProbeHandler : HttpMessageHandler
    {
        public string? Url;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("dsh web authentication required; reopen the URL printed by dsh web.") });
        }
    }
}
