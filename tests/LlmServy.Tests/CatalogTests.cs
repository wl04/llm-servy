using LlmServy.Configuration;
using LlmServy.Models;
using LlmServy.Runtime;
using LlmServy.Services;

namespace LlmServy.Tests;

public sealed class CatalogTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    public CatalogTests() => Directory.CreateDirectory(folder);
    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void Scan_KeepsDistinctPresets_WhenFilesShareModelId()
    {
        File.WriteAllText(Path.Combine(folder, "one.ini"), "[same]\n");
        File.WriteAllText(Path.Combine(folder, "two.ini"), "[same]\n");

        var catalog = Preset.Scan(folder);

        Assert.Equal(2, catalog.Models.Count);
        Assert.All(catalog.Models, model => Assert.Equal("same", model.ModelId));
        Assert.Equal(2, catalog.Models.Select(model => model.IniPath).Distinct().Count());
    }

    [Fact]
    public void Scan_ReturnsDiagnosticAndValidModel_WhenAnotherIniIsBroken()
    {
        File.WriteAllText(Path.Combine(folder, "valid.ini"), "[valid]\n");
        File.WriteAllText(Path.Combine(folder, "broken.ini"), "[duplicate]\n[duplicate]\n");

        var catalog = Preset.Scan(folder);

        Assert.Equal("valid", Assert.Single(catalog.Models).ModelId);
        Assert.Contains(catalog.Diagnostics, message => message.Code == "DuplicateSection");
    }

    [Fact]
    public void Scan_ReadsOnlyTopLevelIni_WhenFolderContainsOtherFiles()
    {
        File.WriteAllText(Path.Combine(folder, "model.INI"), "[model]\n");
        File.WriteAllText(Path.Combine(folder, "ignore.txt"), "[ignored]\n");
        var subfolder = Path.Combine(folder, "nested");
        Directory.CreateDirectory(subfolder);
        File.WriteAllText(Path.Combine(subfolder, "nested.ini"), "[nested]\n");

        var catalog = Preset.Scan(folder);

        Assert.Equal("model", Assert.Single(catalog.Models).ModelId);
    }

    [Theory]
    [InlineData(@"\\wsl.localhost\Ubuntu\home\user\my project", "/home/user/my project")]
    [InlineData(@"\\wsl$\Ubuntu\mnt\c\dev", "/mnt/c/dev")]
    public void GetLinuxPath_ConvertsPath_WhenDistroMatches(string path, string expected) =>
        Assert.Equal(expected, WslPath.GetLinuxPath(path, "Ubuntu"));

    [Fact]
    public void GetLinuxPath_RejectsPath_WhenDistroDiffers() =>
        Assert.Throws<AppException>(() => WslPath.GetLinuxPath(@"\\wsl.localhost\Debian\home", "Ubuntu"));

    [Fact]
    public void MigratePaths_DerivesModelsDirectory_WhenLegacyIniConfigured()
    {
        var settings = new AppSettings { PresetPath = @"C:\models\selected.ini" };

        settings.MigratePaths();

        Assert.Equal(@"C:\models", settings.ModelsDirectory);
        Assert.Equal(@"C:\models\selected.ini", settings.PresetPath);
    }
}
