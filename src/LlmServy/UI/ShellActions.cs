using System.Diagnostics;
using LlmServy.Services;

namespace LlmServy.UI;

public static class ShellActions
{
    public static void OpenTerminal(LlmServy.Runtime.TerminalSession terminal)
    {
        try
        {
            var start = new ProcessStartInfo("wt.exe") { UseShellExecute = true };
            foreach (var arg in new[] { "-w", "new", "new-tab", "--title", "Pi", "wsl.exe", "-d", terminal.Distro,
                "--exec", "tmux", "-S", terminal.Socket, "attach-session", "-t", "pi" })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new AppException(new("PiTerminalFailed"), error);
        }
    }

    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new AppException(new("OpenFailed"), error);
        }
    }

    public static void OpenFile(string file, string missingCode)
    {
        if (!File.Exists(file))
            throw new AppException(new(missingCode));
        Open(file);
    }
}
