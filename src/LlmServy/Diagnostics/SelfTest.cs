using LlmServy.Configuration;
using LlmServy.Processes;
using LlmServy.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LlmServy.Diagnostics;

internal static class SelfTest
{
    public static int Run(AppPaths paths)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var results = new List<string>();
        string root = paths.DataDirectory;
        try
        {
            if (OwnedProcess.Quote("a b") != "\"a b\"")
                throw new Exception("Argument quoting failed");
            if (OwnedProcess.Quote("x y\\") != "\"x y\\\\\"")
                throw new Exception("Trailing backslash quoting failed");
            new AppSettings().Validate(false);
            results.Add("PASS settings and argument escaping");
            TestEndpoint();
            results.Add("PASS DSH auth/legacy readiness, local URL validation and token redaction; probes do not send tokens");
            string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path.");
            var captured = new List<string>();
            using (var p = OwnedProcess.Start(exe, new[] { "--test-echo", "space argument", "quote\"value", "trailing\\" }, root, (ch, s) => { lock (captured) captured.Add(ch + ":" + s); }))
            {
                var end = DateTime.UtcNow.AddSeconds(10);
                while (p.Running && DateTime.UtcNow < end)
                    Thread.Sleep(50);
                if (p.Running)
                    throw new Exception("Child timed out");
                p.DrainAsync().GetAwaiter().GetResult();
                if (p.ExitCode != 23 || !captured.Contains("stdout:space argument") || !captured.Contains("stdout:quote\"value") || !captured.Contains("stdout:trailing\\") || !captured.Contains("stderr:diagnostic"))
                    throw new Exception("Child stdout/stderr/arguments/exit-code failed: " + String.Join(" | ", captured));
            }
            results.Add("PASS suspended child launch, stdout/stderr, arguments and exit code");
            int descendant = 0;
            using (var p = OwnedProcess.Start(exe, new[] { "--test-tree" }, root, (ch, s) => { int id; if (Int32.TryParse(s, out id)) descendant = id; }))
            {
                var end = DateTime.UtcNow.AddSeconds(10);
                while (descendant == 0 && DateTime.UtcNow < end)
                    Thread.Sleep(50);
                if (descendant == 0)
                    throw new Exception("No descendant PID");
            }
            try
            {
                using var descendantProcess = Process.GetProcessById(descendant);
                if (!descendantProcess.WaitForExit(5000))
                    throw new Exception("Job did not terminate descendant");
            }
            catch (ArgumentException) { /* Already reaped: the expected termination condition. */ }
            results.Add("PASS Job closes the owned process tree");
            captured.Clear();
            using (var p = OwnedProcess.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe"), new[] { "-d", "Ubuntu", "--exec", "/bin/echo", "WSL_OK" }, root, (ch, s) => { lock (captured) captured.Add(s); }))
            {
                var end = DateTime.UtcNow.AddSeconds(30);
                while (p.Running && DateTime.UtcNow < end)
                    Thread.Sleep(50);
                if (p.Running)
                    throw new Exception("WSL probe timed out");
                p.DrainAsync().GetAwaiter().GetResult();
                if (p.ExitCode != 0 || !captured.Contains("WSL_OK"))
                    throw new Exception("WSL pipe probe failed: " + String.Join(" | ", captured));
            }
            results.Add("PASS Windows-to-Ubuntu launch through owned pipes");
            string llamaAlias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "llama.exe");
            if (File.Exists(llamaAlias))
            {
                captured.Clear();
                using (var p = OwnedProcess.Start(llamaAlias, new[] { "--version" }, root, (ch, s) => { lock (captured) captured.Add(s); }))
                {
                    var end = DateTime.UtcNow.AddSeconds(15);
                    while (p.Running && DateTime.UtcNow < end)
                        Thread.Sleep(50);
                    if (p.Running)
                        throw new Exception("llama version probe timed out");
                    p.DrainAsync().GetAwaiter().GetResult();
                    if (p.ExitCode != 0)
                        throw new Exception("llama alias failed: " + String.Join(" | ", captured));
                }
                results.Add("PASS installed llama WindowsApps alias: " + String.Join(" | ", captured));
            }
            File.WriteAllLines(Path.Combine(root, "launcher-self-test.txt"), results);
            return 0;
        }
        catch (Exception e) { results.Add("FAIL " + e); File.WriteAllLines(Path.Combine(root, "launcher-self-test.txt"), results); return 1; }
    }
    sealed class FakeWeb : HttpMessageHandler
    {
        public Uri? Requested;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("dsh web authentication required; reopen the URL printed by dsh web.\n")
            });
        }
    }
    static void Check(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message);
    }
    static void TestEndpoint()
    {
        var endpoint = new DshEndpoint(3080);
        string auth = "dsh web authentication required; reopen the URL printed by dsh web.";
        Check(!DshEndpoint.Classify(401, auth, true, false).Ready, "Owned auth server must wait for launch URL");
        string line = "dsh web: http://127.0.0.1:3080/?token=test-secret_123";
        endpoint.Observe(line);
        Check(DshEndpoint.GetAnnouncedUrl(line, 3080) != null, "Auth launch URL not parsed");
        Check(endpoint.BrowserUrl.EndsWith("?token=test-secret_123"), "Browser lost token");
        Check(!endpoint.RootUrl.Contains("token"), "Token leaked into probe URL");
        Check(!DshEndpoint.Redact(line).Contains("test-secret_123"), "Token leaked into log");
        Check(DshEndpoint.GetAnnouncedUrl("dsh web: http://example.com:3080/?token=bad", 3080) == null, "Remote launch URL accepted");
        Check(DshEndpoint.GetAnnouncedUrl("dsh web: http://127.0.0.1:3081/?token=bad", 3080) == null, "Wrong port accepted");
        Check(DshEndpoint.GetAnnouncedUrl("dsh web: http://user@127.0.0.1:3080/?token=bad", 3080) == null, "Userinfo accepted");
        Check(DshEndpoint.GetAnnouncedUrl("dsh web: http://127.0.0.1:3080/admin?token=bad", 3080) == null, "Wrong path accepted");
        Check(DshEndpoint.Classify(200, "<title>DeepSeek Harness</title>", true, false).Ready, "Legacy server rejected");
        Check(!DshEndpoint.Classify(503, auth, true, true).Ready, "Failed server accepted");
        Check(!DshEndpoint.Classify(401, "unauthorized", true, true).Ready, "Unrelated server accepted");
        using (var handler = new FakeWeb())
        using (var client = new HttpClient(handler))
        {
            var result = endpoint.ProbeAsync(client, true, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Ready && result.RequiresLogin, "Authenticated DSH readiness failed");
            Check(handler.Requested?.AbsoluteUri == "http://127.0.0.1:3080/", "Health probe attempted token exchange");
        }
        var fresh = new DshEndpoint(3080);
        Check(!fresh.HasToken, "Token carried across restarts");
        fresh.Observe("\u001b[32mdsh web: http://127.0.0.1:3080\u001b[0m");
        Check(fresh.BrowserUrl == "http://127.0.0.1:3080/", "Legacy ANSI URL rejected");
    }

    public static int DshIntegration(AppPaths paths)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        string root = paths.DataDirectory;
        var endpoint = new DshEndpoint(13083);
        OwnedProcess? child = null;
        try
        {
            var settings = new SettingsStore(new AppPaths()).Load();
            string script = "";
            string wsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
            using (var convert = OwnedProcess.Start(wsl, new[] { "-d", settings.Distro, "--exec", "wslpath", "-a", "-u", paths.BridgeFile }, root, (ch, s) => { if (ch == "stdout") script = s; }))
            {
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (convert.Running && DateTime.UtcNow < deadline)
                    Thread.Sleep(50);
                Check(!convert.Running, "wslpath timed out");
                convert.DrainAsync().GetAwaiter().GetResult();
                Check(convert.ExitCode == 0 && script != "", "wslpath failed");
            }
            child = OwnedProcess.Start(wsl, new[] { "-d", settings.Distro, "--cd", settings.WslDirectory, "--exec", "bash", "-li", script, "--port", "13083", "--package", settings.DshPackage }, root, (ch, s) => endpoint.Observe(s));
            using (var client = new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(3) })
            {
                var deadline = DateTime.UtcNow.AddSeconds(60);
                bool ready = false;
                while (DateTime.UtcNow < deadline && child.Running)
                {
                    var result = endpoint.ProbeAsync(client, true, CancellationToken.None).GetAwaiter().GetResult();
                    if (result.Ready && result.RequiresLogin && endpoint.HasToken)
                    {
                        ready = true;
                        break;
                    }
                    Thread.Sleep(250);
                }
                Check(ready, "DSH auth integration failed to become ready");
            }
            child.StopBridgeAsync().GetAwaiter().GetResult();
            child = null;
            File.WriteAllText(Path.Combine(root, "launcher-dsh-test.txt"), "PASS real Windows -> WSL -> installed DSH: launch URL captured in memory, HTTP 401 recognized, no token sent in health probe, owned supervisor stopped.\n");
            return 0;
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(root, "launcher-dsh-test.txt"), "FAIL " + DshEndpoint.Redact(e.ToString()));
            return 1;
        }
        finally { if (child != null) child.StopBridgeAsync().GetAwaiter().GetResult(); }
    }
}
