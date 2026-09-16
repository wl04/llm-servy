using LlmServy.Configuration;
using LlmServy.Services;

namespace LlmServy.Runtime;

public static class Composition
{
    public static LauncherController CreateController(AppPaths paths, LauncherLog log)
    {
        var llamaHttp = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
        var dshHttp = new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(4) };
        var time = TimeProvider.System;
        return new(new LauncherRuntime(paths, log, new(time), llamaHttp, dshHttp, time), log);
    }
}
