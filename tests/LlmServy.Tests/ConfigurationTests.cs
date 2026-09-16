using LlmServy.Configuration;
using LlmServy.Models;

namespace LlmServy.Tests;

public sealed class ConfigurationTests : IDisposable
{
    private readonly string temp = Path.Combine(Path.GetTempPath(), "LlmServy.Tests", Guid.NewGuid().ToString("N"));
    public ConfigurationTests() => Directory.CreateDirectory(temp);
    public void Dispose() => Directory.Delete(temp, true);

    [Fact]
    public void Save_PreservesAbsoluteIniPath_WhenPathContainsSpacesAndUnicode()
    {
        string folder = Path.Combine(temp, "Модели и INI");
        Directory.CreateDirectory(folder);
        string ini = Path.Combine(folder, "модель.ini");
        File.WriteAllText(ini, "version=1\n[*]\nparallel=1\n[model.gguf]\nmodel=relative.gguf\n");
        var paths = new AppPaths(Path.Combine(temp, "user-data"));
        var store = new SettingsStore(paths);
        var settings = new AppSettings { PresetPath = ini, SelectedModelId = "model.gguf" };
        settings.Validate();
        store.Save(settings);
        var loaded = store.Load();
        Assert.Equal(ini, loaded.PresetPath);
        var preset = Assert.Single(Preset.Read(loaded.PresetPath));
        Assert.Equal(folder, preset.WorkingDirectory);
        Assert.Equal("model.gguf", preset.ModelId);
        Assert.NotEqual(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), preset.WorkingDirectory);
        Assert.False(File.Exists(Path.Combine(folder, "settings.json")));
    }

    [Fact]
    public void ReadLegacy_ResolvesIniAgainstSettingsFile_WhenPathIsRelative()
    {
        var file = Path.Combine(temp, "launcher-settings.json");
        File.WriteAllText(file, """{"SelectedPreset":"old.ini","DshPackage":"@deepseek-ai/dsh@0.1.5-rc.1","LlamaPort":1235}""");
        var settings = SettingsStore.ReadLegacy(file);
        Assert.Equal(Path.Combine(temp, "old.ini"), settings.PresetPath);
        Assert.Equal(1235, settings.LlamaPort);
        Assert.Equal("@deepseek-ai/dsh@0.1.5-rc.1", settings.DshPackage);
    }

    [Fact]
    public void Save_KeepsPreviousBackup_WhenSettingsExist()
    {
        var paths = new AppPaths(temp);
        var store = new SettingsStore(paths);
        store.Save(new AppSettings { DshPort = 3080 });
        store.Save(new AppSettings { DshPort = 3081 });
        Assert.Equal(3081, store.Load().DshPort);
        Assert.Contains("3080", File.ReadAllText(paths.SettingsFile + ".bak"));
    }

    [Fact]
    public void Edit_PreservesSessionSnapshot_WhenSettingsChange()
    {
        var settings = new AppSettings { PresetPath = @"C:\models\first.ini" };
        var session = settings with
        {
        };
        settings.PresetPath = @"D:\models\second.ini";
        settings.DshPort = 4000;
        Assert.Equal(@"C:\models\first.ini", session.PresetPath);
        Assert.Equal(3080, session.DshPort);
    }

    [Fact]
    public void Read_RejectsDuplicateSections_WhenModelIdRepeated()
    {
        string ini = Path.Combine(temp, "bad.ini");
        File.WriteAllText(ini, "[model]\n[model]\n");
        Assert.Throws<LlmServy.Services.AppException>(() => Preset.Read(ini));
    }

    [Fact]
    public void Validate_RejectsMissingIni_WhenLaunchRequested() =>
        Assert.Throws<LlmServy.Services.AppException>(() => new AppSettings { PresetPath = Path.Combine(temp, "missing.ini") }.Validate());
}
