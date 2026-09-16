using System.ComponentModel;
using System.Drawing.Design;
using LlmServy.Runtime;

namespace LlmServy.UI;

public class FolderEditor : UITypeEditor
{
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;
    public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
    {
        if (context?.Instance is not SettingsView view)
            return value;
        using var picker = new FolderBrowserDialog { Description = context.PropertyDescriptor?.DisplayName ?? view.Localizer.Get("SelectFolder"), UseDescriptionForTitle = true, SelectedPath = value as string ?? "" };
        return picker.ShowDialog() == DialogResult.OK ? picker.SelectedPath : value;
    }
}

public sealed class WslFolderEditor : FolderEditor
{
    public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
    {
        if (context?.Instance is not SettingsView view)
            return value;
        var linuxPath = value as string ?? "/";
        using var picker = new FolderBrowserDialog
        {
            Description = view.Localizer.Get("SelectWslFolder", view.Settings.Distro),
            UseDescriptionForTitle = true,
            SelectedPath = @"\\wsl.localhost\" + view.Settings.Distro + linuxPath.Replace('/', '\\')
        };
        if (picker.ShowDialog() != DialogResult.OK)
            return value;
        try
        {
            return WslPath.GetLinuxPath(picker.SelectedPath, view.Settings.Distro);
        }
        catch (Exception error) { MessageBox.Show(view.Localizer.Error(error), view.Localizer.Get("Settings")); return value; }
    }
}
