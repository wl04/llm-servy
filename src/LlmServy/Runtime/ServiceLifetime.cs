using LlmServy.Services;

namespace LlmServy.Runtime;

public interface IServiceLifetime
{
    Task StopAsync();
}

/// <summary>Tracks only processes created by this session; stop failures retain handles for retry.</summary>
public sealed class ServiceLifetime
{
    private readonly Dictionary<string, IServiceLifetime> processes = new();
    public IReadOnlyList<string> StoppedServices { get; private set; } = Array.Empty<string>();
    public bool HasProcesses => processes.Count > 0;
    public void Attach(string name, IServiceLifetime process) => processes.Add(name, process);

    public async Task StopAsync()
    {
        var stopped = new List<string>();
        var errors = new List<Exception>();
        foreach (var entry in processes.Reverse().ToArray())
        {
            try
            {
                await entry.Value.StopAsync();
                processes.Remove(entry.Key);
                stopped.Add(entry.Key);
            }
            catch (Exception error) { errors.Add(new AppException(new("ProcessStopFailed", entry.Key), error)); }
        }
        StoppedServices = stopped.AsReadOnly();
        if (errors.Count > 0)
            throw new AggregateException(errors);
    }
}
