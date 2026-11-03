using VoiceLite.Core;
using Xunit;
namespace VoiceLite.Tests;

public sealed class PlaybackQueueTests
{
    [Fact] public async Task Closing_blocks_new_preview_even_while_old_cleanup_is_pending()
    {
        var queue = new PlaybackQueue();
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = queue.EnqueueAsync(async token => {
            try { await Task.Delay(Timeout.Infinite, token); return 1; }
            finally { await cleanup.Task; }
        });
        try
        {
            var close = queue.CloseAsync();
            var started = false;
            var rejected = queue.EnqueueAsync(_ => { started = true; return Task.FromResult(2.0); });
            Assert.True(rejected.IsCanceled); Assert.False(started); Assert.False(close.IsCompleted);
            cleanup.SetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => close);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        }
        finally { cleanup.TrySetResult(); }
    }

    [Fact] public async Task Next_preview_waits_for_previous_conversion_cleanup()
    {
        var queue = new PlaybackQueue();
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = queue.EnqueueAsync(async token => {
            try { await Task.Delay(Timeout.Infinite, token); return 1; }
            finally { await cleanup.Task; }
        });
        try
        {
            var started = false;
            var next = queue.EnqueueAsync(_ => { started = true; return Task.FromResult(2.0); });
            Assert.False(started);
            cleanup.SetResult();
            Assert.Equal(2.0, await next); Assert.True(started);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            await queue.CloseAsync();
        }
        finally { cleanup.TrySetResult(); }
    }
}
