using System.Text.Json;
using LlmServy.Configuration;
using LlmServy.Services;

namespace LlmServy.Tests;

public sealed class PiSettingsTests
{
    [Fact]
    public void Deserialize_PreservesDshSelection_WhenLegacySettingsHaveNoHarnessKind()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{"WslDirectory":"/home/example/project"}""") ?? throw new InvalidOperationException();

        Assert.Equal("dsh", settings.HarnessKind);
        Assert.Equal("/home/example/project", settings.WslDirectory);
    }

    [Fact]
    public void ValidateValues_IgnoresDshOptions_WhenPiSelected()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{"HarnessKind":"pi","PiProvider":"local","DshPort":-1,"DshPackage":""}""") ?? throw new InvalidOperationException();
        SettingsValidator.ValidateValues(settings);
    }

    [Fact]
    public void ValidateValues_RejectsUnknownHarness_WhenConfigurationInvalid()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{"HarnessKind":"unknown"}""") ?? throw new InvalidOperationException();
        Assert.Throws<AppException>(() => SettingsValidator.ValidateValues(settings));
    }
}
