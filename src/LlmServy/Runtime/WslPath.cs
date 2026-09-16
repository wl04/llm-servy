using LlmServy.Services;

namespace LlmServy.Runtime;

public static class WslPath
{
    public static string GetLinuxPath(string path, string distro)
    {
        var normalized = path.Replace('\\', '/');
        foreach (var host in new[] { "//wsl.localhost/", "//wsl$/" })
        {
            if (!normalized.StartsWith(host, StringComparison.OrdinalIgnoreCase))
                continue;
            var rest = normalized[host.Length..];
            var slash = rest.IndexOf('/');
            var selectedDistro = slash < 0 ? rest : rest[..slash];
            if (!selectedDistro.Equals(distro, StringComparison.OrdinalIgnoreCase))
                throw new AppException(new("WrongDistro", distro));
            return slash < 0 ? "/" : rest[slash..];
        }
        throw new AppException(new("WslPathRequired", distro));
    }
}
