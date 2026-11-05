namespace Iskra.Android;

internal static class MobileExport
{
    public static string PrepareCacheFile(string projectName)
    {
        var directory = FileSystem.CacheDirectory;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.iskp"))
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(1))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MobileLog.Write("Не удалось очистить старые архивы экспорта.", exception);
        }
        return Path.Combine(directory, $"{projectName}.iskp");
    }
}
