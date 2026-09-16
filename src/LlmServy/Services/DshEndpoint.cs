using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LlmServy.Services;

public sealed record DshProbe(bool Ready = false, bool RequiresLogin = false, AppMessage? Detail = null);

// The launch URL is a credential. Keep it in memory, never send it in health probes.
public sealed class DshEndpoint
{
    readonly int port;
    readonly object gate = new object();
    string? launchUrl;
    static readonly Regex Ansi = new Regex(@"\x1B\[[0-?]*[ -/]*[@-~]");
    static readonly Regex Announcement = new Regex(@"^dsh web:\s+(http://\S+)", RegexOptions.IgnoreCase);
    public DshEndpoint(int port)
    {
        this.port = port;
    }
    public string RootUrl
    {
        get
        {
            lock (gate)
            {
                return launchUrl == null ? "http://127.0.0.1:" + port + "/" : new Uri(launchUrl).GetLeftPart(UriPartial.Authority) + "/";
            }
        }
    }
    public string BrowserUrl
    {
        get
        {
            lock (gate)
            {
                return launchUrl ?? RootUrl;
            }
        }
    }
    public bool HasToken
    {
        get
        {
            lock (gate)
            {
                return launchUrl != null && new Uri(launchUrl).Query.Length > 0;
            }
        }
    }
    public static string? GetAnnouncedUrl(string line, int port)
    {
        var match = Announcement.Match(Ansi.Replace(line, "").Trim());
        Uri? uri;
        if (!match.Success || !Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out uri))
            return null;
        if (uri.Scheme != "http" || uri.Port != port || uri.UserInfo != "" || uri.AbsolutePath != "/" || uri.Fragment != "")
            return null;
        if (uri.Host != "127.0.0.1" && uri.Host != "localhost" && uri.Host != "[::1]" && uri.Host != "::1")
            return null;
        if (uri.Query != "" && !Regex.IsMatch(uri.Query, @"^\?token=[A-Za-z0-9_-]+$"))
            return null;
        return uri.AbsoluteUri;
    }
    public void Observe(string line)
    {
        var url = GetAnnouncedUrl(line, port);
        if (url is not null)
            lock (gate)
                launchUrl = url;
    }
    public static string Redact(string text)
    {
        return Regex.Replace(text, @"([?&]token=)[^\s&\""'<>\)\]]+", "$1[hidden]", RegexOptions.IgnoreCase);
    }
    public static DshProbe Classify(int status, string body, bool owned, bool hasToken)
    {
        bool marker = body.IndexOf("dsh", StringComparison.OrdinalIgnoreCase) >= 0 || body.IndexOf("deepseek", StringComparison.OrdinalIgnoreCase) >= 0;
        bool auth = status == 401 && body.IndexOf("dsh web authentication required", StringComparison.OrdinalIgnoreCase) >= 0;
        bool ready = (status >= 200 && status < 300 && marker) || (auth && (!owned || hasToken));
        return new DshProbe(ready, auth,
            auth ? new AppMessage(ready ? "DshLoginReady" : "DshLoginPending") : new AppMessage("DshHttp", status));
    }
    /// <summary>Checks readiness without exchanging the browser token or changing the endpoint.</summary>
    public async Task<DshProbe> ProbeAsync(HttpClient client, bool owned, CancellationToken token)
    {
        try
        {
            using (var response = await client.GetAsync(RootUrl, token))
            {
                return Classify((int)response.StatusCode, await response.Content.ReadAsStringAsync(token), owned, HasToken);
            }
        }
        catch (OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            return new DshProbe(Detail: new("ProbeTimeout"));
        }
        catch (HttpRequestException) { return new DshProbe(Detail: new("ProbeUnavailable")); }
    }
}
