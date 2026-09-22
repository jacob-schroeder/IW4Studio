using IW4.Studio.Desktop.Rendering;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class RenderViewLifecycleTests
{
    [Fact]
    public async Task Editor_transition_closes_borrowers_and_awaits_service_before_proceeding()
    {
        var releaseService = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ControlledAsyncDisposable(releaseService.Task);
        var lifecycle = new SharedRenderViewServiceLifecycle<
            object,
            ControlledAsyncDisposable>();
        var firstPreview = new object();
        var secondPreview = new object();
        lifecycle.GetOrCreateService(() => service);
        lifecycle.TrackPreview(firstPreview);
        lifecycle.TrackPreview(secondPreview);
        int closedPreviewCount = 0;
        bool proceeded = false;

        Task transition = lifecycle.ShutdownThenProceedAsync(
            preview =>
            {
                int closed = Interlocked.Increment(ref closedPreviewCount);
                lifecycle.ReleasePreview(preview);
                if (closed == 1)
                    Assert.Equal(0, service.DisposeCallCount);
            },
            exception => throw new Xunit.Sdk.XunitException(
                $"Unexpected shutdown failure: {exception}"),
            () =>
            {
                proceeded = true;
                return Task.CompletedTask;
            });

        Assert.Equal(2, Volatile.Read(ref closedPreviewCount));
        Assert.Equal(1, service.DisposeCallCount);
        Assert.False(transition.IsCompleted);
        Assert.False(proceeded);

        releaseService.SetResult();
        await transition.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(proceeded);
        Assert.True(lifecycle.IsEmpty);
    }

    [Fact]
    public async Task Editor_transition_reports_drain_failure_and_does_not_proceed()
    {
        var expected = new InvalidOperationException("shutdown failed");
        var service = new ControlledAsyncDisposable(Task.FromException(expected));
        var lifecycle = new SharedRenderViewServiceLifecycle<
            object,
            ControlledAsyncDisposable>();
        lifecycle.GetOrCreateService(() => service);
        Exception? reported = null;
        bool proceeded = false;

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.ShutdownThenProceedAsync(
                _ => { },
                exception => reported = exception,
                () =>
                {
                    proceeded = true;
                    return Task.CompletedTask;
                }));

        Assert.Same(expected, actual);
        Assert.Same(expected, reported);
        Assert.False(proceeded);
        Assert.True(lifecycle.IsEmpty);
    }

    [Fact]
    public async Task Shared_owner_finishes_prior_shutdown_before_creating_replacement()
    {
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new ControlledAsyncDisposable(releaseFirst.Task);
        var second = new ControlledAsyncDisposable(Task.CompletedTask);
        var owner = new RenderViewServiceOwner<ControlledAsyncDisposable>();

        Assert.Same(first, owner.GetOrCreate(() => first));
        Task firstDisposal = owner.BeginDisposal();
        Task repeatedDisposal = owner.BeginDisposal();

        Assert.Same(firstDisposal, repeatedDisposal);
        Assert.Equal(1, first.DisposeCallCount);
        Assert.False(firstDisposal.IsCompleted);
        Assert.Throws<InvalidOperationException>(() =>
            owner.GetOrCreate(() => second));

        Task wait = owner.WaitForDisposalAsync();
        Assert.False(wait.IsCompleted);
        releaseFirst.SetResult();
        await wait.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(second, owner.GetOrCreate(() => second));
        await owner.DisposeAsync();
        Assert.Equal(1, second.DisposeCallCount);
        Assert.True(owner.IsEmpty);
    }

    [Fact]
    public async Task Shared_owner_surfaces_shutdown_failure_and_allows_later_generation()
    {
        var expected = new InvalidOperationException("shutdown failed");
        var failed = new ControlledAsyncDisposable(Task.FromException(expected));
        var replacement = new ControlledAsyncDisposable(Task.CompletedTask);
        var owner = new RenderViewServiceOwner<ControlledAsyncDisposable>();
        owner.GetOrCreate(() => failed);
        Task failedDisposal = owner.BeginDisposal();

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            owner.WaitForDisposalAsync);

        Assert.Same(expected, actual);
        Assert.True(failedDisposal.IsFaulted);
        Assert.Same(replacement, owner.GetOrCreate(() => replacement));
        await owner.DisposeAsync();
        Assert.True(owner.IsEmpty);
    }

    [Fact]
    public async Task Standalone_close_gate_coalesces_retries_until_drain_completes()
    {
        var releaseDrain = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new RenderViewCloseDrainGate();
        int drainCount = 0;
        int retryCount = 0;

        Task firstDrain = gate.BeginDrain(
            () =>
            {
                Interlocked.Increment(ref drainCount);
                return releaseDrain.Task;
            },
            exception => throw new Xunit.Sdk.XunitException(
                $"Unexpected shutdown failure: {exception}"),
            () => Interlocked.Increment(ref retryCount));
        Task repeatedDrain = gate.BeginDrain(
            () => throw new Xunit.Sdk.XunitException(
                "Repeated close started a second drain."),
            exception => throw new Xunit.Sdk.XunitException(
                $"Unexpected shutdown failure: {exception}"),
            () => Interlocked.Increment(ref retryCount));

        Assert.Same(firstDrain, repeatedDrain);
        Assert.True(gate.HasPendingDrain);
        Assert.False(gate.TryConsumeApproval());
        Assert.Equal(1, Volatile.Read(ref drainCount));
        Assert.Equal(0, Volatile.Read(ref retryCount));

        releaseDrain.SetResult();
        await firstDrain.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(gate.HasPendingDrain);
        Assert.Equal(1, Volatile.Read(ref retryCount));
        Assert.True(gate.TryConsumeApproval());
        Assert.False(gate.TryConsumeApproval());
    }

    [Fact]
    public async Task Standalone_close_gate_reports_failure_before_approving_second_close()
    {
        var expected = new InvalidOperationException("drain failed");
        var gate = new RenderViewCloseDrainGate();
        Exception? reported = null;
        int retryCount = 0;

        await gate.BeginDrain(
            () => Task.FromException(expected),
            exception => reported = exception,
            () => Interlocked.Increment(ref retryCount));

        Assert.Same(expected, reported);
        Assert.Equal(0, Volatile.Read(ref retryCount));
        Assert.False(gate.HasPendingDrain);
        Assert.True(gate.TryConsumeApproval());
    }

    private sealed class ControlledAsyncDisposable(Task disposal) : IAsyncDisposable
    {
        private int _disposeCallCount;

        internal int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCallCount);
            return new ValueTask(disposal);
        }
    }
}
