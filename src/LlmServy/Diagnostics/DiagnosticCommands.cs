using System.Diagnostics;
using LlmServy.Configuration;
using LlmServy.UI;

namespace LlmServy.Diagnostics;

/// <summary>Explicit diagnostic entry points; normal application startup never runs these fixtures.</summary>
internal sealed class DiagnosticCommands
{
    public int ExitCode
    {
        get; private set;
    }
    public static bool IsRequested(string[] args) => args.Any(x => x is "--self-test" or "--dsh-integration-test" or "--runtime-integration-test" or "--ui-smoke" or "--settings-smoke" or "--test-echo" or "--test-sleep" or "--test-tree");

    public void Run(string[] args)
    {
        if (args.Contains("--test-echo"))
        {
            foreach (var argument in args.Skip(1))
                Console.WriteLine(argument);
            Console.Error.WriteLine("diagnostic");
            ExitCode = 23;
            return;
        }
        if (args.Contains("--test-sleep"))
        {
            Thread.Sleep(60000);
            return;
        }
        if (args.Contains("--test-tree"))
        {
            using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path."), "--test-sleep") { UseShellExecute = false, CreateNoWindow = true }) ?? throw new InvalidOperationException("Diagnostic child was not created.");
            Console.WriteLine(child.Id);
            Console.Out.Flush();
            Thread.Sleep(60000);
            return;
        }
        string? destination = GetOption(args, "--diagnostics-dir");
        var paths = new AppPaths(destination);
        if (args.Contains("--self-test"))
        {
            ExitCode = SelfTest.Run(paths);
            return;
        }
        if (args.Contains("--dsh-integration-test"))
        {
            ExitCode = SelfTest.DshIntegration(paths);
            return;
        }
        if (args.Contains("--runtime-integration-test"))
        {
            var test = new RuntimeIntegration();
            test.RunAsync(paths).GetAwaiter().GetResult();
            ExitCode = test.ExitCode;
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        var settings = new SettingsStore(new AppPaths()).Load();
        var language = GetOption(args, "--language");
        if (language is not null)
            settings.Language = language;
        using Form form = args.Contains("--settings-smoke") ? new SettingsForm(settings, paths.SettingsFile) : new LauncherForm(paths, settings);
        form.Shown += (_, _) => form.BeginInvoke(() =>
        {
            form.PerformLayout();
            form.Update();
            Directory.CreateDirectory(paths.DataDirectory);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
            bitmap.Save(Path.Combine(paths.DataDirectory, "launcher-ui.png"));
            form.BeginInvoke(() => { form.Dispose(); Application.ExitThread(); });
        });
        Application.Run(form);
    }

    private static string? GetOption(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
