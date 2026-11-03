namespace VoiceLite.Core;
public static class PlaybackAwaiter
{
    public static async Task WaitAsync(Task opened, Task completed, TimeSpan openingTimeout, CancellationToken cancellationToken)
    {
        await opened.WaitAsync(openingTimeout, cancellationToken);
        await completed.WaitAsync(cancellationToken);
    }
}
