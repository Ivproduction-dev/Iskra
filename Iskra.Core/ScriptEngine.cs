using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace IskraStudio;

internal interface IEngineSound
{
    Task Completion { get; }

    double PositionSeconds { get; }

    bool Loop { get; set; }

    void SetVolume(double volume);

    void Pause();

    void Resume();

    void Stop();
}

internal interface IEngineHost
{
    double SurfaceWidth { get; }

    double SurfaceHeight { get; }

    bool Paused { get; }

    Task WaitWhilePausedAsync();

    CancellationToken Token { get; }

    void Output(string objectName, string text);

    void VariablesChanged(IReadOnlyList<RuntimeDebugVariable> variables);

    Task<string?> AskAsync(string question);

    IEngineSound? PlaySound(string path, bool loop, double positionSeconds);

    void OrientationChanged(ProjectOrientation orientation);

    void SceneSwitchRequested(Guid sceneId, bool resume);

    void StateChanged();

    bool Overlaps(EngineObject first, EngineObject second);

    Guid? TopmostAt(double x, double y);

    bool IsOverPoint(EngineObject item, double x, double y);
}

internal sealed class EngineObject
{
    public EngineObject(CompiledObject item)
    {
        Item = item;
        EventBlocks = ScriptEventBlockParser.Parse(item.Commands);
    }

    public CompiledObject Item { get; }

    public string Name => Item.Name;

    public Guid Id => Item.Id;

    public IReadOnlyList<RuntimeCommand> Commands => Item.Commands;

    public IReadOnlyList<ScriptEventBlock> EventBlocks { get; }

    public RuntimeObjectProperties Properties { get; } = new();

    public Dictionary<int, bool> ConditionStates { get; } = [];

    public List<EngineCursor> Cursors { get; } = [];

    public List<EngineCursor> PendingCursors { get; } = [];

    public double X { get; set; }

    public double Y { get; set; }

    public double SizePercent { get; set; } = 100d;

    public double DirectionDegrees { get; set; }

    public double TransparencyPercent { get; set; }

    public double BrightnessPercent { get; set; } = 100d;

    public bool Visible { get; set; } = true;

    public bool FlipX { get; set; }

    public bool FlipY { get; set; }

    public int Layer { get; set; } = 2;

    public string MotionType { get; set; } = "static";

    public double VelocityX { get; set; }

    public double VelocityY { get; set; }

    public double Friction { get; set; } = 0.08d;

    public EngineSmoothMove? SmoothMove { get; set; }

    public Guid? ImageOwnerId { get; set; }

    public int ImageIndex { get; set; } = -1;

    public int ImageSpeed { get; set; }

    public int PendingImageSpeed { get; set; }

    public double ImageCycleElapsed { get; set; }

    public bool IsBackground => Item.Id == IskraObject.BackgroundId;

    public bool IsPressed { get; set; }

    public bool WasHovered { get; set; }

    public string? CurrentImageFile(IReadOnlyDictionary<Guid, List<string>> imageLists)
    {
        var owner = ImageOwnerId;
        var index = ImageIndex;
        if (owner is null || index < 0) return null;
        if (!imageLists.TryGetValue(owner.Value, out var list) || index >= list.Count) return null;
        return list[index];
    }

    public Dictionary<string, string> CurrentValues(IReadOnlyDictionary<string, string> variables)
    {
        var values = new Dictionary<string, string>(variables, StringComparer.Ordinal);
        foreach (var property in Properties.Values)
        {
            values[property.Key] = property.Value;
        }
        return values;
    }
}

internal sealed class EngineCursor(int start, int end)
{
    public int Start { get; } = start;

    public int End { get; } = end;

    public int Index { get; set; } = start;
}

internal sealed class EngineSmoothMove(double startX, double startY, double targetX, double targetY, double durationMilliseconds)
{
    public double StartX { get; } = startX;

    public double StartY { get; } = startY;

    public double TargetX { get; } = targetX;

    public double TargetY { get; } = targetY;

    public double DurationMilliseconds { get; } = durationMilliseconds;

    public double ElapsedMilliseconds { get; set; }

    public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class EngineSound(IEngineSound sound, string path, bool loop)
{
    public IEngineSound Sound { get; } = sound;

    public string Path { get; } = path;

    public bool Loop { get; set; } = loop;

    public bool Ended { get; set; }
}

internal sealed class ScriptEngine
{
    private readonly CompiledProject project;
    private readonly CompiledScene scene;
    private readonly IEngineHost host;
    private readonly Dictionary<Guid, EngineObject> objects = [];
    private readonly Dictionary<Guid, List<string>> imageLists = [];
    private readonly Dictionary<(Guid TargetId, Guid OwnerId), int> imageIndices = [];
    private readonly List<EngineSound> sounds = [];
    private readonly Dictionary<string, double> soundVolumes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> soundLoops = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string SceneName, string ObjectName)> declarations = new(StringComparer.Ordinal);
    private readonly HashSet<(Guid ObjectId, int Start)> runningBlocks = [];
    private readonly Dictionary<(Guid ObjectId, int Start), double> everyElapsed = [];
    private readonly HashSet<string> loggedUnsupported = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool usesTouched;
    private readonly bool needsFrameLoop;
    private readonly RuntimeSceneSnapshot? restore;
    private readonly List<RuntimeSoundSnapshot> pendingSounds = [];
    private readonly IskraProject sourceProject;
    private CancellationTokenSource sceneCancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
    private CancellationToken token = CancellationToken.None;
    private int askDepth;
    private double gravityX;
    private double gravityY;
    private bool started;

    public ScriptEngine(CompiledProject project, CompiledScene scene, IEngineHost host, ProjectContentStore contentStore, IskraProject sourceProject, RuntimeSceneSnapshot? restore = null)
    {
        this.project = project;
        this.scene = scene;
        this.host = host;
        ContentStore = contentStore;
        this.sourceProject = sourceProject;
        this.restore = restore;
        sceneCancellation = CancellationTokenSource.CreateLinkedTokenSource(host.Token);
        token = sceneCancellation.Token;
        Orientation = project.Orientation;        usesTouched = scene.Objects.Any(item => item.Commands.Any(command =>
            command.Argument.Contains(RuntimeObjectProperties.TouchedObjectPropertyPrefix, StringComparison.OrdinalIgnoreCase) ||
            command.Condition?.Contains(RuntimeObjectProperties.TouchedObjectPropertyPrefix, StringComparison.OrdinalIgnoreCase) == true));
        needsFrameLoop = usesTouched || scene.Objects.Any(item => item.Commands.Any(command =>
            command.Kind is "move-to" or "set-scene-gravity" or "set-motion-type" or "frame" or "play-image"));
        foreach (var item in scene.Objects)
        {
            objects[item.Id] = new EngineObject(item);
        }
    }

    public ProjectOrientation Orientation { get; set; }

    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

    public double GlobalVolume { get; set; } = 100d;

    public IReadOnlyCollection<EngineObject> Objects => objects.Values;

    public (Guid SceneId, bool Resume)? PendingScene { get; private set; }

    public Guid SceneId => scene.Id;

    public event Action? SceneRequested;

    public string? CurrentImageFile(EngineObject item) => item.CurrentImageFile(imageLists);

    public IReadOnlyList<string> ImageFiles(Guid ownerId) => imageLists.GetValueOrDefault(ownerId) ?? [];

    public EngineObject? Find(Guid id) => objects.GetValueOrDefault(id);

    public void SetImageList(Guid objectId, List<string> files) => imageLists[objectId] = files;

    private void NotifyVariables()
    {
        var rows = new List<RuntimeDebugVariable>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, place) in declarations)
        {
            if (Variables.TryGetValue(name, out var value) && emitted.Add(name))
            {
                rows.Add(new RuntimeDebugVariable(place.SceneName, place.ObjectName, name, value));
            }
        }
        foreach (var (name, value) in Variables)
        {
            if (emitted.Add(name))
            {
                rows.Add(new RuntimeDebugVariable(string.Empty, string.Empty, name, value));
            }
        }
        host.VariablesChanged(rows);
    }

    public void Prepare()
    {
        foreach (var sceneItem in project.Scenes)
        {
            foreach (var item in sceneItem.Objects)
            {
                foreach (var command in item.Commands)
                {
                    var assignmentIndex = command.Argument.IndexOf('=');
                    var name = command.Kind switch
                    {
                        "set-value" when assignmentIndex > 0 => command.Argument[..assignmentIndex].Trim(),
                        "change-value" when command.Argument.IndexOf(' ') > 0 => command.Argument[..command.Argument.IndexOf(' ')].Trim(),
                        "ask" when command.Argument.LastIndexOf(' ') > 0 => command.Argument[(command.Argument.LastIndexOf(' ') + 1)..].Trim(),
                        _ => string.Empty
                    };
                    if (name.Length == 0) continue;
                    Variables.TryAdd(name, "0");
                    declarations.TryAdd(name, (sceneItem.Name, item.Name));
                }
            }
        }
        NotifyVariables();
    }

    public async Task Start()
    {
        if (started) return;
        started = true;
        ApplyRestore();
        foreach (var item in objects.Values)
        {
            foreach (var cursor in item.PendingCursors)
            {
                item.ConditionStates[cursor.Start] = true;
            }
        }
        foreach (var saved in pendingSounds)
        {
            StartSound(saved.Path, saved.Loop, saved.PositionSeconds);
        }
        pendingSounds.Clear();
        var running = new List<Task>();
        foreach (var item in objects.Values)
        {
            foreach (var cursor in item.PendingCursors)
            {
                running.Add(RunResumedBlockAsync(item, cursor));
            }
            if (restore is not null) continue;
            foreach (var block in item.EventBlocks.Where(block => block.EventKind == "start"))
            {
                if (!runningBlocks.Add((item.Id, block.Start))) continue;
                running.Add(RunBlockSafeAsync(item, block.Start, block.End, "start", releaseSlot: true));
            }
            if (item.PendingImageSpeed > 0)
            {
                item.ImageSpeed = item.PendingImageSpeed;
            }
        }
        await Task.WhenAll(running);
    }

    private void ApplyRestore()
    {
        if (restore is null) return;
        Variables.Clear();
        foreach (var pair in restore.Variables)
        {
            Variables[pair.Key] = pair.Value;
        }
        NotifyVariables();
        foreach (var saved in restore.Objects)
        {
            if (!objects.TryGetValue(saved.Id, out var runtime)) continue;
            runtime.X = saved.X;
            runtime.Y = saved.Y;
            runtime.SizePercent = Math.Abs(saved.SizePercent);
            runtime.DirectionDegrees = saved.DirectionDegrees;
            runtime.TransparencyPercent = saved.TransparencyPercent;
            runtime.BrightnessPercent = saved.BrightnessPercent;
            runtime.Visible = saved.Visible;
            runtime.FlipX = saved.FlipX;
            runtime.FlipY = saved.FlipY;
            runtime.ImageOwnerId = saved.SpriteOwnerId;
            runtime.ImageIndex = saved.SpriteIndex;
            runtime.PendingImageSpeed = saved.ImageSpeed;
            runtime.Properties.SetAxisPosition("x", saved.X.ToString("R", CultureInfo.InvariantCulture));
            runtime.Properties.SetAxisPosition("y", saved.Y.ToString("R", CultureInfo.InvariantCulture));
            runtime.Properties.SetSize(runtime.SizePercent);
            runtime.Properties.SetDirection(saved.DirectionDegrees);
            runtime.Properties.SetTransparency(saved.TransparencyPercent);
            foreach (var cursor in saved.Cursors)
            {
                runtime.PendingCursors.Add(new EngineCursor(cursor.Start, cursor.End) { Index = cursor.Index });
            }
        }
        pendingSounds.AddRange(restore.Sounds);
        host.StateChanged();
    }

    public void Tick(double deltaSeconds)
    {
        if (token.IsCancellationRequested) return;
        StepSmoothMoves(deltaSeconds);
        StepPhysics(deltaSeconds);
        StepImageCycling(deltaSeconds);
        StepEveryBlocks(deltaSeconds);
        StepFrameBlocks();
        if (usesTouched) UpdateTouches();
        RefreshAllConditions();
        host.StateChanged();
    }

    private void StepSmoothMoves(double deltaSeconds)
    {
        foreach (var runtime in objects.Values)
        {
            if (runtime.SmoothMove is not { } movement) continue;
            movement.ElapsedMilliseconds = Math.Min(movement.DurationMilliseconds, movement.ElapsedMilliseconds + deltaSeconds * 1000d);
            var progress = movement.DurationMilliseconds <= 0d ? 1d : movement.ElapsedMilliseconds / movement.DurationMilliseconds;
            runtime.X = movement.StartX + (movement.TargetX - movement.StartX) * progress;
            runtime.Y = movement.StartY + (movement.TargetY - movement.StartY) * progress;
            if (runtime.Properties.SetAxisPosition("x", runtime.X.ToString("R", CultureInfo.InvariantCulture)) |
                runtime.Properties.SetAxisPosition("y", runtime.Y.ToString("R", CultureInfo.InvariantCulture)))
            {
                RefreshConditionEvents(runtime);
            }
            if (progress >= 1d)
            {
                runtime.SmoothMove = null;
                movement.Completion.TrySetResult();
            }
        }
    }

    private void StepPhysics(double deltaSeconds)
    {
        var width = host.SurfaceWidth;
        var height = host.SurfaceHeight;
        foreach (var runtime in objects.Values.Where(item => item.Visible && item.MotionType == "dynamic"))
        {
            var requestedDistance = Math.Max(Math.Abs(runtime.VelocityX), Math.Abs(runtime.VelocityY)) * deltaSeconds;
            var steps = Math.Clamp((int)Math.Ceiling(requestedDistance / 8d), 1, 32);
            var stepSeconds = deltaSeconds / steps;
            for (var step = 0; step < steps; step++)
            {
                runtime.VelocityX += gravityX * stepSeconds;
                runtime.VelocityY += -gravityY * stepSeconds;
                runtime.VelocityX *= Math.Exp(-runtime.Friction * stepSeconds);
                runtime.VelocityY *= Math.Exp(-runtime.Friction * stepSeconds);
                var oldX = runtime.X;
                MoveRuntime(runtime, oldX + runtime.VelocityX * stepSeconds, runtime.Y);
                if (HasSolidOverlap(runtime, width, height))
                {
                    MoveRuntime(runtime, oldX, runtime.Y);
                    runtime.VelocityX = Bounce(runtime.VelocityX);
                }
                var oldY = runtime.Y;
                MoveRuntime(runtime, runtime.X, oldY + runtime.VelocityY * stepSeconds);
                if (HasSolidOverlap(runtime, width, height))
                {
                    MoveRuntime(runtime, runtime.X, oldY);
                    runtime.VelocityY = Bounce(runtime.VelocityY);
                }
            }
        }
    }

    private void MoveRuntime(EngineObject runtime, double x, double y)
    {
        runtime.X = x;
        runtime.Y = y;
        var changed = runtime.Properties.SetAxisPosition("x", x.ToString("R", CultureInfo.InvariantCulture));
        changed |= runtime.Properties.SetAxisPosition("y", y.ToString("R", CultureInfo.InvariantCulture));
        if (changed) RefreshConditionEvents(runtime);
    }

    private bool HasSolidOverlap(EngineObject runtime, double width, double height)
    {
        foreach (var other in objects.Values)
        {
            if (other.IsBackground || ReferenceEquals(other, runtime) || !other.Visible || other.MotionType == "none") continue;
            if (host.Overlaps(runtime, other)) return true;
        }
        return false;
    }

    private static double Bounce(double velocity) => Math.Abs(velocity) < 30d ? 0d : -velocity * 0.35d;

    private void StepImageCycling(double deltaSeconds)
    {
        foreach (var runtime in objects.Values)
        {
            if (runtime.ImageSpeed < 1) continue;
            runtime.ImageCycleElapsed += deltaSeconds * 1000d;
            var interval = 1000d / runtime.ImageSpeed;
            while (runtime.ImageCycleElapsed >= interval)
            {
                runtime.ImageCycleElapsed -= interval;
                var owner = runtime.ImageOwnerId ?? runtime.Id;
                if (!imageLists.TryGetValue(owner, out var list) || list.Count == 0) continue;
                var index = (runtime.ImageIndex + 1) % list.Count;
                ApplyImage(runtime, owner, index);
            }
        }
    }

    private void StepEveryBlocks(double deltaSeconds)
    {
        foreach (var runtime in objects.Values)
        {
            foreach (var block in runtime.EventBlocks.Where(block => block.EventKind == "every"))
            {
                if (!int.TryParse(block.Condition, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds < 1) continue;
                var key = (runtime.Id, block.Start);
                var elapsed = everyElapsed.GetValueOrDefault(key) + deltaSeconds;
                if (elapsed < seconds)
                {
                    everyElapsed[key] = elapsed;
                    continue;
                }
                everyElapsed[key] = 0d;
                RunTimedBlock(runtime, block);
            }
        }
    }

    private void RunTimedBlock(EngineObject runtime, ScriptEventBlock block)
    {
        if (token.IsCancellationRequested) return;
        if (!runningBlocks.Add((runtime.Id, block.Start))) return;
        _ = RunBlockSafeAsync(runtime, block.Start, block.End, "Каждые", releaseSlot: true);
    }

    private void StepFrameBlocks()
    {
        foreach (var runtime in objects.Values)
        {
            foreach (var block in runtime.EventBlocks.Where(block => block.EventKind == "frame"))
            {
                if (!runningBlocks.Add((runtime.Id, block.Start))) continue;
                _ = RunBlockSafeAsync(runtime, block.Start, block.End, "Каждый кадр", releaseSlot: true);
            }
        }
    }

    private void UpdateTouches()
    {
        foreach (var runtime in objects.Values.Where(item => !item.IsBackground))
        {
            var changed = false;
            foreach (var other in objects.Values)
            {
                if (other.IsBackground || ReferenceEquals(other, runtime)) continue;
                changed |= runtime.Properties.SetTouched(other.Name, runtime.Visible && other.Visible &&
                    runtime.TransparencyPercent < 100d && other.TransparencyPercent < 100d && host.Overlaps(runtime, other));
            }
            if (changed) RefreshConditionEvents(runtime);
        }
    }

    public void RefreshAllConditions()
    {
        foreach (var runtime in objects.Values)
        {
            RefreshConditionEvents(runtime);
        }
    }

    private void RefreshConditionEvents(EngineObject runtime)
    {
        foreach (var block in runtime.EventBlocks.Where(block => block.EventKind == "when"))
        {
            bool isTrue;
            try
            {
                isTrue = ConditionalDirectiveParser.Evaluate(block.Condition!, runtime.CurrentValues(Variables));
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            var wasTrue = runtime.ConditionStates.GetValueOrDefault(block.Start);
            runtime.ConditionStates[block.Start] = isTrue;
            if (isTrue && !wasTrue && runningBlocks.Add((runtime.Id, block.Start)))
            {
                _ = RunBlockSafeAsync(runtime, block.Start, block.End, "Когда", releaseSlot: true);
            }
        }
    }

    public void PointerPressed(Guid id)
    {
        if (askDepth > 0) return;
        if (!objects.TryGetValue(id, out var runtime)) return;
        runtime.IsPressed = true;
        if (runtime.Properties.SetPressed(true)) RefreshConditionEvents(runtime);
        foreach (var block in runtime.EventBlocks.Where(block => block.EventKind == "press"))
        {
            _ = RunBlockSafeAsync(runtime, block.Start, block.End, "press");
        }
    }

    public void PointerReleased(Guid id, Guid? hitId)
    {
        if (!objects.TryGetValue(id, out var runtime)) return;
        runtime.IsPressed = false;
        if (runtime.Properties.SetPressed(false)) RefreshConditionEvents(runtime);
        if (hitId == id)
        {
            foreach (var block in runtime.EventBlocks.Where(block => block.EventKind == "release"))
            {
                _ = RunBlockSafeAsync(runtime, block.Start, block.End, "release");
            }
        }
    }

    public void PointerMoved(Guid? topmostId)
    {
        foreach (var runtime in objects.Values)
        {
            var hovered = topmostId == runtime.Id;
            if (hovered && !runtime.WasHovered)
            {
                foreach (var block in runtime.EventBlocks.Where(block => block.EventKind == "hover"))
                {
                    _ = RunBlockSafeAsync(runtime, block.Start, block.End, "hover");
                }
            }
            runtime.WasHovered = hovered;
            if (runtime.Properties.SetHovered(hovered)) RefreshConditionEvents(runtime);
        }
    }

    public void KeyDown(string keyId)
    {
        if (askDepth > 0) return;
        foreach (var runtime in objects.Values)
        {
            if (runtime.Properties.SetKeyboardKey(keyId, true)) RefreshConditionEvents(runtime);
            foreach (var block in runtime.EventBlocks.Where(block => block.EventKind == "key" &&
                         block.Condition is { } condition &&
                         KeyboardKeySyntax.TryNormalize(condition, out var blockKey) && blockKey == keyId))
            {
                _ = RunBlockSafeAsync(runtime, block.Start, block.End, $"клавиша {keyId}");
            }
        }
    }

    public void KeyUp(string keyId)
    {
        if (askDepth > 0) return;
        foreach (var runtime in objects.Values)
        {
            if (runtime.Properties.SetKeyboardKey(keyId, false)) RefreshConditionEvents(runtime);
        }
    }

    public void NotifySoundEnded(string path, bool loop)
    {
        if (loop && !token.IsCancellationRequested)
        {
            StartSound(path, loop: true, 0d);
            return;
        }
        var fileName = Path.GetFileName(path);
        foreach (var runtime in objects.Values)
        {
            foreach (var block in runtime.EventBlocks.Where(block => block.EventKind == "sound-ended" &&
                         string.Equals(Path.GetFileName(block.Condition), fileName, StringComparison.OrdinalIgnoreCase)))
            {
                _ = RunBlockSafeAsync(runtime, block.Start, block.End, "Окончание звука");
            }
        }
    }

    private bool EvaluateCondition(string expression, EngineObject runtime)
    {
        try
        {
            return ConditionalDirectiveParser.Evaluate(expression, runtime.CurrentValues(Variables));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task RunBlockSafeAsync(EngineObject runtime, int start, int end, string label, bool releaseSlot = false)
    {
        try
        {
            await ExecuteBlockAsync(runtime, start, end);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            CoreLog.Write($"Ошибка выполнения события «{label}» объекта «{runtime.Name}».", exception);
        }
        finally
        {
            if (releaseSlot) runningBlocks.Remove((runtime.Id, start));
        }
    }

    private async Task RunResumedBlockAsync(EngineObject runtime, EngineCursor cursor)
    {
        try
        {
            await ExecuteBlockAsync(runtime, cursor.Start, cursor.End, cursor.Index, resumed: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            CoreLog.Write($"Ошибка продолжения сценария объекта «{runtime.Name}».", exception);
        }
    }

    private async Task ExecuteBlockAsync(EngineObject runtime, int start, int end, int firstIndex = -1, bool resumed = false)
    {
        var first = firstIndex >= 0 ? firstIndex : start;
        var cursor = new EngineCursor(start, end) { Index = first };
        if (token.IsCancellationRequested) return;
        runtime.Cursors.Add(cursor);
        try
        {
            for (var commandIndex = first; commandIndex < end; commandIndex++)
            {
                if (token.IsCancellationRequested) return;
                await host.WaitWhilePausedAsync();
                if (resumed && commandIndex == first && runtime.Commands[commandIndex].Kind == "wait") continue;
                cursor.Index = commandIndex;
                var command = runtime.Commands[commandIndex];
                switch (command.Kind)
                {
                    case "branch-false":
                        if (!EvaluateCondition(command.Argument, runtime)) commandIndex = command.Target - 1;
                        break;
                    case "jump":
                        commandIndex = command.Target - 1;
                        break;
                    case "loop-jump":
                        await Task.Delay(1, token);
                        commandIndex = command.Target - 1;
                        break;
                    case "set-image":
                        ApplySetImage(command, runtime);
                        break;
                    case "step-image":
                        ApplyStepImage(command, runtime);
                        break;
                    case "set-transparency":
                        if (double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var transparency))
                        {
                            runtime.TransparencyPercent = Math.Clamp(transparency, 0d, 100d);
                            if (runtime.Properties.SetTransparency(runtime.TransparencyPercent)) RefreshConditionEvents(runtime);
                            host.StateChanged();
                        }
                        break;
                    case "change-transparency":
                        if (double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var transparencyChange))
                        {
                            runtime.TransparencyPercent = Math.Clamp(runtime.TransparencyPercent + transparencyChange, 0d, 100d);
                            if (runtime.Properties.SetTransparency(runtime.TransparencyPercent)) RefreshConditionEvents(runtime);
                            host.StateChanged();
                        }
                        break;
                    case "set-size":
                        if (double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && SpriteSizeSyntax.IsValidSetSize(size))
                        {
                            runtime.SizePercent = size;
                            if (runtime.Properties.SetSize(size)) RefreshConditionEvents(runtime);
                            host.StateChanged();
                        }
                        break;
                    case "change-size":
                        if (double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var sizeChange))
                        {
                            runtime.SizePercent = SpriteSizeSyntax.ApplyChange(runtime.SizePercent, sizeChange);
                            if (runtime.Properties.SetSize(runtime.SizePercent)) RefreshConditionEvents(runtime);
                            host.StateChanged();
                        }
                        break;
                    case "set-brightness":
                        if (double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var brightness))
                        {
                            runtime.BrightnessPercent = Math.Clamp(brightness, 0d, 100d);
                            host.StateChanged();
                        }
                        break;
                    case "change-brightness":
                        if (double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var brightnessChange))
                        {
                            runtime.BrightnessPercent = Math.Clamp(runtime.BrightnessPercent + brightnessChange, 0d, 100d);
                            host.StateChanged();
                        }
                        break;
                    case "set-direction":
                        if (double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var direction))
                        {
                            SetDirection(runtime, direction);
                        }
                        break;
                    case "change-direction":
                        if (double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var directionChange))
                        {
                            SetDirection(runtime, runtime.DirectionDegrees + directionChange);
                        }
                        break;
                    case "place":
                        ApplyCoordinates(command.Argument, runtime, absolute: true);
                        break;
                    case "move-by":
                        ApplyCoordinates(command.Argument, runtime, absolute: false);
                        break;
                    case "move-to":
                        await ApplyMoveTo(command.Argument, runtime);
                        break;
                    case "stop-movement":
                        runtime.SmoothMove = null;
                        runtime.VelocityX = 0d;
                        runtime.VelocityY = 0d;
                        break;
                    case "set-layer":
                        if (int.TryParse(command.Argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var layer))
                        {
                            runtime.Layer = layer;
                            host.StateChanged();
                        }
                        break;
                    case "change-layer":
                        if (int.TryParse(command.Argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var layerChange))
                        {
                            runtime.Layer = (int)Math.Clamp((long)runtime.Layer + layerChange, int.MinValue, int.MaxValue);
                            host.StateChanged();
                        }
                        break;
                    case "set-visibility":
                        SetVisible(runtime, command.Argument == "true");
                        break;
                    case "toggle-visibility":
                        SetVisible(runtime, !runtime.Visible);
                        break;
                    case "show":
                    case "hide":
                        if (objects.TryGetValue(command.TargetObjectId ?? runtime.Id, out var visibilityTarget))
                        {
                            SetVisible(visibilityTarget, command.Kind == "show");
                        }
                        break;
                    case "flip":
                        if (command.Argument == "x") runtime.FlipX = !runtime.FlipX;
                        else runtime.FlipY = !runtime.FlipY;
                        host.StateChanged();
                        break;
                    case "set-scene-gravity":
                        var gravityParts = command.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (gravityParts.Length == 2 &&
                            double.TryParse(gravityParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var gx) &&
                            double.TryParse(gravityParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var gy))
                        {
                            gravityX = gx;
                            gravityY = gy;
                        }
                        break;
                    case "set-motion-type":
                        runtime.MotionType = command.Argument;
                        break;
                    case "set-position":
                    case "change-position":
                        ApplyAxisPosition(command, runtime);
                        break;
                    case "launch-scene":
                    case "resume-scene":
                        RequestSceneSwitch(command.Argument, command.Kind == "resume-scene");
                        return;
                    case "set-orientation":
                        Orientation = ResolveOrientation(command.Argument);
                        host.OrientationChanged(Orientation);
                        break;
                    case "wait":
                        await DelayWithPauseAsync(TimeSpan.FromMilliseconds(long.Parse(command.Argument)), token);
                        break;
                    case "play-sound":
                    case "play-sound-wait":
                        if (TryResolveSoundPath(command.Argument, runtime, out var soundPath))
                        {
                            var sound = StartSound(soundPath, soundLoops.GetValueOrDefault(soundPath), 0d);
                            if (command.Kind == "play-sound-wait" && sound is not null)
                            {
                                await sound.Completion.WaitAsync(token);
                            }
                        }
                        break;
                    case "loop-sound":
                        if (TryResolveSoundPath(command.Argument, runtime, out var loopPath))
                        {
                            soundLoops[loopPath] = true;
                            StartSound(loopPath, loop: true, 0d);
                        }
                        break;
                    case "stop-sound":
                        if (TryResolveSoundPath(command.Argument, runtime, out var stopPath))
                        {
                            StopSounds(stopPath);
                        }
                        break;
                    case "set-volume":
                        if (double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var volume))
                        {
                            GlobalVolume = Math.Clamp(volume, 0d, 100d);
                            ApplyVolumes();
                        }
                        break;
                    case "set-sound-volume":
                        var volumeParts = command.Argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                        if (volumeParts.Length == 2 &&
                            double.TryParse(volumeParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var soundVolume) &&
                            TryResolveSoundPath(volumeParts[1], runtime, out var volumePath))
                        {
                            soundVolumes[volumePath] = Math.Clamp(soundVolume, 0d, 100d);
                            ApplyVolumeToPath(volumePath);
                        }
                        break;
                    case "change-sound-volume":
                        var changedVolumeParts = command.Argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                        if (changedVolumeParts.Length == 2 &&
                            double.TryParse(changedVolumeParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var volumeDelta) &&
                            TryResolveSoundPath(RuntimeValueResolver.ResolveValue(changedVolumeParts[1], runtime.CurrentValues(Variables)), runtime, out var changedVolumePath))
                        {
                            var current = soundVolumes.GetValueOrDefault(changedVolumePath, 100d);
                            soundVolumes[changedVolumePath] = Math.Clamp(current + volumeDelta, 0d, 100d);
                            ApplyVolumeToPath(changedVolumePath);
                        }
                        break;
                    case "set-sound-repeat":
                        var repeatParts = command.Argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                        if (repeatParts.Length == 2 &&
                            TryResolveSoundPath(RuntimeValueResolver.ResolveValue(repeatParts[0], runtime.CurrentValues(Variables)), runtime, out var repeatPath))
                        {
                            var repeat = repeatParts[1] == "True";
                            soundLoops[repeatPath] = repeat;
                            foreach (var active in sounds.Where(candidate => string.Equals(candidate.Path, repeatPath, StringComparison.OrdinalIgnoreCase)))
                            {
                                active.Loop = repeat;
                                active.Sound.Loop = repeat;
                            }
                        }
                        break;
                    case "pause-sound":
                    case "resume-sound":
                        if (TryResolveSoundPath(command.Argument, runtime, out var controlledPath))
                        {
                            foreach (var active in sounds.Where(candidate => string.Equals(candidate.Path, controlledPath, StringComparison.OrdinalIgnoreCase)))
                            {
                                if (command.Kind == "pause-sound") active.Sound.Pause();
                                else active.Sound.Resume();
                            }
                        }
                        break;
                    case "play-image":
                        if (int.TryParse(command.Argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var imageSpeed) &&
                            objects.TryGetValue(command.TargetObjectId ?? runtime.Id, out var cyclingTarget))
                        {
                            cyclingTarget.ImageSpeed = Math.Max(1, imageSpeed);
                            cyclingTarget.ImageCycleElapsed = 0d;
                        }
                        break;
                    case "stop-image":
                        if (objects.TryGetValue(command.TargetObjectId ?? runtime.Id, out var stoppedTarget))
                        {
                            stoppedTarget.ImageSpeed = 0;
                        }
                        break;
                    case "set-value":
                        var assignmentIndex = command.Argument.IndexOf('=');
                        if (assignmentIndex > 0)
                        {
                            var variableName = command.Argument[..assignmentIndex].Trim();
                            Variables[variableName] = RuntimeValueResolver.ResolveValue(command.Argument[(assignmentIndex + 1)..].Trim(), runtime.CurrentValues(Variables));
                            NotifyVariables();
                            RefreshAllConditions();
                        }
                        break;
                    case "change-value":
                        var changeSeparator = command.Argument.IndexOf(' ');
                        if (changeSeparator > 0 &&
                            double.TryParse(RuntimeValueResolver.ResolveValue(command.Argument[(changeSeparator + 1)..], runtime.CurrentValues(Variables)),
                                NumberStyles.Float, CultureInfo.InvariantCulture, out var change))
                        {
                            var changedName = command.Argument[..changeSeparator];
                            var current = double.TryParse(Variables.GetValueOrDefault(changedName, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0d;
                            Variables[changedName] = (current + change).ToString("R", CultureInfo.InvariantCulture);
                            NotifyVariables();
                            RefreshAllConditions();
                        }
                        break;
                    case "print":
                        host.Output(runtime.Name, RuntimeValueResolver.ResolveValue(command.Argument, runtime.CurrentValues(Variables)));
                        break;
                    case "ask":
                        var askSeparator = command.Argument.LastIndexOf(' ');
                        if (askSeparator > 0)
                        {
                            var askQuestion = command.Argument[..askSeparator].Trim();
                            if (askQuestion.Length >= 2 && askQuestion[0] == '"' && askQuestion[^1] == '"') askQuestion = askQuestion[1..^1];
                            var askVariable = command.Argument[(askSeparator + 1)..].Trim();
                            askDepth++;
                            try
                            {
                                var answer = await host.AskAsync(askQuestion).WaitAsync(token);
                                if (answer is null) return;
                                Variables[askVariable] = answer;
                                NotifyVariables();
                                RefreshAllConditions();
                            }
                            finally
                            {
                                askDepth--;
                            }
                        }
                        break;
                    default:
                        if (loggedUnsupported.Add(command.Kind))
                        {
                            CoreLog.Write($"Команда «{command.Kind}» не поддержана исполнителем.");
                        }
                        break;
                }
                cursor.Index = commandIndex + 1;
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                runtime.Cursors.Remove(cursor);
            }
        }
    }

    private void ApplySetImage(RuntimeCommand command, EngineObject runtime)
    {
        var target = objects.GetValueOrDefault(command.TargetObjectId ?? runtime.Id);
        var owner = command.ResourceOwnerId ?? runtime.Id;
        if (target is null) return;
        if (!imageLists.TryGetValue(owner, out var list)) return;
        var fileName = command.Argument;
        var index = list.FindIndex(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        ApplyImage(target, owner, index);
    }

    private void ApplyStepImage(RuntimeCommand command, EngineObject runtime)
    {
        var target = objects.GetValueOrDefault(command.TargetObjectId ?? runtime.Id);
        var owner = command.ResourceOwnerId ?? runtime.Id;
        if (target is null || !imageLists.TryGetValue(owner, out var list) || list.Count == 0) return;
        var key = (target.Id, owner);
        var currentIndex = imageIndices.GetValueOrDefault(key);
        var next = (currentIndex + (command.Argument == "назад" ? -1 : 1) + list.Count) % list.Count;
        ApplyImage(target, owner, next);
    }

    private void ApplyImage(EngineObject target, Guid ownerId, int index)
    {
        if (index < 0 || !imageLists.TryGetValue(ownerId, out var list) || index >= list.Count) return;
        imageIndices[(target.Id, ownerId)] = index;
        target.ImageOwnerId = ownerId;
        target.ImageIndex = index;
        host.StateChanged();
    }

    private void ApplyCoordinates(string argument, EngineObject runtime, bool absolute)
    {
        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;
        if (!double.TryParse(RuntimeValueResolver.ResolveValue(parts[0], runtime.CurrentValues(Variables)), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return;
        if (!double.TryParse(RuntimeValueResolver.ResolveValue(parts[1], runtime.CurrentValues(Variables)), NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return;
        if (!absolute)
        {
            x += runtime.X;
            y += runtime.Y;
        }
        runtime.SmoothMove = null;
        MoveRuntime(runtime, x, y);
        host.StateChanged();
    }

    private async Task ApplyMoveTo(string argument, EngineObject runtime)
    {
        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return;
        var values = runtime.CurrentValues(Variables);
        if (!double.TryParse(RuntimeValueResolver.ResolveValue(parts[0], values), NumberStyles.Float, CultureInfo.InvariantCulture, out var targetX)) return;
        if (!double.TryParse(RuntimeValueResolver.ResolveValue(parts[1], values), NumberStyles.Float, CultureInfo.InvariantCulture, out var targetY)) return;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var durationMilliseconds)) return;
        var movement = new EngineSmoothMove(runtime.X, runtime.Y, targetX, targetY, durationMilliseconds);
        runtime.SmoothMove = movement;
        await movement.Completion.Task.WaitAsync(token);
    }

    private void ApplyAxisPosition(RuntimeCommand command, EngineObject runtime)
    {
        var parts = command.Argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return;
        if (!double.TryParse(RuntimeValueResolver.ResolveValue(parts[1], runtime.CurrentValues(Variables)), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return;
        var axis = parts[0].Trim().ToLowerInvariant();
        var x = axis == "y" ? runtime.X : command.Kind == "set-position" ? value : runtime.X + value;
        var y = axis == "y" ? (command.Kind == "set-position" ? value : runtime.Y + value) : runtime.Y;
        runtime.SmoothMove = null;
        MoveRuntime(runtime, x, y);
        host.StateChanged();
    }

    private void SetVisible(EngineObject runtime, bool visible)
    {
        runtime.Visible = visible;
        host.StateChanged();
    }

    private void SetDirection(EngineObject runtime, double degrees)
    {
        var normalized = degrees % 360d;
        if (normalized < 0d) normalized += 360d;
        runtime.DirectionDegrees = normalized;
        if (runtime.Properties.SetDirection(normalized)) RefreshConditionEvents(runtime);
        host.StateChanged();
    }

    private IEngineSound? StartSound(string path, bool loop, double positionSeconds)
    {
        if (loop)
        {
            var existing = sounds.FirstOrDefault(sound => !sound.Ended && sound.Loop &&
                string.Equals(sound.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing.Sound;
        }
        var sound = host.PlaySound(path, loop, positionSeconds);
        if (sound is null) return null;
        var volume = soundVolumes.GetValueOrDefault(path, 100d);
        sound.SetVolume(Math.Clamp(GlobalVolume / 100d * volume / 100d, 0d, 1d));
        sounds.Add(new EngineSound(sound, path, loop));
        return sound;
    }

    private void StopSounds(string path)
    {
        foreach (var sound in sounds.Where(sound => string.Equals(sound.Path, path, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            sound.Sound.Stop();
            sound.Ended = true;
            sounds.Remove(sound);
        }
    }

    private void ApplyVolumes()
    {
        foreach (var sound in sounds)
        {
            var perSound = soundVolumes.GetValueOrDefault(sound.Path, 100d);
            sound.Sound.SetVolume(Math.Clamp(GlobalVolume / 100d * perSound / 100d, 0d, 1d));
        }
    }

    private void ApplyVolumeToPath(string path)
    {
        foreach (var sound in sounds.Where(sound => string.Equals(sound.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            var perSound = soundVolumes.GetValueOrDefault(path, 100d);
            sound.Sound.SetVolume(Math.Clamp(GlobalVolume / 100d * perSound / 100d, 0d, 1d));
        }
    }

    private bool TryResolveSoundPath(string source, EngineObject runtime, out string path) =>
        RuntimeValueResolver.TryResolveSound(source, runtime.Item, scene, sourceProject, ContentStore, runtime.CurrentValues(Variables), out path);

    internal ProjectContentStore ContentStore { get; }

    private void RequestSceneSwitch(string sceneName, bool resume)
    {
        var target = project.Scenes.FirstOrDefault(candidate => string.Equals(candidate.Name, sceneName, StringComparison.OrdinalIgnoreCase));
        if (target is null) return;
        PendingScene = (target.Id, resume);
        SceneRequested?.Invoke();
        sceneCancellation.Cancel();
    }

    private static ProjectOrientation ResolveOrientation(string mode) => mode switch
    {
        "horizontal" => ProjectOrientation.Horizontal,
        "vertical" => ProjectOrientation.Vertical,
        _ => ProjectOrientation.Horizontal
    };

    private async Task DelayWithPauseAsync(TimeSpan duration, CancellationToken token)
    {
        var remaining = duration;
        while (remaining > TimeSpan.Zero)
        {
            await host.WaitWhilePausedAsync();
            var started = Stopwatch.GetTimestamp();
            var delay = Task.Delay(remaining, token);
            var finished = await Task.WhenAny(delay, WaitForUnpause());
            remaining -= Stopwatch.GetElapsedTime(started);
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            if (finished == delay) await delay;
        }
    }

    private Task WaitForUnpause() => host.Paused ? host.WaitWhilePausedAsync() : Task.CompletedTask;

    public RuntimeSceneSnapshot CaptureSnapshot()
    {
        var objectSnapshots = new List<RuntimeObjectSnapshot>();
        foreach (var runtime in objects.Values)
        {
            objectSnapshots.Add(new RuntimeObjectSnapshot(
                runtime.Id,
                runtime.X,
                runtime.Y,
                runtime.SizePercent,
                runtime.DirectionDegrees,
                runtime.TransparencyPercent,
                runtime.BrightnessPercent,
                runtime.Visible,
                runtime.FlipX,
                runtime.FlipY,
                runtime.ImageOwnerId,
                runtime.ImageIndex,
                runtime.ImageSpeed,
                runtime.Cursors.Select(cursor => new RuntimeCursorSnapshot(cursor.Start, cursor.End, cursor.Index)).ToList()));
        }
        var soundSnapshots = sounds
            .Where(sound => !sound.Ended)
            .Select(sound => new RuntimeSoundSnapshot(sound.Path, sound.Loop, sound.Sound.PositionSeconds))
            .ToList();
        return new RuntimeSceneSnapshot(
            scene.Id,
            new Dictionary<string, string>(Variables, StringComparer.Ordinal),
            objectSnapshots,
            soundSnapshots);
    }

    public void Cancel() => sceneCancellation.Cancel();

    public async Task WaitForBlocksAsync(int timeoutMilliseconds = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (runningBlocks.Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, CancellationToken.None);
        }
    }
}
