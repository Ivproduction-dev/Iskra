using System.IO;

namespace IskraStudio;

public sealed class ProjectContentStore(ProjectStore projectStore)
{
    public bool TryCreateScene(IskraProject project, string inputName, out IskraScene? scene, out string? error)
    {
        scene = null;
        error = null;
        var name = inputName.Trim();
        if (name.Length == 0)
        {
            var suffix = 2;
            do
            {
                name = $"Сцена {suffix++}";
            }
            while (project.Scenes.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)));
        }
        else if (!IsValidName(name, out error))
        {
            return false;
        }

        if (project.Scenes.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Сцена с таким именем уже существует.";
            return false;
        }

        var created = new IskraScene { Name = name };
        var rootDirectory = projectStore.GetProjectDirectoryFor(project);
        if (Directory.Exists(Path.Combine(rootDirectory, "sounds", SceneDirectoryName(created))) ||
            Directory.Exists(Path.Combine(rootDirectory, "sprites", SceneDirectoryName(created))))
        {
            error = "Папка ресурсов для новой сцены уже существует.";
            return false;
        }

        project.Scenes.Add(created);
        try
        {
            CreateSceneDirectories(project, created);
            projectStore.SaveProject(project);
            scene = created;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            project.Scenes.Remove(created);
            DeleteSceneDirectories(project, created);
            error = "Не удалось создать сцену. Проверьте доступ к папке проекта.";
            return false;
        }
    }

    public bool TryRenameScene(IskraProject project, IskraScene scene, string inputName, out string? error)
    {
        error = null;
        var name = inputName.Trim();
        if (!IsValidName(name, out error))
        {
            return false;
        }

        if (project.Scenes.Any(item => item.Id != scene.Id &&
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Сцена с таким именем уже существует.";
            return false;
        }

        if (string.Equals(scene.Name, name, StringComparison.Ordinal))
        {
            return true;
        }

        var oldName = scene.Name;
        var root = projectStore.GetProjectDirectoryFor(project);
        scene.Name = name;
        try
        {
            RewriteResourceReferences(Path.Combine(root, "code.txt"), oldName, name);
            projectStore.SaveProject(project);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            scene.Name = oldName;
            RewriteResourceReferences(Path.Combine(root, "code.txt"), name, oldName);
            error = "Не удалось переименовать сцену.";
            return false;
        }
    }

    public bool TryDeleteScenes(IskraProject project, IReadOnlyCollection<IskraScene> scenes, out string? error)
    {
        error = null;
        var deleting = project.Scenes.Where(scene => scenes.Any(item => item.Id == scene.Id)).ToList();
        if (deleting.Count == 0)
        {
            error = "Не выбраны сцены для удаления.";
            return false;
        }

        var root = projectStore.GetProjectDirectoryFor(project);
        var staging = Path.Combine(root, $".delete-{Guid.NewGuid():N}");
        var moved = new List<(string Original, string Current)>();
        var originalScenes = project.Scenes.ToList();
        try
        {
            foreach (var scene in deleting)
            {
                foreach (var resourceType in new[] { "sounds", "sprites", "scripts" })
                {
                    var source = Path.Combine(root, resourceType, SceneDirectoryName(scene));
                    if (!Directory.Exists(source))
                    {
                        continue;
                    }

                    var destination = Path.Combine(staging, resourceType, SceneDirectoryName(scene));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    Directory.Move(source, destination);
                    moved.Add((source, destination));
                }
            }

        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RestoreMoves(moved);
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
            error = "Не удалось удалить выбранные сцены и их ресурсы.";
            return false;
        }

        project.Scenes.RemoveAll(scene => deleting.Any(item => item.Id == scene.Id));
        try
        {
            projectStore.SaveProject(project);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            project.Scenes = originalScenes;
            RestoreMoves(moved);
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
            error = "Не удалось сохранить изменения после удаления сцен.";
            return false;
        }

        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Сцены удалены, но не удалось очистить временную папку с ресурсами.", exception);
        }
        return true;
    }

    public bool TryCreateObject(IskraProject project, IskraScene scene, string inputName, out IskraObject? created, out string? error)
    {
        created = null;
        error = null;
        var name = inputName.Trim();
        if (!IsValidName(name, out error))
        {
            return false;
        }

        if (scene.Objects.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Объект с таким именем уже существует в этой сцене.";
            return false;
        }

        var item = new IskraObject { Name = name };
        scene.Objects.Add(item);
        try
        {
            File.WriteAllText(GetObjectScriptPath(project, scene, item.Id), DefaultObjectScript());
            projectStore.SaveProject(project);
            created = item;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            scene.Objects.Remove(item);
            TryDeleteFile(GetObjectScriptPath(project, scene, item.Id));
            error = "Не удалось создать объект.";
            return false;
        }
    }

    public bool TryRenameObject(IskraProject project, IskraScene scene, IskraObject item, string inputName, out string? error)
    {
        error = null;
        var name = inputName.Trim();
        if (!IsValidName(name, out error))
        {
            return false;
        }

        if (scene.Objects.Any(other => other.Id != item.Id &&
            string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Объект с таким именем уже существует в этой сцене.";
            return false;
        }

        var previousName = item.Name;
        item.Name = name;
        try
        {
            projectStore.SaveProject(project);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            item.Name = previousName;
            error = "Не удалось переименовать объект.";
            return false;
        }
    }

    public bool TryDeleteObject(IskraProject project, IskraScene scene, IskraObject item, out string? error)
    {
        error = null;
        var root = projectStore.GetProjectDirectoryFor(project);
        var stagedPaths = new List<(string Original, string Current)>();
        var staging = Path.Combine(root, $".delete-{Guid.NewGuid():N}");
        var objectIndex = scene.Objects.IndexOf(item);
        try
        {
            foreach (var resourceType in new[] { "sounds", "sprites" })
            {
                var source = Path.Combine(root, resourceType, SceneDirectoryName(scene), item.Id.ToString("N"));
                if (Directory.Exists(source))
                {
                    var destination = Path.Combine(staging, resourceType, item.Id.ToString("N"));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    Directory.Move(source, destination);
                    stagedPaths.Add((source, destination));
                }
            }

            var scriptSource = Path.Combine(root, "scripts", SceneDirectoryName(scene), item.Id.ToString("N") + ".isk");
            if (File.Exists(scriptSource))
            {
                var scriptDestination = Path.Combine(staging, "scripts", item.Id.ToString("N") + ".isk");
                Directory.CreateDirectory(Path.GetDirectoryName(scriptDestination)!);
                File.Move(scriptSource, scriptDestination);
                stagedPaths.Add((scriptSource, scriptDestination));
            }

        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RestoreMoves(stagedPaths);
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
            error = "Не удалось удалить объект и его ресурсы.";
            return false;
        }

        scene.Objects.Remove(item);
        try
        {
            projectStore.SaveProject(project);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (scene.Objects.All(existing => existing.Id != item.Id))
            {
                scene.Objects.Insert(Math.Clamp(objectIndex, 0, scene.Objects.Count), item);
            }
            RestoreMoves(stagedPaths);
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
            error = "Не удалось сохранить изменения после удаления объекта.";
            return false;
        }

        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Объект удалён, но не удалось очистить временную папку с его ресурсами.", exception);
        }
        return true;
    }

    public string GetObjectScriptPath(IskraProject project, IskraScene scene, Guid objectId)
    {
        var directory = Path.Combine(projectStore.GetProjectDirectoryFor(project), "scripts", SceneDirectoryName(scene));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, objectId.ToString("N") + ".isk");
    }

    public string ReadObjectScript(IskraProject project, IskraScene scene, Guid objectId)
    {
        var path = GetObjectScriptPath(project, scene, objectId);
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }
        return DefaultObjectScript();
    }

    public bool TryWriteObjectScript(IskraProject project, IskraScene scene, Guid objectId, string text, out string? error)
    {
        error = null;
        try
        {
            File.WriteAllText(GetObjectScriptPath(project, scene, objectId), text);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = "Не удалось сохранить скрипт.";
            return false;
        }
    }

    private static string DefaultObjectScript() =>
        "Задать скорость = 0" + Environment.NewLine + "Печать скорость" + Environment.NewLine;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Не удалось удалить файл скрипта.", exception);
        }
    }

    private static string SceneDirectoryName(IskraScene scene) => scene.Id.ToString("N");

    private void CreateSceneDirectories(IskraProject project, IskraScene scene)
    {
        var root = projectStore.GetProjectDirectoryFor(project);
        Directory.CreateDirectory(Path.Combine(root, "sounds", SceneDirectoryName(scene)));
        Directory.CreateDirectory(Path.Combine(root, "sprites", SceneDirectoryName(scene)));
        File.WriteAllText(GetObjectScriptPath(project, scene, IskraObject.BackgroundId), DefaultObjectScript());
    }

    private void DeleteSceneDirectories(IskraProject project, IskraScene scene)
    {
        var root = projectStore.GetProjectDirectoryFor(project);
        foreach (var resourceType in new[] { "sounds", "sprites", "scripts" })
        {
            var path = Path.Combine(root, resourceType, SceneDirectoryName(scene));
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    private static void RestoreMoves(IEnumerable<(string Original, string Current)> moved)
    {
        foreach (var (original, current) in moved.Reverse())
        {
            if (Directory.Exists(current) && !Directory.Exists(original))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                Directory.Move(current, original);
            }
            else if (File.Exists(current) && !File.Exists(original) && !Directory.Exists(original))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                File.Move(current, original);
            }
        }
    }

    private static void RewriteResourceReferences(string codePath, string oldSceneName, string newSceneName)
    {
        if (!File.Exists(codePath))
        {
            return;
        }

        var code = File.ReadAllText(codePath);
        var escapedOldName = System.Text.RegularExpressions.Regex.Escape(oldSceneName);
        var pattern = $"(?<quote>[\\\"'])((?:sounds|sprites)/){escapedOldName}(?=/)";
        var updated = System.Text.RegularExpressions.Regex.Replace(
            code,
            pattern,
            match => $"{match.Groups["quote"].Value}{match.Groups[2].Value}{newSceneName}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!string.Equals(code, updated, StringComparison.Ordinal))
        {
            File.WriteAllText(codePath, updated);
        }
    }

    private static bool IsValidName(string name, out string? error)
    {
        error = null;
        if (name.Length == 0)
        {
            error = "Введите имя.";
            return false;
        }

        if (name.Length > 50 || name is "." or ".." || name.EndsWith('.') ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "Имя слишком длинное или содержит недопустимые символы.";
            return false;
        }

        return true;
    }
}
