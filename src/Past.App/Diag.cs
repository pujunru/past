namespace Past.App;

/// <summary>
/// Local-only startup/diagnostic log (metadata only — never clip contents).
/// Writes to %LOCALAPPDATA%\Past\diag.log. Nothing here ever leaves the machine.
/// </summary>
internal static class Diag
{
    private static readonly object Lock = new();
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Past", "diag.log");

    public static void Log(string message)
    {
        try
        {
            lock (Lock)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                System.IO.File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // diagnostics must never take the app down
        }
    }
}
