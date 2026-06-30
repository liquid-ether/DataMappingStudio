namespace App.Infrastructure.Remote;

/// <summary>
/// Retries transient file IO against the synced (OneDrive/SharePoint) folder. Sync clients and
/// anti-virus briefly lock files while uploading/scanning, so a read/write can fail with
/// <see cref="IOException"/> / <see cref="UnauthorizedAccessException"/> and succeed moments later. A
/// few short backoffs smooth those over; a persistent failure still surfaces (the caller records the
/// remote as unavailable).
/// </summary>
internal static class RemoteIo
{
    private static readonly int[] BackoffMs = [50, 150, 350, 700];

    public static T Retry<T>(Func<T> op)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return op();
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < BackoffMs.Length)
            {
                Thread.Sleep(BackoffMs[attempt]);
            }
        }
    }

    public static void Retry(Action op) => Retry(() => { op(); return true; });

    private static bool IsTransient(Exception ex) => ex is IOException or UnauthorizedAccessException;
}
