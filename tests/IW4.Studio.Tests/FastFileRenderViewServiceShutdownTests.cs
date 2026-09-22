using IW4.Studio.Desktop.Rendering;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class FastFileRenderViewServiceShutdownTests
{
    [Fact]
    public async Task DisposeAsync_cancels_before_drain_and_all_callers_share_completion()
    {
        var buildStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBuild = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task? activeBuild = null;
        int beginShutdownCount = 0;
        var lifetime = new RenderServiceLifetime(() =>
        {
            Interlocked.Increment(ref beginShutdownCount);
            return [activeBuild!];
        });
        activeBuild = ObserveLifetimeAsync();

        await buildStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task firstShutdown = lifetime.DisposeAsync().AsTask();
        Task repeatedShutdown = lifetime.DisposeAsync().AsTask();

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(firstShutdown, repeatedShutdown);
        Assert.Equal(1, Volatile.Read(ref beginShutdownCount));
        Assert.False(firstShutdown.IsCompleted);
        Assert.True(lifetime.Token.IsCancellationRequested);

        releaseBuild.SetResult();
        await firstShutdown.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<ObjectDisposedException>(() => _ = lifetime.Token);

        async Task ObserveLifetimeAsync()
        {
            using CancellationTokenRegistration registration =
                lifetime.Token.Register(cancellationObserved.SetResult);
            buildStarted.SetResult();
            await releaseBuild.Task;
        }
    }

    [Fact]
    public async Task DisposeAsync_treats_cancelled_builds_as_a_successful_drain()
    {
        Task cancelledBuild = Task.FromCanceled(
            new CancellationToken(canceled: true));
        var lifetime = new RenderServiceLifetime(() => [cancelledBuild]);

        await lifetime.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_propagates_build_failure_after_releasing_lifetime()
    {
        var expected = new InvalidOperationException("scene build failed");
        var lifetime = new RenderServiceLifetime(
            () => [Task.FromException(expected)]);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifetime.DisposeAsync().AsTask());

        Assert.Same(expected, actual);
        Assert.Throws<ObjectDisposedException>(() => _ = lifetime.Token);
    }

    [Fact]
    public async Task A_fresh_service_can_be_created_after_prior_shutdown()
    {
        var prior = new FastFileRenderViewService();

        await prior.DisposeAsync();
        await prior.DisposeAsync();

        var replacement = new FastFileRenderViewService();
        await replacement.DisposeAsync();
    }
}
