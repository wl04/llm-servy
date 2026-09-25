using LlmServy.Runtime;

namespace LlmServy.Tests;

public sealed class LifetimeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StopAsync_AttemptsEveryService_WhenOneStopFails(bool failingServiceAttachedFirst)
    {
        var lifetime = new ServiceLifetime();
        var failing = new FakeProcess(true);
        var healthy = new FakeProcess(false);
        if (failingServiceAttachedFirst)
            lifetime.Attach("DSH", failing);
        lifetime.Attach("llama", healthy);
        if (!failingServiceAttachedFirst)
            lifetime.Attach("DSH", failing);

        await Assert.ThrowsAnyAsync<Exception>(() => lifetime.StopAsync());

        Assert.Equal(1, failing.StopCount);
        Assert.Equal(1, healthy.StopCount);
        Assert.Contains("llama", lifetime.StoppedServices);
        Assert.DoesNotContain("DSH", lifetime.StoppedServices);
        Assert.True(lifetime.HasProcesses);
    }

    [Fact]
    public async Task StopAsync_DoesNotStopAgain_WhenAlreadyStopped()
    {
        var lifetime = new ServiceLifetime();
        var process = new FakeProcess(false);
        lifetime.Attach("llama", process);

        await lifetime.StopAsync();
        await lifetime.StopAsync();

        Assert.Equal(1, process.StopCount);
        Assert.False(lifetime.HasProcesses);
    }

    [Fact]
    public async Task StopAsync_PreservesOtherServices_WhenSingleServiceReleased()
    {
        var lifetime = new ServiceLifetime();
        var llama = new FakeProcess(false);
        var pi = new FakeProcess(false);
        lifetime.Attach("llama.cpp", llama);
        lifetime.Attach("Pi", pi);

        await lifetime.StopAsync("Pi");
        lifetime.Attach("Pi", new FakeProcess(false));

        Assert.Equal(1, pi.StopCount);
        Assert.Equal(0, llama.StopCount);
        await lifetime.StopAsync();
        Assert.Equal(1, llama.StopCount);
    }

    private sealed class FakeProcess(bool fails) : IServiceLifetime
    {
        public int StopCount
        {
            get; private set;
        }
        public Task StopAsync()
        {
            StopCount++;
            return fails ? Task.FromException(new IOException("stop failed")) : Task.CompletedTask;
        }
    }
}
