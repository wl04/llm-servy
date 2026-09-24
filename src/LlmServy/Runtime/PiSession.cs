using System.Text.Json;
using System.Text.RegularExpressions;
using LlmServy.Services;

namespace LlmServy.Runtime;

public sealed record TerminalSession(string Distro, string Socket);

/// <summary>Reads lifecycle events from the owned supervisor, not agent activity or conversation output.</summary>
public sealed class PiSession(string distro, LauncherLog log)
{
    private readonly object gate = new();
    private ServiceState state = ServiceState.Starting;
    private string? socket;
    private AppException? failure;
    public ServiceState State
    {
        get
        {
            lock (gate)
                return state;
        }
    }
    public TerminalSession? Terminal
    {
        get
        {
            lock (gate)
                return state == ServiceState.Ready && socket is not null ? new(distro, socket) : null;
        }
    }

    public void ThrowIfFailed()
    {
        lock (gate)
            if (failure is not null)
                throw failure;
    }

    public void Observe(string channel, string line)
    {
        // Shell diagnostics are useful; agent terminal output never travels over this channel.
        if (channel != "stdout" || !line.StartsWith('{'))
        {
            log.Write("Pi/" + channel, line);
            return;
        }
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            lock (gate)
            {
                switch (root.GetProperty("state").GetString())
                {
                    case "running":
                        var candidate = root.GetProperty("socket").GetString() ?? "";
                        if (!Regex.IsMatch(candidate, @"^/tmp/llm-servy-pi-[a-zA-Z0-9_-]+/tmux\.sock$"))
                            throw new JsonException("Invalid terminal endpoint.");
                        socket = candidate;
                        state = ServiceState.Ready;
                        log.WriteMessage("Pi", new("PiSessionStarted"));
                        break;
                    case "exited":
                        int code = root.GetProperty("exitCode").GetInt32();
                        state = code == 0 ? ServiceState.Stopped : ServiceState.Failed;
                        if (code != 0)
                            failure = new(new("PiExited", code));
                        log.WriteMessage("Pi", new("PiExited", code));
                        break;
                    case "error":
                        var key = root.GetProperty("code").GetString();
                        failure = new(new(key is "PiMissingExecutable" or "PiMissingTmux" or "PiSessionLost" ? key : "PiSupervisorFailed"));
                        state = ServiceState.Failed;
                        log.WriteError("Pi", failure);
                        break;
                    case "stopped":
                        if (state != ServiceState.Failed)
                            state = ServiceState.Stopped;
                        socket = null;
                        break;
                    default:
                        throw new JsonException("Unknown lifecycle event.");
                }
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            lock (gate)
            {
                state = ServiceState.Failed;
                failure = new(new("PiSupervisorFailed"), error);
            }
            log.WriteError("Pi", error);
        }
    }
}
