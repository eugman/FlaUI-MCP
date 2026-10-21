/// <summary>
/// Whether a thread that should beat regularly still does. One gap longer than StaleAfterMs poisons it for good:
/// Windows removes a low-level hook whose thread misses LowLevelHooksTimeout (about a second) without telling it, so
/// a later beat cannot prove the hooks came back. A new observer, started by the next batch, gets a new heartbeat.
/// </summary>
internal sealed class Heartbeat(long start)
{
    internal const int StaleAfterMs = 1_000;
    private long last = start;
    private int poisoned;

    public void Beat(long now)
    {
        Check(now);
        Interlocked.Exchange(ref last, now);
    }

    public bool Alive(long now)
    {
        Check(now);
        return Volatile.Read(ref poisoned) == 0;
    }

    private void Check(long now)
    {
        if (now - Interlocked.Read(ref last) > StaleAfterMs)
        {
            Volatile.Write(ref poisoned, 1);
        }
    }
}
