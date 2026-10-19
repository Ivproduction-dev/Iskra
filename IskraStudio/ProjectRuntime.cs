using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

internal sealed record RuntimeCommand(string Kind, string Argument, int Line, int Target = -1, Guid? TargetObjectId = null, Guid? ResourceOwnerId = null, string? Condition = null);
internal sealed record RuntimeDebugVariable(string SceneName, string ObjectName, string Name, string Value);
internal static class SpriteSizeSyntax
{
    public const double MaximumPercent = 100_000d;

    public static bool IsValidSetSize(double amount) =>
        double.IsFinite(amount) && amount is >= 0 and <= MaximumPercent;

    public static double ApplyChange(double currentPercent, double change) =>
        Math.Clamp(currentPercent + change, 0d, MaximumPercent);
}

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

internal sealed partial class ProjectRuntime(ProjectStore projectStore, ProjectContentStore contentStore)
{
    internal ProjectContentStore ContentStore => contentStore;

    private const int CacheVersion = 16;
    private static readonly Regex CommandPattern = new(
        "^(?<name>Задать|Присвоить)\\s+изображение\\s*\\.\\s*(?<arg>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SetTransparencyPattern = new(
        "^Задать\\s+прозрачность\\s*\\.\\s*(?<amount>[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ChangeTransparencyPattern = new(
        "^Изменить\\s+прозрачность\\s*\\.\\s*(?<amount>[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SetSizePattern = new(
        "^Задать\\s+размер\\s*\\.\\s*(?<amount>[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ChangeSizePattern = new(
        "^Изменить\\s+размер\\s*\\.\\s*(?<amount>[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SetBrightnessPattern = new(
        "^Задать\\s+яркость\\s*\\.\\s*(?<amount>[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ChangeBrightnessPattern = new(
        "^Изменить\\s+яркость\\s*\\.\\s*(?<amount>[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AudioPattern = new(
        "^Воспроизвести\\s+звук(?<wait>\\s+ждать)?\\s*\\.\\s*(?<arg>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PlacePattern = new(
        "^Поместить\\s+(?<x>-?\\d+)\\s+(?<y>-?\\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WaitPattern = new(
        "^Ждать\\s+(?<amount>\\d+)\\s+(?<unit>секунд(?:а|ы|у|е|ой|ами|ах)?|минут(?:а|ы|у|е|ой|ами|ах)?|час(?:а|ов|у|ом|ами|ах)?)$",
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

    private List<RuntimeCommand> CompileScript(
        IskraProject project,
        IskraScene scene,
        IskraObject item,
        string source,
        IReadOnlyList<IskraObject> objects,
        List<RuntimeDiagnostic> diagnostics)
    {
        var commands = new List<RuntimeCommand>();
        var flow = new ScriptFlowBuilder(commands);
        var knownValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var lineNumber = 0;
        var eventStarted = false;
        var commandsOutsideEvent = false;
        string? requiredEventEnd = null;
        var requiredEventLine = 0;
        foreach (var sourceLine in ConditionalDirectiveParser.ExpandInline(source))
        {
            lineNumber = sourceLine.LineNumber;
            var line = sourceLine.Text;
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;

            var eventLineKind = ConditionalDirectiveParser.ClassifyEventLine(line, eventStarted);
            if (eventLineKind == ScriptEventLineKind.Start)
            {
                if (requiredEventEnd is not null)
                {
                    AddAtLine(RuntimeDiagnosticSeverity.Error,
                        $"Для блока «{requiredEventEnd}» не найдена команда «Конец».", requiredEventLine);
                    requiredEventEnd = null;
                }
                eventStarted = true;
                knownValues.Clear();
                foreach (var openBlock in flow.OpenBlocks)
                {
                    AddAtLine(RuntimeDiagnosticSeverity.Error,
                        $"Блок «{openBlock.Name}» должен завершиться командой «Конец» до следующего события.", openBlock.Line);
                    flow.End(out _, out _);
                }
                if (ConditionalDirectiveParser.TryParseConditionEvent(line, out var eventCondition))
                {
                    if (!ConditionalDirectiveParser.IsValidCondition(eventCondition))
                    {
                        Add(RuntimeDiagnosticSeverity.Error, "Формат события: «Когда. готово = истина» или «Как.Только готово = истина».");
                    }
                    else
                    {
                        ValidateConditionReferences(eventCondition);
                        commands.Add(new RuntimeCommand("event", "when", lineNumber, Condition: eventCondition));
                    }
                }
                else
                {
                    ConditionalDirectiveParser.TryParseEventDetail(line, out var eventKind, out var eventArgument);
                    commands.Add(new RuntimeCommand("event", eventKind, lineNumber, Condition: eventArgument));
                    if (eventKind is "key" or "every" or "frame")
                    {
                        requiredEventEnd = line;
                        requiredEventLine = lineNumber;
                    }
                }
                continue;
            }

            if (eventLineKind == ScriptEventLineKind.Unsupported)
            {
                if (ScriptCommandSyntax.TryParseKeyboardEvent(line, out var keyToken, out var keyId))
                {
                    Add(RuntimeDiagnosticSeverity.Error,
                        $"Клавиша «{keyToken}» не распознана. Подходят: {KeyboardKeySyntax.AllowedKeysHint}.");
                }
                else if (ScriptCommandSyntax.IsKeyboardEventPrefix(line))
                {
                    Add(RuntimeDiagnosticSeverity.Error,
                        "Формат события: «При. нажатии клавиши.пробел» — клавиша пишется словом или одним символом.");
                }
                else if (ScriptCommandSyntax.IsEveryEventPrefix(line))
                {
                    Add(RuntimeDiagnosticSeverity.Error,
                        "Формат события: «Каждые.2.секунды» — целое число секунд, минимум 1.");
                }
                else if (ScriptCommandSyntax.IsFrameEventPrefix(line))
                {
                    Add(RuntimeDiagnosticSeverity.Error, "Формат события: «Каждый кадр».");
                }
                else
                {
                    Add(RuntimeDiagnosticSeverity.Error,
                        "Поддерживаются события «При. запуске», «При. нажатии», «При. отпускании», «При. наведении», «При. нажатии клавиши.X», «Каждые.N.секунды», «Каждый кадр», «Когда.» и «Как.Только».");
                }
                continue;
            }

            if (eventLineKind == ScriptEventLineKind.OutsideEvent)
            {
                commandsOutsideEvent = true;
                continue;
            }

            if (ConditionalDirectiveParser.TryParseIf(line, out var condition))
            {
                knownValues.Clear();
                if (!flow.BeginIf(condition, lineNumber, out var error))
                {
                    Add(RuntimeDiagnosticSeverity.Error, error);
                }
                else
                {
                    ValidateConditionReferences(condition);
                }
                continue;
            }

            if (ConditionalDirectiveParser.IsForever(line))
            {
                flow.BeginForever(lineNumber);
                continue;
            }

            if (ConditionalDirectiveParser.TryParseWhile(line, out condition))
            {
                knownValues.Clear();
                if (!flow.BeginWhile(condition, lineNumber, out var error))
                {
                    Add(RuntimeDiagnosticSeverity.Error, error);
                }
                else
                {
                    ValidateConditionReferences(condition);
                }
                continue;
            }

            if (ConditionalDirectiveParser.IsWhilePrefix(line))
            {
                Add(RuntimeDiagnosticSeverity.Error, "Формат цикла: «Пока условие то». Например: «Пока жизни > 0 то».");
                continue;
            }

            if (ConditionalDirectiveParser.IsIfPrefix(line))
            {
                Add(RuntimeDiagnosticSeverity.Error, "Формат условия: «Если условие то». Например: «Если готово = истина то».");
                continue;
            }

            if (ConditionalDirectiveParser.TryParseElseIf(line, out condition))
            {
                knownValues.Clear();
                if (!flow.ElseIf(condition, lineNumber, out var error))
                {
                    Add(RuntimeDiagnosticSeverity.Error, error);
                }
                else
                {
                    ValidateConditionReferences(condition);
                }
                continue;
            }

            if (ConditionalDirectiveParser.IsElse(line))
            {
                knownValues.Clear();
                if (!flow.Else(lineNumber, out var error))
                {
                    Add(RuntimeDiagnosticSeverity.Error, error);
                }
                continue;
            }

            if (ConditionalDirectiveParser.IsElsePrefix(line))
            {
                Add(RuntimeDiagnosticSeverity.Error, "Формат условия: «Иначе если условие то» или отдельная команда «Иначе».");
                continue;
            }

            if (ConditionalDirectiveParser.IsEnd(line))
            {
                knownValues.Clear();
                if (eventStarted && flow.OpenBlocks.Count == 0)
                {
                    commands.Add(new RuntimeCommand("event-end", string.Empty, lineNumber));
                    eventStarted = false;
                    requiredEventEnd = null;
                    continue;
                }
                if (!flow.End(out _, out var error))
                {
                    Add(RuntimeDiagnosticSeverity.Error, error);
                }
                continue;
            }

            if (ConditionalDirectiveParser.IsBreak(line))
            {
                if (!flow.Break(lineNumber, out var error))
                {
                    Add(RuntimeDiagnosticSeverity.Error, error);
                }
                continue;
            }

            var imageMatch = CommandPattern.Match(line);
            if (imageMatch.Success)
            {
                var argument = imageMatch.Groups["arg"].Value.Trim();
                var localSpriteFiles = ProjectContentStore.ListResourceFiles(
                    contentStore.GetObjectResourceDirectory(project, scene, item.Id, "sprites"), ProjectContentStore.ImageExtensions);
                var imageAssignment = ImageCommandSyntax.ResolveAssignment(argument, item.Id, objects,
                    fileName => localSpriteFiles.Any(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase)));
                var path = FindResource(project, scene, item.Id, "sprites", argument, objects);
                if (path is null)
                    Add(RuntimeDiagnosticSeverity.Error, $"Изображение «{argument}» не найдено.");
                else
                    commands.Add(new RuntimeCommand("set-image", imageAssignment.FileName, lineNumber,
                        TargetObjectId: imageAssignment.TargetId, ResourceOwnerId: imageAssignment.ResourceOwnerId));
                continue;
            }

            if (ImageCommandSyntax.StartsStepCommand(line))
            {
                if (!ImageCommandSyntax.TryParseStepCommand(line, out var targetName, out var direction))
                {
                    Add(RuntimeDiagnosticSeverity.Error,
                        "Формат команды: «Изменить изображение. перед/назад» или «Изменить изображение. имя объекта.перед/назад».");
                    continue;
                }

                var targetObjectId = item.Id;
                if (targetName is not null)
                {
                    var target = objects.FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, targetName, StringComparison.OrdinalIgnoreCase));
                    if (target is null)
                    {
                        Add(RuntimeDiagnosticSeverity.Error, $"Объект «{targetName}» не найден.");
                        continue;
                    }
                    targetObjectId = target.Id;
                }

                var spriteDirectory = contentStore.GetObjectResourceDirectory(project, scene, targetObjectId, "sprites");
                if (ProjectContentStore.ListResourceFiles(spriteDirectory, ProjectContentStore.ImageExtensions).Count == 0)
                    Add(RuntimeDiagnosticSeverity.Warning, $"У объекта «{objects.First(candidate => candidate.Id == targetObjectId).Name}» пока нет изображений для переключения.");
                else
                    commands.Add(new RuntimeCommand("step-image", direction.ToLowerInvariant(), lineNumber,
                        TargetObjectId: targetObjectId, ResourceOwnerId: targetObjectId));
                continue;
            }

            var setTransparency = SetTransparencyPattern.Match(line);
            if (setTransparency.Success)
            {
                if (!ImageEffectSyntax.TryParsePercent(setTransparency.Groups["amount"].Value, out var amount))
                {
                    Add(RuntimeDiagnosticSeverity.Warning, "Прозрачность вне диапазона 0–100; команда пропущена.");
                }
                else
                {
                    commands.Add(new RuntimeCommand("set-transparency", amount.ToString("R", CultureInfo.InvariantCulture), lineNumber));
                }
                continue;
            }

            if (line.StartsWith("Задать прозрачность", StringComparison.OrdinalIgnoreCase))
            {
                Add(RuntimeDiagnosticSeverity.Error, "Формат команды: «Задать прозрачность. 50»; допустимо от 0 до 100.");
                continue;
            }

            var changeTransparency = ChangeTransparencyPattern.Match(line);
            if (changeTransparency.Success)
            {
                if (!double.TryParse(changeTransparency.Groups["amount"].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var amount) || !double.IsFinite(amount))
                {
                    Add(RuntimeDiagnosticSeverity.Error, "Укажи изменение прозрачности числом.");
                }
                else
                {
                    commands.Add(new RuntimeCommand("change-transparency", amount.ToString("R", CultureInfo.InvariantCulture), lineNumber));
                }
                continue;
            }

            if (line.StartsWith("Изменить прозрачность", StringComparison.OrdinalIgnoreCase))
            {
                Add(RuntimeDiagnosticSeverity.Error, "Формат команды: «Изменить прозрачность. 10» или «Изменить прозрачность. -10».");
                continue;
            }

            var setSize = SetSizePattern.Match(line);
            if (setSize.Success)
            {
                if (!double.TryParse(setSize.Groups["amount"].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var amount) || !SpriteSizeSyntax.IsValidSetSize(amount))
                {
                    Add(RuntimeDiagnosticSeverity.Error, "Размер образа должен быть числом от 0 до 100000 процентов.");
                }
                else
                {
                    commands.Add(new RuntimeCommand("set-size", amount.ToString("R", CultureInfo.InvariantCulture), lineNumber));
                }
                continue;
            }

            if (line.StartsWith("Задать размер", StringComparison.OrdinalIgnoreCase))
            {
                Add(RuntimeDiagnosticSeverity.Error, "Формат команды: «Задать размер. 100»; допустимо от 0 до 100000 процентов, где 100 — исходный размер.");
                continue;
            }

            var changeSize = ChangeSizePattern.Match(line);
            if (changeSize.Success)
            {
                if (!double.TryParse(changeSize.Groups["amount"].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var amount) || !double.IsFinite(amount))
                {
                    Add(RuntimeDiagnosticSeverity.Error, "Изменение размера должно быть числом в процентах.");
                }
                else
                {
                    commands.Add(new RuntimeCommand("change-size", amount.ToString("R", CultureInfo.InvariantCulture), lineNumber));
                }
                continue;
            }

            if (line.StartsWith("Изменить размер", StringComparison.OrdinalIgnoreCase))
            {
                Add(RuntimeDiagnosticSeverity.Error, "Формат команды: «Изменить размер. 10» или «Изменить размер. -10».");
                continue;
            }

            var setBrightness = SetBrightnessPattern.Match(line);
            if (setBrightness.Success)
            {
                if (!ImageEffectSyntax.TryParsePercent(setBrightness.Groups["amount"].Value, out var amount))
                {
                    Add(RuntimeDiagnosticSeverity.Warning, "Яркость вне диапазона 0–100; команда пропущена.");
                }
                else
                {
                    commands.Add(new RuntimeCommand("set-brightness", amount.ToString("R", CultureInfo.InvariantCulture), lineNumber));
                }
                continue;
            }

            if (line.StartsWith("Задать яркость", StringComparison.OrdinalIgnoreCase))
            {
                Add(RuntimeDiagnosticSeverity.Error, "Формат команды: «Задать яркость. 50»; допустимо от 0 до 100.");
                continue;
            }

            var changeBrightness = ChangeBrightnessPattern.Match(line);
            if (changeBrightness.Success)
            {
                if (!double.TryParse(changeBrightness.Groups["amount"].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var amount) || !double.IsFinite(amount))
                {
                    Add(RuntimeDiagnosticSeverity.Error, "Укажи изменение яркости числом.");
                }
                else
                {
                    commands.Add(new RuntimeCommand("change-brightness", amount.ToString("R", CultureInfo.InvariantCulture), lineNumber));
                }
                continue;
            }

            if (line.StartsWith("Изменить яркость", StringComparison.OrdinalIgnoreCase))
            {
                Add(RuntimeDiagnosticSeverity.Error, "Формат команды: «Изменить яркость. 10» или «Изменить яркость. -10».");
                continue;
            }

            if (ScriptCommandSyntax.TryParseMoveBy(line, out var moveOffset, out var moveError))
            {
                if (moveError is null) commands.Add(new RuntimeCommand("move-by", moveOffset, lineNumber));
                else Add(RuntimeDiagnosticSeverity.Error, moveError);
                continue;
            }

            if (ScriptCommandSyntax.TryParseSceneCommand(line, out var resumeScene, out var sceneName, out var sceneError))
            {
                if (sceneError is not null)
                {
                    Add(RuntimeDiagnosticSeverity.Error, sceneError);
                }
                else if (!project.Scenes.Any(candidate =>
                             string.Equals(candidate.Name, sceneName, StringComparison.OrdinalIgnoreCase)))
                {
                    Add(RuntimeDiagnosticSeverity.Error, $"Сцена «{sceneName}» не найдена.");
                }
                else
                {
                    var targetScene = project.Scenes.First(candidate =>
                        string.Equals(candidate.Name, sceneName, StringComparison.OrdinalIgnoreCase));
                    commands.Add(new RuntimeCommand(resumeScene ? "resume-scene" : "launch-scene", targetScene.Name, lineNumber));
                }
                continue;
            }

            if (ScriptCommandSyntax.TryParseOrientationCommand(line, out var orientationMode, out var orientationError))
            {
                if (orientationError is null) commands.Add(new RuntimeCommand("set-orientation", orientationMode, lineNumber));
                else Add(RuntimeDiagnosticSeverity.Error, orientationError);
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

            var waitMatch = WaitPattern.Match(line);
            if (waitMatch.Success)
            {
                var unit = waitMatch.Groups["unit"].Value;
                var multiplier = unit.StartsWith("час", StringComparison.OrdinalIgnoreCase) ? 3_600_000L :
                    unit.StartsWith("минут", StringComparison.OrdinalIgnoreCase) ? 60_000L : 1_000L;
                if (long.TryParse(waitMatch.Groups["amount"].Value, out var amount) && amount <= int.MaxValue / multiplier)
                {
                    commands.Add(new RuntimeCommand("wait", (amount * multiplier).ToString(), lineNumber));
                }
                else
                {
                    Add(RuntimeDiagnosticSeverity.Error, "Время ожидания слишком большое.");
                }
                continue;
            }

            if (line.StartsWith("Ждать", StringComparison.OrdinalIgnoreCase))
            {
                Add(RuntimeDiagnosticSeverity.Error, "Формат команды: Ждать число и единицу времени, например «Ждать 2 секунды».");
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

            if (ScriptCommandSyntax.TryParseLoopSound(line, out var loopFile, out var loopError))
            {
                if (loopError is not null)
                {
                    Add(RuntimeDiagnosticSeverity.Error, loopError);
                }
                else
                {
                    var effectiveLoopFile = knownValues.TryGetValue(loopFile, out var knownLoop) ? knownLoop : loopFile;
                    if (!LooksLikeDynamicValue(loopFile, knownValues) &&
                        FindResource(project, scene, item.Id, "sounds", effectiveLoopFile, objects) is null)
                        Add(RuntimeDiagnosticSeverity.Error, $"Звуковой файл «{effectiveLoopFile}» не найден.");
                    else
                        commands.Add(new RuntimeCommand("loop-sound", loopFile, lineNumber));
                }
                continue;
            }

            if (ScriptCommandSyntax.TryParseStopSound(line, out var stopFile, out var stopError))
            {
                if (stopError is not null)
                {
                    Add(RuntimeDiagnosticSeverity.Error, stopError);
                }
                else
                {
                    var effectiveStopFile = knownValues.TryGetValue(stopFile, out var knownStop) ? knownStop : stopFile;
                    if (!LooksLikeDynamicValue(stopFile, knownValues) &&
                        FindResource(project, scene, item.Id, "sounds", effectiveStopFile, objects) is null)
                        Add(RuntimeDiagnosticSeverity.Error, $"Звуковой файл «{effectiveStopFile}» не найден.");
                    else
                        commands.Add(new RuntimeCommand("stop-sound", stopFile, lineNumber));
                }
                continue;
            }

            if (ScriptCommandSyntax.TryParseVolume(line, out var volumeFile, out var volumeAmount, out var volumeError))
            {
                if (volumeError is not null)
                {
                    Add(RuntimeDiagnosticSeverity.Error, volumeError);
                }
                else if (!ImageEffectSyntax.TryParsePercent(volumeAmount, out var volume))
                {
                    Add(RuntimeDiagnosticSeverity.Warning, "Громкость вне диапазона 0–100; команда пропущена.");
                }
                else if (volumeFile is null)
                {
                    commands.Add(new RuntimeCommand("set-volume", volume.ToString("R", CultureInfo.InvariantCulture), lineNumber));
                }
                else
                {
                    var effectiveVolumeFile = knownValues.TryGetValue(volumeFile, out var knownVolume) ? knownVolume : volumeFile;
                    if (!LooksLikeDynamicValue(volumeFile, knownValues) &&
                        FindResource(project, scene, item.Id, "sounds", effectiveVolumeFile, objects) is null)
                        Add(RuntimeDiagnosticSeverity.Error, $"Звуковой файл «{effectiveVolumeFile}» не найден.");
                    else
                        commands.Add(new RuntimeCommand("set-sound-volume",
                            $"{volume.ToString("R", CultureInfo.InvariantCulture)} {volumeFile}", lineNumber));
                }
                continue;
            }

            if (ScriptCommandSyntax.TryParsePlayImage(line, out var imageSpeed, out var imageObjectName, out var imageError))
            {
                if (imageError is not null)
                {
                    Add(RuntimeDiagnosticSeverity.Error, imageError);
                }
                else
                {
                    var imageTargetId = item.Id;
                    var imageTargetName = item.Name;
                    if (imageObjectName is not null)
                    {
                        var imageTarget = objects.FirstOrDefault(candidate =>
                            string.Equals(candidate.Name, imageObjectName, StringComparison.OrdinalIgnoreCase));
                        if (imageTarget is null)
                        {
                            Add(RuntimeDiagnosticSeverity.Error, $"Объект «{imageObjectName}» не найден.");
                            continue;
                        }
                        imageTargetId = imageTarget.Id;
                        imageTargetName = imageTarget.Name;
                    }
                    var imageSpriteDirectory = contentStore.GetObjectResourceDirectory(project, scene, imageTargetId, "sprites");
                    if (ProjectContentStore.ListResourceFiles(imageSpriteDirectory, ProjectContentStore.ImageExtensions).Count == 0)
                        Add(RuntimeDiagnosticSeverity.Warning, $"У объекта «{imageTargetName}» пока нет изображений для переключения.");
                    else
                        commands.Add(new RuntimeCommand("play-image", imageSpeed.ToString(CultureInfo.InvariantCulture), lineNumber,
                            TargetObjectId: imageTargetId, ResourceOwnerId: imageTargetId));
                }
                continue;
            }

            if (ScriptCommandSyntax.TryParseStopImage(line, out var stopImageError))
            {
                if (stopImageError is null) commands.Add(new RuntimeCommand("stop-image", string.Empty, lineNumber));
                else Add(RuntimeDiagnosticSeverity.Error, stopImageError);
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
                if (ScriptCommandSyntax.TryReadPositionAxis(name, out var positionAxis))
                {
                    commands.Add(new RuntimeCommand("set-position",
                        $"{ScriptCommandSyntax.AxisId(positionAxis)} {value}", lineNumber));
                    continue;
                }
                if (!ScriptCommandSyntax.ValidateVariableName(name, out var nameError))
                {
                    Add(RuntimeDiagnosticSeverity.Error, nameError!);
                    continue;
                }
                if (long.TryParse(value, out _) || IsQuoted(value) || IsBooleanLiteral(value)) knownValues[name] = value.Trim('"');
                else if (knownValues.ContainsKey(value)) knownValues[name] = knownValues[value];
                else knownValues.Remove(name);
                commands.Add(new RuntimeCommand("set-value", $"{name} = {value}", lineNumber));
                continue;
            }

            if (line.StartsWith("Задать Свойство", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Присвоить Свойство", StringComparison.OrdinalIgnoreCase))
            {
                Add(RuntimeDiagnosticSeverity.Error, "Формат команды: «Задать Свойство.х = 5» — имя свойства, знак = и значение.");
                continue;
            }

            var change = Regex.Match(line, "^Изменить\\s+(?<name>[^\\s]+)\\s+(?<amount>-?\\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (change.Success)
            {
                var changeName = change.Groups["name"].Value;
                var changeAmount = change.Groups["amount"].Value;
                if (ScriptCommandSyntax.TryReadPositionAxis(changeName, out var changeAxis))
                {
                    commands.Add(new RuntimeCommand("change-position",
                        $"{ScriptCommandSyntax.AxisId(changeAxis)} {changeAmount}", lineNumber));
                    continue;
                }
                if (!ScriptCommandSyntax.ValidateVariableName(changeName, out var changeNameError))
                {
                    Add(RuntimeDiagnosticSeverity.Error, changeNameError!);
                    continue;
                }
                commands.Add(new RuntimeCommand("change-value", $"{changeName} {changeAmount}", lineNumber));
                continue;
            }

            if (line.StartsWith("Изменить Свойство", StringComparison.OrdinalIgnoreCase))
            {
                Add(RuntimeDiagnosticSeverity.Error, "Формат команды: «Изменить Свойство.х 5» — сдвиг по одной оси целым числом.");
                continue;
            }

            if (line.StartsWith("Печать", StringComparison.OrdinalIgnoreCase))
            {
                var expression = line["Печать".Length..].Trim();
                if (expression.Length == 0) Add(RuntimeDiagnosticSeverity.Error, "После команды «Печать» нужно значение.");
                else commands.Add(new RuntimeCommand("print", expression, lineNumber));
                continue;
            }

            if (ScriptCommandSyntax.TryParseVisibility(line, out var visibleState, out var visibilityTarget, out var visibilityError))
            {
                if (visibilityError is not null)
                {
                    Add(RuntimeDiagnosticSeverity.Error, visibilityError);
                }
                else if (visibilityTarget is null)
                {
                    commands.Add(new RuntimeCommand(visibleState ? "show" : "hide", string.Empty, lineNumber,
                        TargetObjectId: item.Id));
                }
                else
                {
                    var visibilityObject = objects.FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, visibilityTarget, StringComparison.OrdinalIgnoreCase));
                    if (visibilityObject is null)
                    {
                        Add(RuntimeDiagnosticSeverity.Error, $"Объект «{visibilityTarget}» не найден в этой сцене.");
                    }
                    else
                    {
                        commands.Add(new RuntimeCommand(visibleState ? "show" : "hide", visibilityObject.Name, lineNumber,
                            TargetObjectId: visibilityObject.Id));
                    }
                }
                continue;
            }

            if (ScriptCommandSyntax.TryParseAsk(line, out var askQuestion, out var askVariable, out var askError))
            {
                if (askError is not null)
                {
                    Add(RuntimeDiagnosticSeverity.Error, askError);
                }
                else if (!ScriptCommandSyntax.ValidateVariableName(askVariable, out var askNameError))
                {
                    Add(RuntimeDiagnosticSeverity.Error, askNameError!);
                }
                else
                {
                    commands.Add(new RuntimeCommand("ask", $"{askQuestion} {askVariable}", lineNumber));
                }
                continue;
            }

            Add(RuntimeDiagnosticSeverity.Error, $"Неизвестная или пока неподдерживаемая команда «{line.Split(' ', 2)[0]}».");
        }

        if (requiredEventEnd is not null)
        {
            AddAtLine(RuntimeDiagnosticSeverity.Error,
                $"Для блока «{requiredEventEnd}» не найдена команда «Конец».", requiredEventLine);
        }

        foreach (var openBlock in flow.OpenBlocks)
        {
            diagnostics.Add(new RuntimeDiagnostic(RuntimeDiagnosticSeverity.Error,
                $"Для блока «{openBlock.Name}» не найдена команда «Конец».",
                scene.Name, item.Name, openBlock.Line, scene.Id, item.Id));
        }

        if (commandsOutsideEvent)
        {
            AddAtLine(RuntimeDiagnosticSeverity.Warning,
                "Команды вне событий пропущены. Чтобы выполнить команды при старте, помести их в «При. запуске».", 1);
        }

        return commands;

        void Add(RuntimeDiagnosticSeverity severity, string message) => diagnostics.Add(
            new RuntimeDiagnostic(severity, message, scene.Name, item.Name, lineNumber, scene.Id, item.Id));

        void AddAtLine(RuntimeDiagnosticSeverity severity, string message, int diagnosticLine) => diagnostics.Add(
            new RuntimeDiagnostic(severity, message, scene.Name, item.Name, diagnosticLine, scene.Id, item.Id));

        void ValidateConditionReferences(string conditionExpression)
        {
            (List<string> TouchedObjectNames, List<string> KeyboardKeys) references;
            try
            {
                references = ConditionalDirectiveParser.ExtractPropertyReferences(conditionExpression);
            }
            catch (FormatException)
            {
                return;
            }
            foreach (var objectName in references.TouchedObjectNames)
            {
                if (!objects.Any(candidate => string.Equals(candidate.Name, objectName, StringComparison.OrdinalIgnoreCase)))
                    Add(RuntimeDiagnosticSeverity.Error,
                        $"В условии «Свойство.коснулся.{objectName}» объект «{objectName}» не найден в этой сцене.");
            }
            foreach (var keyToken in references.KeyboardKeys)
            {
                if (!KeyboardKeySyntax.TryNormalize(keyToken, out _))
                    Add(RuntimeDiagnosticSeverity.Error,
                        $"В условии клавиша «{keyToken}» не распознана. Подходят: {KeyboardKeySyntax.AllowedKeysHint}.");
            }
        }
    }

    private string? FindResource(IskraProject project, IskraScene scene, Guid ownerId, string resourceType,
        string reference, IReadOnlyList<IskraObject> objects)
    {
        var extensions = resourceType == "sprites" ? ProjectContentStore.ImageExtensions : ProjectContentStore.AudioExtensions;
        var localFiles = ProjectContentStore.ListResourceFiles(
            contentStore.GetObjectResourceDirectory(project, scene, ownerId, resourceType), extensions);
        var resolved = ImageCommandSyntax.ResolveReference(reference, ownerId, objects,
            fileName => localFiles.Any(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase)));
        var directory = contentStore.GetObjectResourceDirectory(project, scene, resolved.OwnerId, resourceType);
        return ProjectContentStore.ListResourceFiles(directory, extensions).FirstOrDefault(path =>
            string.Equals(Path.GetFileName(path), resolved.FileName, StringComparison.OrdinalIgnoreCase));
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
            var scriptObjects = scene.Objects.Select(item => (Id: item.Id, Name: item.Name))
                .Append((Id: IskraObject.BackgroundId, Name: "Фон"));
            foreach (var (objectId, objectName) in scriptObjects)
            {
                var scriptPath = contentStore.GetObjectScriptPath(project, scene, objectId);
                content.Append('|').Append(objectId).Append(':').Append(objectName).Append(':');
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

    private static bool IsBooleanLiteral(string value) =>
        value.Equals("истина", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("ложь", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("нет", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("false", StringComparison.OrdinalIgnoreCase);

    internal static string ResolveValue(string source, IReadOnlyDictionary<string, string> variables)
    {
        var parts = new List<string>();
        var start = 0;
        var inQuotes = false;
        for (var index = 0; index < source.Length; index++)
        {
            if (inQuotes && source[index] == '\\' && index + 1 < source.Length &&
                (source[index + 1] == '"' || source[index + 1] == '\\'))
            {
                index++;
            }
            else if (source[index] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && source[index] == '.' && index + 1 < source.Length && source[index + 1] == '.')
            {
                parts.Add(source[start..index]);
                start = index + 2;
                index++;
            }
        }
        parts.Add(source[start..]);
        var output = new StringBuilder();
        foreach (var part in parts)
        {
            var value = part.Trim();
            if (IsQuoted(value)) output.Append(value[1..^1]);
            else if (long.TryParse(value, out _)) output.Append(value);
            else output.Append(ConditionalDirectiveParser.ResolveVariable(value, variables));
        }
        return output.ToString();
    }

    private static bool TryResolveSound(string source, CompiledObject item, CompiledScene scene, IskraProject project,
        ProjectContentStore contentStore, IReadOnlyDictionary<string, string> variables, out string path)
    {
        var reference = variables.TryGetValue(source, out var value) ? value : source;
        var sourceScene = project.Scenes.First(value => value.Id == scene.Id);
        var localSoundFiles = ProjectContentStore.ListResourceFiles(
            contentStore.GetObjectResourceDirectory(project, sourceScene, item.Id, "sounds"), ProjectContentStore.AudioExtensions);
        var objectReferences = scene.Objects.Select(candidate => new IskraObject { Id = candidate.Id, Name = candidate.Name }).ToArray();
        var resolved = ImageCommandSyntax.ResolveReference(reference, item.Id, objectReferences,
            fileName => localSoundFiles.Any(candidate => string.Equals(Path.GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase)));
        var directory = contentStore.GetObjectResourceDirectory(project, sourceScene, resolved.OwnerId, "sounds");
        path = ProjectContentStore.ListResourceFiles(directory, ProjectContentStore.AudioExtensions)
            .FirstOrDefault(candidate => string.Equals(Path.GetFileName(candidate), resolved.FileName, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
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
