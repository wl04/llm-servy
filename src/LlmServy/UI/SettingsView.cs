using System.ComponentModel;
using System.Drawing.Design;
using LlmServy.Configuration;
using LlmServy.Localization;

namespace LlmServy.UI;

public sealed class SettingsView(AppSettings settings, Localizer localizer) : CustomTypeDescriptor
{
    public AppSettings Settings => settings;
    public Localizer Localizer => localizer;
    public event Action? LanguageChanged;
    private static readonly Dictionary<string, string> Groups = new()
    {
        ["Language"] = "GroupAppearance",
        ["ModelsDirectory"] = "GroupModels",
        ["LlamaDirectory"] = "GroupLlama",
        ["LlamaPort"] = "GroupLlama",
        ["BindAddress"] = "GroupLlama",
        ["Distro"] = "GroupWsl",
        ["WslDirectory"] = "GroupWsl",
        ["DshPort"] = "GroupDsh",
        ["DshPackage"] = "GroupDsh",
        ["OpenBrowser"] = "GroupDsh",
        ["TimeoutSeconds"] = "GroupStartup"
    };
    public override PropertyDescriptorCollection GetProperties() => GetProperties(null);
    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => new(
        TypeDescriptor.GetProperties(typeof(AppSettings)).Cast<PropertyDescriptor>()
            .Where(p => Groups.ContainsKey(p.Name)).Select(p => new SettingDescriptor(p, this)).ToArray(), true);
    public override object GetPropertyOwner(PropertyDescriptor? pd) => this;

    private sealed class SettingDescriptor(PropertyDescriptor inner, SettingsView view) : PropertyDescriptor(inner.Name, null)
    {
        public override string DisplayName => view.Localizer.Get(Name);
        public override string Description => view.Localizer.Get(Name + "Help");
        public override string Category => view.Localizer.Get(Groups[Name]);
        public override Type ComponentType => typeof(SettingsView);
        public override Type PropertyType => inner.PropertyType;
        public override bool IsReadOnly => false;
        public override object? GetValue(object? component) => inner.GetValue(view.Settings);
        public override void SetValue(object? component, object? value)
        {
            inner.SetValue(view.Settings, value);
            if (Name == "Language")
                view.LanguageChanged?.Invoke();
            OnValueChanged(component, EventArgs.Empty);
        }
        public override bool CanResetValue(object component) => false;
        public override void ResetValue(object component)
        {
        }
        public override bool ShouldSerializeValue(object component) => false;
        public override TypeConverter Converter => Name == "Language" ? new LanguageConverter(view.Localizer) : PropertyType == typeof(bool) ? new LocalizedBooleanConverter(view.Localizer) : inner.Converter;
        public override object? GetEditor(Type editorBaseType) => editorBaseType == typeof(UITypeEditor) ? Name switch
        {
            "ModelsDirectory" or "LlamaDirectory" => new FolderEditor(),
            "WslDirectory" => new WslFolderEditor(),
            _ => inner.GetEditor(editorBaseType)
        } : inner.GetEditor(editorBaseType);
    }
}

public sealed class LanguageConverter(Localizer localizer) : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => new(new[] { "system", "ru", "en" });
    public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType) => destinationType == typeof(string) ? value switch
    {
        "system" => localizer.Get("System"),
        "ru" => "Русский",
        "en" => "English",
        _ => value
    } : base.ConvertTo(context, culture, value, destinationType);
    public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value) => value switch
    {
        "Русский" => "ru",
        "English" => "en",
        "System" or "Системный" => "system",
        _ => base.ConvertFrom(context, culture, value)
    };
}

public sealed class LocalizedBooleanConverter(Localizer localizer) : BooleanConverter
{
    public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is bool flag ? localizer.Get(flag ? "Yes" : "No") : base.ConvertTo(context, culture, value, destinationType);
    public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value) => value switch
    {
        "Yes" or "Да" => true,
        "No" or "Нет" => false,
        _ => base.ConvertFrom(context, culture, value)
    };
}
