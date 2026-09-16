using LlmServy.Configuration;
using LlmServy.Services;

namespace LlmServy.Tests;

public sealed class MigrationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "LlmServy.Tests", Guid.NewGuid().ToString("N"));
    public MigrationTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void ImportIfMissing_PreservesOriginal_WhenLegacySettingsValid()
    {
        var legacy = Path.Combine(directory, "legacy.json");
        const string source = """{"DshPort":4000,"PresetPath":"C:\\models\\test.ini"}""";
        File.WriteAllText(legacy, source);
        var paths = new AppPaths(Path.Combine(directory, "current"));

        new SettingsMigration(paths).ImportIfMissing(legacy);

        var settings = new SettingsStore(paths).Load();
        Assert.Equal(4000, settings.DshPort);
        Assert.Equal(@"C:\models", settings.ModelsDirectory);
        Assert.Equal(source, File.ReadAllText(legacy));
    }

    [Fact]
    public void ImportIfMissing_DoesNotReplaceSettings_WhenTargetExists()
    {
        var legacy = Path.Combine(directory, "legacy.json");
        File.WriteAllText(legacy, """{"DshPort":4000}""");
        var paths = new AppPaths(directory);
        new SettingsStore(paths).Save(new AppSettings { DshPort = 5000 });

        new SettingsMigration(paths).ImportIfMissing(legacy);

        Assert.Equal(5000, new SettingsStore(paths).Load().DshPort);
    }

    [Fact]
    public void ImportIfMissing_DoesNotCreateTarget_WhenLegacyJsonInvalid()
    {
        var legacy = Path.Combine(directory, "broken.json");
        File.WriteAllText(legacy, "{broken");
        var paths = new AppPaths(Path.Combine(directory, "new"));

        Assert.Throws<AppException>(() => new SettingsMigration(paths).ImportIfMissing(legacy));

        Assert.False(File.Exists(paths.SettingsFile));
    }

    [Fact]
    public void Save_PreservesLanguage_WhenConfigurationReloaded()
    {
        var store = new SettingsStore(new AppPaths(directory));
        store.Save(new AppSettings { Language = "ru" });

        Assert.Equal("ru", store.Load().Language);
    }
}
