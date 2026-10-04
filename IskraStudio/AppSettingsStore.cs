using System.IO;
using System.Text.Json;

namespace IskraStudio;

public sealed class IskraAppSettings
{
    public int Theme { get; set; }
    public bool UseCustomWindow { get; set; }
}

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path = Path.Combine(AppPaths.DataDirectory, "settings.json");

    public IskraAppSettings Load()
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<IskraAppSettings>(File.ReadAllText(path)) ?? new();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Write("Не удалось загрузить настройки приложения.", exception);
        }

        return new();
    }

    public void Save(IskraAppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
