using LlmServy.Configuration;

namespace LlmServy.Tests;

public sealed class AtomicSettingsTests
{
    [Fact]
    public void Write_PreservesBothErrors_WhenPublicationAndCleanupFail()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(destination);
        var cleanupFailure = new IOException("cleanup failed");
        var file = new AtomicSettingsFile(_ => throw cleanupFailure);
        try
        {
            var failure = Assert.Throws<AggregateException>(() => file.Write(destination, "{}", true));

            Assert.Equal(2, failure.InnerExceptions.Count);
            Assert.IsAssignableFrom<IOException>(failure.InnerExceptions[0]);
            Assert.Same(cleanupFailure, failure.InnerExceptions[1]);
        }
        finally { Directory.Delete(directory, true); }
    }
}
