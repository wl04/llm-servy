namespace LlmServy.UI;

internal static class AppIcon
{
    // Shared for the lifetime of the application, independent of EXE location.
    public static System.Drawing.Icon Instance { get; } = Load();
    private static System.Drawing.Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("LlmServy.AppIcon.ico")
            ?? throw new InvalidOperationException("Application icon resource is missing.");
        using var icon = new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)icon.Clone();
    }
}
