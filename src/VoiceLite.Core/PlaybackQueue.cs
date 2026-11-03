namespace VoiceLite.Core;

public sealed class PlaybackQueue
{
    private readonly object sync = new();
    private CancellationTokenSource? active;
    private Task tail = Task.CompletedTask;
    private bool closed;
    public Task<double> EnqueueAsync(Func<CancellationToken, Task<double>> play)
    {
        lock (sync)
        {
            if (closed) return Task.FromCanceled<double>(new CancellationToken(true));
            active?.Cancel();
            var cancellation = new CancellationTokenSource(); active = cancellation;
            var next = RunAsync(tail, play, cancellation);
            tail = next; return next;
        }
    }
    private async Task<double> RunAsync(Task previous, Func<CancellationToken, Task<double>> play, CancellationTokenSource cancellation)
    {
        try
        {
            try { await previous; } catch (Exception) { }
            cancellation.Token.ThrowIfCancellationRequested();
            return await play(cancellation.Token);
        }
        finally
        {
            lock (sync)
            {
                if (ReferenceEquals(active, cancellation)) active = null;
                cancellation.Dispose();
            }
        }
    }
    public void Cancel() { lock (sync) active?.Cancel(); }
    public Task CloseAsync()
    {
        lock (sync) { closed = true; active?.Cancel(); return tail; }
    }
}
