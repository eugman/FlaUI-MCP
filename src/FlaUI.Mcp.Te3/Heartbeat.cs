/// <summary>Whether a thread that should beat regularly still does.</summary>
internal sealed class Heartbeat(long start)
{
    internal const int StaleAfterMs = 2_000;
    private long last = start;

    public void Beat(long now) => Interlocked.Exchange(ref last, now);

    public bool Alive(long now) => now - Interlocked.Read(ref last) <= StaleAfterMs;
}
