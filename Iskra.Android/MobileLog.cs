using System.Diagnostics;
using System.Text;

namespace Iskra.Android;

internal static class MobileLog
{
    private const long MaximumBytes = 1_000_000;
    private static readonly object Sync = new();
    private static readonly string LogPath = Path.Combine(FileSystem.AppDataDirectory, "IskraStudio", "log.txt");

    public static void Write(string message, Exception? exception = null)
    {        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaximumBytes)
                {
                    File.Move(LogPath, LogPath + ".old", overwrite: true);
                }
                var entry = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("u"))
                    .Append("  ")
                    .AppendLine(message);
                if (exception is not null)
                {
                    entry.AppendLine(exception.ToString());
                }
                entry.AppendLine();
                File.AppendAllText(LogPath, entry.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(logException);
        }
    }

    public static string Read()
    {
        try
        {
            return File.Exists(LogPath) ? File.ReadAllText(LogPath) : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
