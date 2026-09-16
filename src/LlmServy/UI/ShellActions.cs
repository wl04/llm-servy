using System.Diagnostics;
using LlmServy.Services;

namespace LlmServy.UI;

public static class ShellActions
{
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
