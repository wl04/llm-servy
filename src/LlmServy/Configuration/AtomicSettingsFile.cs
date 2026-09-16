namespace LlmServy.Configuration;

/// <summary>Publishes a settings file atomically and removes its temporary file.</summary>
internal sealed class AtomicSettingsFile(Action<string>? deleteTemporary = null)
{
    private readonly Action<string> delete = deleteTemporary ?? File.Delete;

    public void Write(string destination, string contents, bool overwrite)
    {
        if (!Path.IsPathFullyQualified(destination))
            throw new ArgumentException("An absolute destination is required.", nameof(destination));
        var directory = Path.GetDirectoryName(destination) ?? throw new ArgumentException("An absolute destination is required.", nameof(destination));
        Directory.CreateDirectory(directory);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Exception? primaryFailure = null;
        try
        {
            File.WriteAllText(temporary, contents);
            if (overwrite && File.Exists(destination))
                File.Replace(temporary, destination, destination + ".bak");
            else
                File.Move(temporary, destination, false);
        }
        catch (Exception error) { primaryFailure = error; throw; }
        finally
        {
            try
            {
                delete(temporary);
            }
            catch (Exception cleanupError) when (primaryFailure is not null)
            {
                throw new AggregateException(primaryFailure, cleanupError);
            }
        }
    }
}
