using System.Globalization;
using System.Resources;
using LlmServy.Localization;
using LlmServy.Configuration;
using LlmServy.UI;

namespace LlmServy.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void Error_PreservesFailureType_WhenContextWrapsCause()
    {
        var localizer = new Localizer();
        var failure = new LlmServy.Services.AppException(new("ProcessStopFailed", "DeepSeek Harness"), new TimeoutException());

        var message = localizer.Error(failure);

        Assert.Contains("DeepSeek Harness", message);
        Assert.Contains("TimeoutException", message);
    }

    [Theory]
    [InlineData("system", "ru-RU", "ru")]
    [InlineData("system", "de-DE", "en")]
    [InlineData("en", "ru-RU", "en")]
    public void GetCulture_SelectsSupportedLanguage_WhenPreferenceResolved(string preference, string system, string expected) =>
        Assert.Equal(expected, Localizer.GetCulture(preference, CultureInfo.GetCultureInfo(system)).Name);

    [Fact]
    public void Resources_ContainSameKeys_WhenBothLanguagesLoaded()
    {
        var manager = new ResourceManager("LlmServy.Localization.Strings", typeof(Localizer).Assembly);
        var english = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var russian = manager.GetResourceSet(CultureInfo.GetCultureInfo("ru"), true, false)!;
        var englishKeys = english.Cast<System.Collections.DictionaryEntry>().Select(x => (string)x.Key).Order().ToArray();
        var russianKeys = russian.Cast<System.Collections.DictionaryEntry>().Select(x => (string)x.Key).Order().ToArray();
        Assert.Equal(englishKeys, russianKeys);
        Assert.All(englishKeys, key => Assert.False(string.IsNullOrWhiteSpace(russian.GetString(key))));
    }

    [Fact]
    public void Resources_AcceptSameArguments_WhenLanguagesDiffer()
    {
        var manager = new ResourceManager("LlmServy.Localization.Strings", typeof(Localizer).Assembly);
        var english = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var russian = manager.GetResourceSet(CultureInfo.GetCultureInfo("ru"), true, false)!;

        foreach (System.Collections.DictionaryEntry entry in english)
        {
            var first = System.Text.CompositeFormat.Parse((string)entry.Value!);
            var second = System.Text.CompositeFormat.Parse(russian.GetString((string)entry.Key)!);
            Assert.Equal(first.MinimumArgumentCount, second.MinimumArgumentCount);
        }
    }

    [Fact]
    public void SetValue_SavesStableLanguageCode_WhenTranslatedValueSelected()
    {
        var localizer = new Localizer();
        localizer.SetLanguage("ru");
        var settings = new AppSettings();
        var view = new SettingsView(settings, localizer);
        var language = view.GetProperties()["Language"]!;
        int changes = 0;
        view.LanguageChanged += () => changes++;

        language.SetValue(view, language.Converter.ConvertFromString("English"));

        Assert.Equal("en", settings.Language);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void SettingsView_UsesTranslatedMetadata_WhenLanguageChanges()
    {
        var localizer = new Localizer();
        var view = new SettingsView(new AppSettings(), localizer);
        localizer.SetLanguage("en");
        Assert.Equal("Language", view.GetProperties()["Language"]!.DisplayName);
        localizer.SetLanguage("ru");
        Assert.Equal("Язык", view.GetProperties()["Language"]!.DisplayName);
        Assert.All(view.GetProperties().Cast<System.ComponentModel.PropertyDescriptor>(), p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));
    }
}
