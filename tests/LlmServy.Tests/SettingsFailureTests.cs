using LlmServy.Configuration;
using LlmServy.Services;

namespace LlmServy.Tests;

public sealed class SettingsFailureTests
{
    [Theory]
    [InlineData("{\"WslDirectory\":null}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"WslDirectory\":123}")]
    [InlineData("{\"ModelsDirectory\":null}")]
    [InlineData("{\"PresetPath\":null}")]
    public void Load_RejectsInvalidValues_WhenSettingsJsonMalformed(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var paths = new AppPaths(directory);
            File.WriteAllText(paths.SettingsFile, json);
            var store = new SettingsStore(paths);

            Assert.Throws<AppException>(() => store.Load());
        }
        finally { Directory.Delete(directory, true); }
    }
}
