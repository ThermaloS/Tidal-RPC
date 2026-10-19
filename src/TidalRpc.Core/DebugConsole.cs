namespace TidalRpc;

// Memory only. Callers provide selected diagnostic fields, never raw API payloads.
public static class DebugConsole
{
    private static readonly object Gate = new();
    private static readonly Queue<string> Lines = new();
    private static long revision;
    public static void Write(string source, string message)
    {
        message = message.Replace('\r', ' ').Replace('\n', ' ');
        if (message.Length > 600) message = message[..600];
        lock (Gate)
        {
            Lines.Enqueue($"{DateTime.Now:HH:mm:ss.fff} [{source}] {message}");
            while (Lines.Count > 500) Lines.Dequeue();
            revision++;
        }
    }
    public static (long Revision, string Text) Snapshot()
    {
        lock (Gate) return (revision, string.Join(Environment.NewLine, Lines));
    }
    public static void Clear() { lock (Gate) { Lines.Clear(); revision++; } }
}
