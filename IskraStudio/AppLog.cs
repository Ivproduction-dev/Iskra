using System.Diagnostics;
using System.IO;
using System.Text;

namespace IskraStudio;

public static class AppLog
{
    public static string FilePath { get; } = Path.Combine(AppPaths.DataDirectory, "log.txt");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var content = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("u"))
                .Append("  ")
                .AppendLine(message);
            if (exception is not null)
            {
                content.AppendLine(exception.ToString());
            }
            content.AppendLine();
            File.AppendAllText(FilePath, content.ToString(), Encoding.UTF8);
        }
        catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(logException);
        }
    }

    public static void Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        if (!File.Exists(FilePath))
        {
            File.WriteAllText(FilePath, "Журнал ошибок Искры\n", Encoding.UTF8);
        }

        Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = true });
    }
}
