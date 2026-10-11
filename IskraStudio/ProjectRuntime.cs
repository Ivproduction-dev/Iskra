using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IskraStudio;

internal enum RuntimeDiagnosticSeverity
{
    Warning,
    Error
}

internal sealed record RuntimeDiagnostic(
    RuntimeDiagnosticSeverity Severity,
    string Message,
    string SceneName,
    string ObjectName,
    int Line,
    Guid SceneId,
    Guid ObjectId);

internal sealed record RuntimeCommand(string Kind, string Argument, int Line);
internal sealed record CompiledObject(Guid Id, string Name, string ScriptPath, List<RuntimeCommand> Commands);
internal sealed record CompiledScene(Guid Id, string Name, List<CompiledObject> Objects);
internal sealed record CompiledProject(int Version, string Name, ProjectOrientation Orientation, List<CompiledScene> Scenes);
internal sealed record RuntimeCacheEntry(CompiledProject Project, List<RuntimeDiagnostic> Diagnostics);

internal sealed record RuntimeCompileResult(
    CompiledProject? Project,
    IReadOnlyList<RuntimeDiagnostic> Diagnostics,
    string? CachePath)
{
    public bool HasErrors => Diagnostics.Any(item => item.Severity == RuntimeDiagnosticSeverity.Error);
    public bool HasWarnings => Diagnostics.Any(item => item.Severity == RuntimeDiagnosticSeverity.Warning);
}

internal sealed class ProjectRuntime(ProjectStore projectStore, ProjectContentStore contentStore)
{
    private const int CacheVersion = 2;
    private static readonly Regex CommandPattern = new(
        "^(?<name>Задать|Присвоить)\\s+изображение\\s*\\.\\s*(?<arg>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ImageStepPattern = new(
        "^Изменить\\s+изображение\\s*\\.\\s*(?<arg>перед|назад)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AudioPattern = new(
        "^Воспроизвести\\s+звук(?<wait>\\s+ждать)?\\s*\\.\\s*(?<arg>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PlacePattern = new(
        "^Поместить\\s+(?<x>-?\\d+)\\s+(?<y>-?\\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public RuntimeCompileResult Compile(IskraProject project, bool createCache = true, IProgress<string>? progress = null)
    {
        var diagnostics = new List<RuntimeDiagnostic>();
        string? cachePath = null;
        if (createCache)
        {
            try
            {
                cachePath = GetCachePath(project);
                if (File.Exists(cachePath))
                {
                    var cached = JsonSerializer.Deserialize<RuntimeCacheEntry>(File.ReadAllText(cachePath));
                    if (cached is not null && cached.Project.Version == CacheVersion)
                    {
                        progress?.Report("Проверка завершена. Используем готовый кэш…");
                        return new RuntimeCompileResult(cached.Project, cached.Diagnostics, cachePath);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                AppLog.Write("Не удалось прочитать кэш кода; он будет собран заново.", exception);
            }
        }
        var compiledScenes = new List<CompiledScene>();
        foreach (var scene in project.Scenes)
        {
            var compiledObjects = new List<CompiledObject>();
            var objects = new List<IskraObject>
            {
                new() { Id = IskraObject.BackgroundId, Name = "Фон" }
            };
            objects.AddRange(scene.Objects);
            foreach (var item in objects)
            {
                var scriptPath = contentStore.GetObjectScriptPath(project, scene, item.Id);
                var script = File.Exists(scriptPath) ? File.ReadAllText(scriptPath) : "";
                var commands = CompileScript(project, scene, item, script, objects, diagnostics);
                compiledObjects.Add(new CompiledObject(item.Id, item.Name, scriptPath, commands));
            }
            compiledScenes.Add(new CompiledScene(scene.Id, scene.Name, compiledObjects));
        }

        if (compiledScenes.Count == 0)
        {
            diagnostics.Add(new RuntimeDiagnostic(RuntimeDiagnosticSeverity.Error,
                "В проекте нет сцен для запуска.", "Проект", "", 0, Guid.Empty, Guid.Empty));
            return new RuntimeCompileResult(null, diagnostics, null);
        }

        if (diagnostics.Any(item => item.Severity == RuntimeDiagnosticSeverity.Error))
        {
            return new RuntimeCompileResult(null, diagnostics, null);
        }

        var compiled = new CompiledProject(CacheVersion, project.Name, project.Orientation, compiledScenes);
        if (!createCache)
        {
            return new RuntimeCompileResult(compiled, diagnostics, null);
        }

        try
        {
            progress?.Report("Создаём кэш кода…");
            cachePath ??= GetCachePath(project);
            var cacheDirectory = Path.GetDirectoryName(cachePath)!;
            Directory.CreateDirectory(cacheDirectory);
            var temporaryPath = cachePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new RuntimeCacheEntry(compiled, diagnostics)));
            File.Move(temporaryPath, cachePath, overwrite: true);
            return new RuntimeCompileResult(compiled, diagnostics, cachePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new RuntimeDiagnostic(RuntimeDiagnosticSeverity.Error,
                "Не удалось создать кэш кода проекта.", "Проект", "", 0, Guid.Empty, Guid.Empty));
            AppLog.Write("Не удалось создать кэш кода проекта.", exception);
            return new RuntimeCompileResult(null, diagnostics, null);
        }
    }

    public bool ClearCache(IskraProject project, out string? error)
    {
        error = null;
        try
        {
            var directory = GetProjectCacheDirectory(project);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Не удалось очистить кэш кода проекта.", exception);
            error = "Не удалось очистить кэш кода.";
            return false;
        }
    }

    public static async Task RunSceneAsync(
        CompiledProject project,
        IskraScene scene,
        IskraProject sourceProject,
        ProjectContentStore contentStore,
        FrameworkElement sceneSurface,
        Action<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        var compiledScene = project.Scenes.FirstOrDefault(item => item.Id == scene.Id);
        if (compiledScene is null)
        {
            return;
        }

        var imageLists = new Dictionary<Guid, List<string>>();
        foreach (var item in compiledScene.Objects)
        {
            var resourceDirectory = contentStore.GetObjectResourceDirectory(sourceProject, scene, item.Id, "sprites");
            imageLists[item.Id] = ProjectContentStore.ListResourceFiles(resourceDirectory, ProjectContentStore.ImageExtensions);
        }

        var renderTargets = new List<FrameworkElement>();
        var backgroundImage = imageLists.GetValueOrDefault(IskraObject.BackgroundId)?.FirstOrDefault();
        if (backgroundImage is not null && sceneSurface is Panel panel)
        {
            var background = new Image
            {
                Source = LoadBitmap(backgroundImage),
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false
            };
            panel.Children.Add(background);
            Panel.SetZIndex(background, 1);
            renderTargets.Add(background);
        }

        var perObjectImage = new Dictionary<Guid, Image>();
        if (sceneSurface is Panel objectPanel)
        {
            foreach (var item in compiledScene.Objects.Where(item => item.Id != IskraObject.BackgroundId))
            {
                var images = imageLists[item.Id];
                if (images.Count == 0)
                {
                    continue;
                }
                var image = new Image
                {
                    Source = LoadBitmap(images[0]),
                    Stretch = Stretch.None,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    RenderTransform = new TranslateTransform(),
                    IsHitTestVisible = false
                };
                objectPanel.Children.Add(image);
                Panel.SetZIndex(image, 2);
                perObjectImage[item.Id] = image;
                renderTargets.Add(image);
            }
        }

        var variables = compiledScene.Objects.ToDictionary(item => item.Id, _ => new Dictionary<string, string>(StringComparer.Ordinal));
        var players = new List<MediaPlayer>();
        try
        {
            var tasks = compiledScene.Objects.Select(item => RunObjectAsync(item)).ToArray();
            await Task.WhenAll(tasks);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var player in players)
            {
                player.Stop();
                player.Close();
            }
        }

        async Task RunObjectAsync(CompiledObject item)
        {
            var spriteIndex = 0;
            foreach (var command in item.Commands)
            {
                switch (command.Kind)
                {
                    case "set-image":
                        if (TryResolveSprite(item, command.Argument, out var imagePath))
                        {
                            if (item.Id == IskraObject.BackgroundId && sceneSurface is Panel backgroundPanel)
                            {
                                var layer = backgroundPanel.Children.OfType<Image>().FirstOrDefault();
                                if (layer is not null) layer.Source = LoadBitmap(imagePath);
                            }
                            else if (perObjectImage.GetValueOrDefault(item.Id) is { } sprite)
                            {
                                sprite.Source = LoadBitmap(imagePath);
                                var sourceList = imageLists[item.Id];
                                spriteIndex = sourceList.IndexOf(imagePath);
                            }
                        }
                        break;
                    case "step-image":
                        if (imageLists.GetValueOrDefault(item.Id) is { Count: > 0 } images && perObjectImage.GetValueOrDefault(item.Id) is { } cyclingSprite)
                        {
                            spriteIndex = (spriteIndex + (command.Argument == "назад" ? -1 : 1) + images.Count) % images.Count;
                            cyclingSprite.Source = LoadBitmap(images[spriteIndex]);
                        }
                        break;
                    case "place":
                        var coordinates = command.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (coordinates.Length == 2 && int.TryParse(coordinates[0], out var x) && int.TryParse(coordinates[1], out var y))
                        {
                            if (perObjectImage.GetValueOrDefault(item.Id)?.RenderTransform is TranslateTransform translation)
                            {
                                translation.X = x;
                                translation.Y = y;
                            }
                        }
                        break;
                    case "play-sound":
                    case "play-sound-wait":
                        if (TryResolveSound(command.Argument, item, compiledScene, sourceProject, contentStore, variables, out var soundPath))
                        {
                            var player = new MediaPlayer();
                            players.Add(player);
                            if (command.Kind == "play-sound-wait")
                            {
                                var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                                EventHandler handler = (_, _) => ended.TrySetResult();
                                player.MediaEnded += handler;
                                player.MediaFailed += (_, _) => ended.TrySetResult();
                                player.Open(new Uri(soundPath, UriKind.Absolute));
                                player.Play();
                                await ended.Task.WaitAsync(cancellationToken);
                                player.MediaEnded -= handler;
                            }
                            else
                            {
                                player.Open(new Uri(soundPath, UriKind.Absolute));
                                player.Play();
                            }
                        }
                        break;
                    case "set-value":
                        variables[item.Id][command.Argument[..command.Argument.IndexOf('=')].Trim()] = ResolveValue(command.Argument[(command.Argument.IndexOf('=') + 1)..].Trim(), variables[item.Id]);
                        break;
                    case "change-value":
                        var changeParts = command.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (changeParts.Length == 2 && long.TryParse(changeParts[1], out var change) &&
                            long.TryParse(variables[item.Id].GetValueOrDefault(changeParts[0]), out var current))
                        {
                            variables[item.Id][changeParts[0]] = (current + change).ToString();
                        }
                        break;
                    case "print":
                        output?.Invoke(ResolveValue(command.Argument, variables[item.Id]));
                        break;
                    case "show":
                        break;
                }
            }
        }

        bool TryResolveSprite(CompiledObject item, string reference, out string path)
        {
            var objectId = item.Id;
            var fileName = reference;
            var dot = reference.IndexOf('.');
            if (dot > 0)
            {
                var owner = compiledScene.Objects.FirstOrDefault(value =>
                    string.Equals(value.Name, reference[..dot], StringComparison.OrdinalIgnoreCase));
                if (owner is not null)
                {
                    objectId = owner.Id;
                    fileName = reference[(dot + 1)..];
                }
            }
            var directory = contentStore.GetObjectResourceDirectory(sourceProject, scene, objectId, "sprites");
            path = ProjectContentStore.ListResourceFiles(directory, ProjectContentStore.ImageExtensions)
                .FirstOrDefault(value => string.Equals(Path.GetFileName(value), fileName, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            return path.Length > 0;
        }
    }

    private List<RuntimeCommand> CompileScript(
        IskraProject project,
        IskraScene scene,
        IskraObject item,
        string source,
        IReadOnlyList<IskraObject> objects,
        List<RuntimeDiagnostic> diagnostics)
    {
        var commands = new List<RuntimeCommand>();
        var knownValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var lineNumber = 0;
        foreach (var rawLine in source.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;

            var imageMatch = CommandPattern.Match(line);
            if (imageMatch.Success)
            {
                var argument = imageMatch.Groups["arg"].Value.Trim();
                var path = FindResource(project, scene, item.Id, "sprites", argument, objects);
                if (path is null)
                    Add(RuntimeDiagnosticSeverity.Error, $"Изображение «{argument}» не найдено.");
                else
                    commands.Add(new RuntimeCommand("set-image", argument, lineNumber));
                continue;
            }

            var stepMatch = ImageStepPattern.Match(line);
            if (stepMatch.Success)
            {
                var spriteDirectory = contentStore.GetObjectResourceDirectory(project, scene, item.Id, "sprites");
                if (ProjectContentStore.ListResourceFiles(spriteDirectory, ProjectContentStore.ImageExtensions).Count == 0)
                    Add(RuntimeDiagnosticSeverity.Warning, "У объекта пока нет изображений для переключения.");
                else
                    commands.Add(new RuntimeCommand("step-image", stepMatch.Groups["arg"].Value.ToLowerInvariant(), lineNumber));
                continue;
            }

            var placeMatch = PlacePattern.Match(line);
            if (placeMatch.Success)
            {
                if (int.TryParse(placeMatch.Groups["x"].Value, out _) && int.TryParse(placeMatch.Groups["y"].Value, out _))
                {
                    commands.Add(new RuntimeCommand("place", $"{placeMatch.Groups["x"].Value} {placeMatch.Groups["y"].Value}", lineNumber));
                }
                else
                {
                    Add(RuntimeDiagnosticSeverity.Error, "Координаты «Поместить» должны быть целыми числами в допустимом диапазоне.");
                }
                continue;
            }

            var audioMatch = AudioPattern.Match(line);
            if (audioMatch.Success)
            {
                var argument = audioMatch.Groups["arg"].Value.Trim();
                var effectiveArgument = knownValues.TryGetValue(argument, out var known) ? known : argument;
                if (!LooksLikeDynamicValue(argument, knownValues) &&
                    FindResource(project, scene, item.Id, "sounds", effectiveArgument, objects) is null)
                    Add(RuntimeDiagnosticSeverity.Error, $"Звуковой файл «{effectiveArgument}» не найден.");
                commands.Add(new RuntimeCommand(audioMatch.Groups["wait"].Success ? "play-sound-wait" : "play-sound", argument, lineNumber));
                continue;
            }

            var assignment = Regex.Match(line, "^(?:Задать|Присвоить)\\s+(?<name>[^\\s=]+)\\s*=\\s*(?<value>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (assignment.Success)
            {
                var name = assignment.Groups["name"].Value;
                var value = assignment.Groups["value"].Value.Trim();
                if (value.Length == 0)
                {
                    Add(RuntimeDiagnosticSeverity.Error, "После знака = нужно значение.");
                    continue;
                }
                if (long.TryParse(value, out _) || IsQuoted(value)) knownValues[name] = value.Trim('"');
                else if (knownValues.ContainsKey(value)) knownValues[name] = knownValues[value];
                else knownValues.Remove(name);
                commands.Add(new RuntimeCommand("set-value", $"{name} = {value}", lineNumber));
                continue;
            }

            var change = Regex.Match(line, "^Изменить\\s+(?<name>[^\\s]+)\\s+(?<amount>-?\\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (change.Success)
            {
                commands.Add(new RuntimeCommand("change-value", $"{change.Groups["name"].Value} {change.Groups["amount"].Value}", lineNumber));
                continue;
            }

            if (line.StartsWith("Печать", StringComparison.OrdinalIgnoreCase))
            {
                var expression = line["Печать".Length..].Trim();
                if (expression.Length == 0) Add(RuntimeDiagnosticSeverity.Error, "После команды «Печать» нужно значение.");
                else commands.Add(new RuntimeCommand("print", expression, lineNumber));
                continue;
            }

            var show = Regex.Match(line, "^Показать\\s+(?<name>[^\\s]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (show.Success)
            {
                var objectName = show.Groups["name"].Value;
                if (!objects.Any(candidate => string.Equals(candidate.Name, objectName, StringComparison.OrdinalIgnoreCase)))
                    Add(RuntimeDiagnosticSeverity.Warning, $"Объект «{objectName}» не найден; команда пропущена.");
                else
                    commands.Add(new RuntimeCommand("show", objectName, lineNumber));
                continue;
            }

            Add(RuntimeDiagnosticSeverity.Error, $"Неизвестная или пока неподдерживаемая команда «{line.Split(' ', 2)[0]}».");
        }

        return commands;

        void Add(RuntimeDiagnosticSeverity severity, string message) => diagnostics.Add(
            new RuntimeDiagnostic(severity, message, scene.Name, item.Name, lineNumber, scene.Id, item.Id));
    }

    private string? FindResource(IskraProject project, IskraScene scene, Guid ownerId, string resourceType,
        string reference, IReadOnlyList<IskraObject> objects)
    {
        var fileName = reference;
        var dot = reference.IndexOf('.');
        if (dot > 0)
        {
            var owner = objects.FirstOrDefault(value =>
                string.Equals(value.Name, reference[..dot], StringComparison.OrdinalIgnoreCase));
            if (owner is not null)
            {
                ownerId = owner.Id;
                fileName = reference[(dot + 1)..];
            }
        }
        var extensions = resourceType == "sprites" ? ProjectContentStore.ImageExtensions : ProjectContentStore.AudioExtensions;
        var directory = contentStore.GetObjectResourceDirectory(project, scene, ownerId, resourceType);
        return ProjectContentStore.ListResourceFiles(directory, extensions).FirstOrDefault(path =>
            string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private string GetProjectCacheDirectory(IskraProject project)
    {
        var root = projectStore.GetProjectDirectoryFor(project);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root))))[..24];
        return Path.Combine(AppPaths.DataDirectory, "CodeCache", key);
    }

    private string GetCachePath(IskraProject project) =>
        Path.Combine(GetProjectCacheDirectory(project), GetFingerprint(project) + ".json");

    private string GetFingerprint(IskraProject project)
    {
        var content = new StringBuilder().Append(CacheVersion).Append('|').Append(project.Orientation);
        foreach (var scene in project.Scenes)
        {
            content.Append('|').Append(scene.Id).Append(':').Append(scene.Name);
            var scriptIds = scene.Objects.Select(item => item.Id).Append(IskraObject.BackgroundId);
            foreach (var objectId in scriptIds)
            {
                var scriptPath = contentStore.GetObjectScriptPath(project, scene, objectId);
                content.Append('|').Append(objectId).Append(':');
                if (File.Exists(scriptPath)) content.Append(File.ReadAllText(scriptPath));
                foreach (var resourceType in new[] { "sprites", "sounds" })
                {
                    var directory = contentStore.GetObjectResourceDirectory(project, scene, objectId, resourceType);
                    if (!Directory.Exists(directory)) continue;
                    foreach (var resource in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    {
                        var info = new FileInfo(resource);
                        content.Append('|').Append(Path.GetFileName(resource)).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
                    }
                }
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString()))).ToLowerInvariant();
    }

    private static bool LooksLikeDynamicValue(string value, IReadOnlyDictionary<string, string> knownValues) =>
        knownValues.ContainsKey(value) || long.TryParse(value, out _) || IsQuoted(value);

    private static bool IsQuoted(string value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"';

    private static string ResolveValue(string source, IReadOnlyDictionary<string, string> variables)
    {
        var parts = source.Split("..", StringSplitOptions.None);
        var output = new StringBuilder();
        foreach (var part in parts)
        {
            var value = part.Trim();
            if (IsQuoted(value)) output.Append(value[1..^1]);
            else if (long.TryParse(value, out _)) output.Append(value);
            else output.Append(variables.GetValueOrDefault(value, value));
        }
        return output.ToString();
    }

    private static bool TryResolveSound(string source, CompiledObject item, CompiledScene scene, IskraProject project,
        ProjectContentStore contentStore, IReadOnlyDictionary<Guid, Dictionary<string, string>> variables, out string path)
    {
        var reference = variables[item.Id].TryGetValue(source, out var value) ? value : source;
        var ownerId = item.Id;
        var dot = reference.IndexOf('.');
        if (dot > 0)
        {
            var owner = scene.Objects.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, reference[..dot], StringComparison.OrdinalIgnoreCase));
            if (owner is not null)
            {
                ownerId = owner.Id;
                reference = reference[(dot + 1)..];
            }
        }
        var directory = contentStore.GetObjectResourceDirectory(project, project.Scenes.First(value => value.Id == scene.Id), ownerId, "sounds");
        path = ProjectContentStore.ListResourceFiles(directory, ProjectContentStore.AudioExtensions)
            .FirstOrDefault(candidate => string.Equals(Path.GetFileName(candidate), reference, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return path.Length > 0;
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
