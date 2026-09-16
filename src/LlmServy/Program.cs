using LlmServy.Configuration;
using LlmServy.Diagnostics;
using LlmServy.Localization;
using LlmServy.Services;
using LlmServy.UI;

namespace LlmServy;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (DiagnosticCommands.IsRequested(args))
        {
            var diagnostics = new DiagnosticCommands();
            diagnostics.Run(args);
            return diagnostics.ExitCode;
        }
        var localizer = new Localizer();
        localizer.SetLanguage("system");
        var paths = new AppPaths();
        if (args is ["--import-settings", var legacy])
        {
            try
            {
                if (File.Exists(paths.SettingsFile))
                    throw new AppException(new("SettingsExist"));
                var imported = SettingsStore.ReadLegacy(legacy);
                SettingsValidator.Validate(imported);
                new SettingsStore(paths).Save(imported);
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(localizer.Error(error)); return 1; }
        }
        ApplicationConfiguration.Initialize();
        // The shared mutex also excludes older releases that use the previous application name.
        using var mutex = new Mutex(true, "Local\\LlamaDshLauncher", out bool first);
        if (!first)
        {
            MessageBox.Show(localizer.Get("AlreadyRunning"));
            return 0;
        }
        try
        {
            var legacyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DshLauncher", "settings.json");
            new SettingsMigration(paths).ImportIfMissing(legacyFile);
            Application.Run(new LauncherForm(paths));
            return 0;
        }
        catch (Exception error) { MessageBox.Show(localizer.Error(error), "LLM Servy"); return 1; }
    }
}
