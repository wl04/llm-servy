using LlmServy.Services;

namespace LlmServy.Models;

public sealed record CatalogResult(IReadOnlyList<Preset> Models, IReadOnlyList<AppMessage> Diagnostics);

public sealed record Preset(string IniPath, string ModelId)
{
    public string WorkingDirectory => Path.GetDirectoryName(IniPath) ?? throw new AppException(new("InvalidIni"));
    public override string ToString() => $"{ModelId} — {Path.GetFileName(IniPath)}";

    public static CatalogResult Scan(string directory)
    {
        var models = new List<Preset>();
        var diagnostics = new List<AppMessage>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory).Where(p => Path.GetExtension(p).Equals(".ini", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    models.AddRange(Read(file));
                }
                catch (AppException error) { diagnostics.Add(new("IniReadFailed", Path.GetFileName(file))); diagnostics.Add(error.Detail); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(new("IniReadFailed", Path.GetFileName(file)));
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new AppException(new("CatalogReadFailed"), error);
        }
        return new(models.AsReadOnly(), diagnostics.AsReadOnly());
    }

    public static IReadOnlyList<Preset> Read(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new AppException(new("InvalidIni"));
        path = Path.GetFullPath(path);
        var presets = new List<Preset>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (!line.StartsWith('[') || !line.EndsWith(']'))
                continue;
            var name = line[1..^1].Trim();
            if (name is "*" or "")
                continue;
            if (!names.Add(name))
                throw new AppException(new("DuplicateSection", name));
            presets.Add(new(path, name));
        }
        if (presets.Count == 0)
            throw new AppException(new("NoModels"));
        return presets.AsReadOnly();
    }
}
