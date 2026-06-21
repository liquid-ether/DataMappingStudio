namespace App.Infrastructure.Remote;

/// <summary>
/// Writes a file atomically: the producer writes to a temp file, which then replaces the target via a
/// single rename. Used for snapshot rebuilds (a multi-writer race that is harmless but must never leave
/// a half-written file) and per-writer log appends (Architecture §6a/§7a).
/// </summary>
internal static class AtomicWrite
{
    public static void Write(string targetPath, Action<string> writeTo)
    {
        string temp = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            writeTo(temp);
            // File.Move with overwrite is atomic on the same volume.
            File.Move(temp, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch (IOException) { /* best effort */ }
            }
        }
    }
}
