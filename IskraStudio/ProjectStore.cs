using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualBasic.FileIO;

namespace IskraStudio;

public enum ProjectOrientation
{
    Horizontal,
    Vertical
}

public sealed class IskraProject
{
    public string Name { get; set; } = string.Empty;
    public ProjectOrientation Orientation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastOpenedAt { get; set; }
    public List<IskraScene> Scenes { get; set; } = [];

    [JsonIgnore]
    public string OrientationLabel => Orientation == ProjectOrientation.Horizontal
        ? "Горизонтальная сцена"
        : "Вертикальная сцена";
}

public sealed class IskraScene
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<IskraObject> Objects { get; set; } = [];
}

public sealed class IskraObject
{
    public static readonly Guid BackgroundId = new("A7B3C9D1-E5F2-4A8B-9C0D-1234567890AB");

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
}

public sealed class ProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static JsonSerializerOptions SerializerOptionsForArchive => JsonOptions;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly string projectsDirectory = Path.Combine(AppPaths.DataDirectory, "Projects");

    public IReadOnlyList<IskraProject> LoadAll(out IReadOnlyList<string> skippedProjects)
    {
        Directory.CreateDirectory(projectsDirectory);
        var projects = new List<IskraProject>();
        var skipped = new List<string>();

        foreach (var projectDirectory in Directory.EnumerateDirectories(projectsDirectory))
        {
            if (Path.GetFileName(projectDirectory).StartsWith('.'))
            {
                continue;
            }

            var metadataPath = Path.Combine(projectDirectory, "project.iskra.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                var project = JsonSerializer.Deserialize<IskraProject>(File.ReadAllText(metadataPath), JsonOptions);
                if (project is null || !IsValidProjectName(project.Name, out _))
                {
                    skipped.Add(Path.GetFileName(projectDirectory));
                    continue;
                }

                project.Scenes ??= [];
                if (project.Scenes.Count == 0)
                {
                    project.Scenes.Add(new IskraScene { Name = "Сцена" });
                }

                EnsureProjectFiles(project);

                projects.Add(project);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                skipped.Add(Path.GetFileName(projectDirectory));
            }
        }

        skippedProjects = skipped;
        return projects.OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public bool TryCreate(
        string inputName,
        ProjectOrientation orientation,
        IReadOnlyCollection<IskraProject> existingProjects,
        out IskraProject? project,
        out string? error)
    {
        project = null;
        error = null;
        var name = inputName.Trim();

        if (name.Length == 0)
        {
            error = "Введите имя проекта.";
            return false;
        }

        if (name.Length > 50 || name is "." or ".." || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "Имя слишком длинное или содержит недопустимые символы.";
            return false;
        }

        var baseName = Path.GetFileNameWithoutExtension(name);
        if (ReservedNames.Contains(baseName))
        {
            error = "Это имя зарезервировано Windows. Выберите другое.";
            return false;
        }

        if (existingProjects.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Проект с таким именем уже существует.";
            return false;
        }

        var directory = Path.Combine(projectsDirectory, name);
        if (Directory.Exists(directory) || File.Exists(directory))
        {
            error = "Папка с таким именем уже существует. Выберите другое имя.";
            return false;
        }

        project = new IskraProject
        {
            Name = name,
            Orientation = orientation,
            CreatedAt = DateTimeOffset.UtcNow,
            Scenes = [new IskraScene { Name = "Сцена" }]
        };

        try
        {
            Directory.CreateDirectory(directory);
            EnsureProjectFiles(project);
            Save(project);
            return true;
        }
        catch (IOException)
        {
            project = null;
            error = "Не удалось создать проект. Проверьте доступ к папке проектов.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            project = null;
            error = "Нет доступа к папке проектов.";
            return false;
        }
        catch (InvalidDataException)
        {
            project = null;
            error = "Данные нового проекта некорректны.";
            return false;
        }
    }

    public bool TryUpdate(
        IskraProject currentProject,
        string inputName,
        ProjectOrientation orientation,
        IReadOnlyCollection<IskraProject> existingProjects,
        out IskraProject? updatedProject,
        out string? error)
    {
        updatedProject = null;
        error = null;
        var name = inputName.Trim();

        if (!IsValidProjectName(name, out error))
        {
            return false;
        }

        if (existingProjects.Any(item => !ReferenceEquals(item, currentProject) &&
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Проект с таким именем уже существует.";
            return false;
        }

        string oldDirectory;
        string newDirectory;
        try
        {
            oldDirectory = GetProjectDirectory(currentProject.Name);
            newDirectory = GetProjectDirectory(name);
        }
        catch (ArgumentException)
        {
            error = "Не удалось найти папку этого проекта.";
            return false;
        }

        if (!Directory.Exists(oldDirectory))
        {
            error = "Папка проекта не найдена.";
            return false;
        }

        var nameChanged = !string.Equals(currentProject.Name, name, StringComparison.Ordinal);
        if (nameChanged && !string.Equals(currentProject.Name, name, StringComparison.OrdinalIgnoreCase) &&
            (Directory.Exists(newDirectory) || File.Exists(newDirectory)))
        {
            error = "Папка с таким именем уже существует. Выберите другое имя.";
            return false;
        }

        updatedProject = new IskraProject
        {
            Name = name,
            Orientation = orientation,
            CreatedAt = currentProject.CreatedAt,
            LastOpenedAt = currentProject.LastOpenedAt,
            Scenes = currentProject.Scenes
        };

        var movedDirectory = false;
        try
        {
            if (nameChanged && !string.Equals(currentProject.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(oldDirectory, newDirectory);
                movedDirectory = true;
            }

            Save(updatedProject);
            return true;
        }
        catch (IOException)
        {
            RollBackDirectoryMove(oldDirectory, newDirectory, movedDirectory);
            updatedProject = null;
            error = "Не удалось сохранить изменения проекта.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            RollBackDirectoryMove(oldDirectory, newDirectory, movedDirectory);
            updatedProject = null;
            error = "Нет доступа к папке проекта.";
            return false;
        }
    }

    public bool TryDelete(IskraProject project, out string? error)
    {
        error = null;
        string directory;
        try
        {
            directory = GetProjectDirectory(project.Name);
        }
        catch (ArgumentException)
        {
            error = "Не удалось найти папку этого проекта.";
            return false;
        }

        if (!Directory.Exists(directory))
        {
            error = "Папка проекта не найдена.";
            return false;
        }

        try
        {
            FileSystem.DeleteDirectory(directory, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return true;
        }
        catch (IOException)
        {
            error = "Не удалось переместить проект в корзину.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            error = "Нет доступа к папке проекта.";
            return false;
        }
    }

    internal static bool IsValidProjectName(string name, out string? error)
    {
        error = null;
        if (name.Length == 0)
        {
            error = "Введите имя проекта.";
            return false;
        }

        if (name.Length > 50 || name is "." or ".." || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "Имя слишком длинное или содержит недопустимые символы.";
            return false;
        }

        var baseName = Path.GetFileNameWithoutExtension(name);
        if (ReservedNames.Contains(baseName))
        {
            error = "Это имя зарезервировано Windows. Выберите другое.";
            return false;
        }

        return true;
    }

    private string GetProjectDirectory(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Недопустимое имя папки проекта.", nameof(name));
        }

        var root = Path.GetFullPath(projectsDirectory);
        var directory = Path.GetFullPath(Path.Combine(root, name));
        if (!string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Папка проекта должна быть внутри списка проектов.", nameof(name));
        }

        return directory;
    }

    internal void SweepLeftoverOperations()
    {
        try
        {
            Directory.CreateDirectory(projectsDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Не удалось открыть папку проектов при зачистке.", exception);
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(projectsDirectory))
        {
            var name = Path.GetFileName(directory);
            try
            {
                if (name.StartsWith(".import-", StringComparison.Ordinal))
                {
                    Directory.Delete(directory, recursive: true);
                }
                else if (name.StartsWith(".delete-", StringComparison.Ordinal))
                {
                    ProjectContentStore.RestoreStagedDeletions(directory);
                }
                else if (!name.StartsWith('.'))
                {
                    foreach (var staging in Directory.EnumerateDirectories(directory, ".delete-*"))
                    {
                        ProjectContentStore.RestoreStagedDeletions(staging);
                    }
                    foreach (var temporary in Directory.EnumerateFiles(directory, "*.tmp"))
                    {
                        try
                        {
                            File.Delete(temporary);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            AppLog.Write($"Не удалось удалить временный файл «{Path.GetFileName(temporary)}».", exception);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Write($"Не удалось зачистить «{name}».", exception);
            }
        }
    }

    internal string GetProjectDirectoryFor(IskraProject project) => GetProjectDirectory(project.Name);

    internal void SaveProject(IskraProject project) => Save(project);

    internal string ProjectsRoot => projectsDirectory;

    internal static void SaveProjectToDirectory(IskraProject project, string directory)
    {
        Directory.CreateDirectory(directory);
        var metadataPath = Path.Combine(directory, "project.iskra.json");
        var tempPath = metadataPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(project, JsonOptions));
        File.Move(tempPath, metadataPath, overwrite: true);
    }

    private void EnsureProjectFiles(IskraProject project)
    {
        var directory = GetProjectDirectory(project.Name);
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "sounds"));
        Directory.CreateDirectory(Path.Combine(directory, "sprites"));
        Directory.CreateDirectory(Path.Combine(directory, "scripts"));
        var codePath = Path.Combine(directory, "code.txt");
        if (!File.Exists(codePath))
        {
            File.WriteAllText(codePath, string.Empty);
        }

        foreach (var scene in project.Scenes)
        {
            if (!IsValidProjectName(scene.Name, out _))
            {
                throw new InvalidDataException("В данных проекта найдено недопустимое имя сцены.");
            }

            scene.Objects ??= [];
            MigrateLegacySceneDirectories(directory, scene);
            Directory.CreateDirectory(Path.Combine(directory, "sounds", scene.Id.ToString("N")));
            Directory.CreateDirectory(Path.Combine(directory, "sprites", scene.Id.ToString("N")));
        }

        Save(project);
    }

    internal static void MigrateLegacySceneDirectories(string directory, IskraScene scene)
    {
        var currentName = scene.Id.ToString("N");
        foreach (var resourceType in new[] { "sounds", "sprites" })
        {
            var legacy = Path.Combine(directory, resourceType, scene.Name);
            var current = Path.Combine(directory, resourceType, currentName);
            if (string.Equals(legacy, current, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!Directory.Exists(legacy) || Directory.Exists(current))
            {
                continue;
            }
            Directory.Move(legacy, current);
        }
    }

    private static void RollBackDirectoryMove(string oldDirectory, string newDirectory, bool movedDirectory)
    {
        if (movedDirectory && Directory.Exists(newDirectory) && !Directory.Exists(oldDirectory))
        {
            Directory.Move(newDirectory, oldDirectory);
        }
    }

    public void MarkOpened(IskraProject project)
    {
        project.LastOpenedAt = DateTimeOffset.UtcNow;
        Save(project);
    }

    private void Save(IskraProject project)
    {
        SaveProjectToDirectory(project, GetProjectDirectory(project.Name));
    }
}
