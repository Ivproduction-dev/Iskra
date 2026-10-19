using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IskraStudio;

internal sealed record RuntimeCursorSnapshot(int Start, int End, int Index);

internal sealed record RuntimeObjectSnapshot(
    Guid Id,
    double X,
    double Y,
    double SizePercent,
    double TransparencyPercent,
    double BrightnessPercent,
    bool Visible,
    Guid? SpriteOwnerId,
    int SpriteIndex,
    int ImageSpeed,
    List<RuntimeCursorSnapshot> Cursors);

internal sealed record RuntimeSoundSnapshot(string Path, bool Loop, double PositionSeconds);

internal sealed record RuntimeSceneSnapshot(
    Guid SceneId,
    Dictionary<string, string> Variables,
    List<RuntimeObjectSnapshot> Objects,
    List<RuntimeSoundSnapshot> Sounds);

internal sealed partial class ProjectRuntime
{
    public async Task RunSceneAsync(
        CompiledProject project,
        IskraScene scene,
        IskraProject sourceProject,
        FrameworkElement sceneSurface,
        Action<string, string>? output = null,
        Action<IReadOnlyList<RuntimeDebugVariable>>? variablesUpdated = null,
        CancellationToken cancellationToken = default)
    {
        var run = new ProjectRun(this, project, sourceProject, sceneSurface, output, variablesUpdated, cancellationToken);
        run.Prepare();
        try
        {
            IskraScene? currentScene = scene;
            var resume = false;
            while (currentScene is not null && !cancellationToken.IsCancellationRequested)
            {
                var compiledScene = project.Scenes.FirstOrDefault(candidate => candidate.Id == currentScene.Id);
                if (compiledScene is null)
                {
                    break;
                }
                var execution = new SceneExecution(run, compiledScene, currentScene,
                    resume ? run.LoadSnapshot(currentScene.Id) : null);
                SceneSwitchRequest? request = null;
                try
                {
                    request = await execution.RunAsync();
                    run.SaveSnapshot(currentScene.Id, execution.CaptureSnapshot());
                }
                finally
                {
                    execution.Dispose();
                }
                if (request is not { } target || run.Token.IsCancellationRequested)
                {
                    break;
                }
                resume = target.Resume;
                currentScene = sourceProject.Scenes.FirstOrDefault(candidate => candidate.Id == target.SceneId);
            }
        }
        finally
        {
            run.Dispose();
        }
    }

    private sealed class ProjectRun
    {
        private readonly Dictionary<string, (string SceneName, string ObjectName)> declarations = new(StringComparer.Ordinal);
        private readonly HashSet<Guid> visitedScenes = [];
        private readonly string snapshotDirectory;

        public ProjectRun(
            ProjectRuntime owner,
            CompiledProject project,
            IskraProject sourceProject,
            FrameworkElement surface,
            Action<string, string>? output,
            Action<IReadOnlyList<RuntimeDebugVariable>>? variablesUpdated,
            CancellationToken token)
        {
            Owner = owner;
            Project = project;
            SourceProject = sourceProject;
            Surface = surface;
            Output = output;
            VariablesUpdated = variablesUpdated;
            Token = token;
            Orientation = project.Orientation;
            snapshotDirectory = Path.Combine(owner.GetProjectCacheDirectory(sourceProject), "SceneSnapshots");
        }

        public ProjectRuntime Owner { get; }
        public CompiledProject Project { get; }
        public IskraProject SourceProject { get; }
        public FrameworkElement Surface { get; }
        public Action<string, string>? Output { get; }
        public Action<IReadOnlyList<RuntimeDebugVariable>>? VariablesUpdated { get; }
        public CancellationToken Token { get; }
        public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, double> SoundVolumes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public double GlobalVolume { get; set; } = 100d;
        public ProjectOrientation Orientation { get; set; }

        public void Prepare()
        {
            foreach (var scene in Project.Scenes)
            {
                foreach (var item in scene.Objects)
                {
                    foreach (var command in item.Commands)
                    {
                        var assignmentIndex = command.Argument.IndexOf('=');
                        var name = command.Kind switch
                        {
                            "set-value" when assignmentIndex > 0 => command.Argument[..assignmentIndex].Trim(),
                            "ask" => LastToken(command.Argument),
                            _ => string.Empty
                        };
                        if (name.Length == 0)
                        {
                            continue;
                        }
                        Variables.TryAdd(name, "0");
                        declarations.TryAdd(name, (scene.Name, item.Name));
                    }
                }
            }
            NotifyVariables();
        }

        public void NotifyVariables()
        {
            if (VariablesUpdated is null)
            {
                return;
            }
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
            VariablesUpdated(rows);
        }

        public void ApplySurfaceOrientation()
        {
            var horizontal = Orientation == ProjectOrientation.Horizontal;
            Surface.Width = horizontal ? 1920d : 1080d;
            Surface.Height = horizontal ? 1080d : 1920d;
        }

        public void SaveSnapshot(Guid sceneId, RuntimeSceneSnapshot snapshot)
        {
            visitedScenes.Add(sceneId);
            try
            {
                Directory.CreateDirectory(snapshotDirectory);
                File.WriteAllText(Path.Combine(snapshotDirectory, sceneId.ToString("N") + ".json"),
                    JsonSerializer.Serialize(snapshot));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                AppLog.Write("Не удалось сохранить снапшот сцены.", exception);
            }
        }

        public RuntimeSceneSnapshot? LoadSnapshot(Guid sceneId)
        {
            if (!visitedScenes.Contains(sceneId))
            {
                return null;
            }
            try
            {
                var path = Path.Combine(snapshotDirectory, sceneId.ToString("N") + ".json");
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<RuntimeSceneSnapshot>(File.ReadAllText(path))
                    : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                AppLog.Write("Не удалось прочитать снапшот сцены.", exception);
                return null;
            }
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(snapshotDirectory))
                {
                    Directory.Delete(snapshotDirectory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Write("Не удалось удалить временные снапшоты сцен.", exception);
            }
        }

        private static string LastToken(string argument)
        {
            var separator = argument.LastIndexOf(' ');
            return separator < 0 ? string.Empty : argument[(separator + 1)..].Trim();
        }
    }

    private sealed class SceneExecution
    {
        private readonly ProjectRun run;
        private readonly CompiledScene compiledScene;
        private readonly IskraScene scene;
        private readonly RuntimeSceneSnapshot? restore;
        private readonly List<ObjectRuntime> objects = [];
        private readonly Dictionary<Guid, ObjectRuntime> byId = [];
        private readonly Dictionary<Guid, List<string>> imageLists = new();
        private readonly Dictionary<(Guid DestinationId, Guid ResourceOwnerId), int> imageIndices = [];
        private readonly List<RuntimeSound> sounds = [];
        private readonly List<RuntimeSoundSnapshot> pendingSounds = [];
        private readonly List<Action> eventSubscriptions = [];
        private readonly CancellationTokenSource sceneCancellation;
        private readonly CancellationToken token;
        private readonly bool usesTouched;
        private SceneSwitchRequest? request;
        private int askDepth;

        public SceneExecution(ProjectRun run, CompiledScene compiledScene, IskraScene scene, RuntimeSceneSnapshot? restore)
        {
            this.run = run;
            this.compiledScene = compiledScene;
            this.scene = scene;
            this.restore = restore;
            sceneCancellation = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
            token = sceneCancellation.Token;
            usesTouched = compiledScene.Objects.Any(item => item.Commands.Any(ReferencesTouched));
        }

        public async Task<SceneSwitchRequest?> RunAsync()
        {
            Build();
            ApplyRestore();
            WireEvents();
            StartBlocks();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            return run.Token.IsCancellationRequested ? null : request;
        }

        public RuntimeSceneSnapshot CaptureSnapshot()
        {
            var objectSnapshots = new List<RuntimeObjectSnapshot>();
            foreach (var runtime in objects)
            {
                var spriteKey = imageIndices.Keys.FirstOrDefault(key => key.DestinationId == runtime.Item.Id);
                var spriteOwnerId = (Guid?)null;
                var spriteIndex = -1;
                if (imageIndices.TryGetValue(spriteKey, out var index))
                {
                    spriteOwnerId = spriteKey.ResourceOwnerId;
                    spriteIndex = index;
                }
                objectSnapshots.Add(new RuntimeObjectSnapshot(
                    runtime.Item.Id,
                    runtime.Position?.X ?? 0d,
                    runtime.Position?.Y ?? 0d,
                    (runtime.Size?.ScaleX ?? 1d) * 100d,
                    runtime.Target is { } target ? (1d - target.Opacity) * 100d : 0d,
                    runtime.Brightness,
                    runtime.Visible,
                    spriteOwnerId,
                    spriteIndex,
                    runtime.ImageSpeed,
                    runtime.Cursors.Select(cursor => new RuntimeCursorSnapshot(cursor.Start, cursor.End, cursor.Index)).ToList()));
            }
            var soundSnapshots = sounds
                .Where(sound => !sound.Ended)
                .Select(sound => new RuntimeSoundSnapshot(sound.Path, sound.Loop, SoundPosition(sound)))
                .ToList();
            return new RuntimeSceneSnapshot(
                scene.Id,
                new Dictionary<string, string>(run.Variables, StringComparer.Ordinal),
                objectSnapshots,
                soundSnapshots);
        }

        public void Dispose()
        {
            sceneCancellation.Cancel();
            foreach (var unsubscribe in eventSubscriptions)
            {
                unsubscribe();
            }
            eventSubscriptions.Clear();
            foreach (var runtime in objects)
            {
                foreach (var timer in runtime.Timers)
                {
                    timer.Stop();
                }
                runtime.Timers.Clear();
                if (runtime.ImageTimer is { } imageTimer)
                {
                    imageTimer.Stop();
                    runtime.ImageTimer = null;
                }
            }
            foreach (var sound in sounds)
            {
                sound.Player.Stop();
                sound.Player.Close();
            }
            sounds.Clear();
            sceneCancellation.Dispose();
        }

        private static bool ReferencesTouched(RuntimeCommand command) =>
            command.Argument.Contains(RuntimeObjectProperties.TouchedObjectPropertyPrefix, StringComparison.OrdinalIgnoreCase) ||
            command.Condition?.Contains(RuntimeObjectProperties.TouchedObjectPropertyPrefix, StringComparison.OrdinalIgnoreCase) == true;

        private void Build()
        {
            run.ApplySurfaceOrientation();
            foreach (var item in compiledScene.Objects)
            {
                var resourceDirectory = run.Owner.ContentStore.GetObjectResourceDirectory(run.SourceProject, scene, item.Id, "sprites");
                imageLists[item.Id] = ProjectContentStore.ListResourceFiles(resourceDirectory, ProjectContentStore.ImageExtensions);
            }
            var panel = run.Surface as Panel;
            panel?.Children.Clear();

            var backgroundImages = imageLists.GetValueOrDefault(IskraObject.BackgroundId);
            var backgroundSource = backgroundImages is { Count: > 0 } ? ProjectRuntime.LoadBitmap(backgroundImages[0]) : null;
            var background = new Image
            {
                Source = backgroundSource,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = true
            };
            var backgroundScale = new ScaleTransform();
            background.RenderTransform = backgroundScale;
            background.RenderTransformOrigin = new Point(0.5, 0.5);
            panel?.Children.Add(background);
            Panel.SetZIndex(background, 1);
            var backgroundRuntime = new ObjectRuntime(FindObject(IskraObject.BackgroundId), background, backgroundScale, null)
            {
                OriginalSource = backgroundSource
            };
            objects.Add(backgroundRuntime);
            byId[IskraObject.BackgroundId] = backgroundRuntime;

            foreach (var item in compiledScene.Objects.Where(candidate => candidate.Id != IskraObject.BackgroundId))
            {
                var images = imageLists[item.Id];
                var scale = new ScaleTransform();
                var translation = new TranslateTransform();
                var transform = new TransformGroup();
                transform.Children.Add(scale);
                transform.Children.Add(translation);
                var source = images.Count == 0 ? null : ProjectRuntime.LoadBitmap(images[0]);
                var image = new Image
                {
                    Source = source,
                    Stretch = Stretch.None,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    RenderTransform = transform,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    IsHitTestVisible = true
                };
                panel?.Children.Add(image);
                Panel.SetZIndex(image, 2);
                var runtime = new ObjectRuntime(item, image, scale, translation)
                {
                    OriginalSource = source
                };
                objects.Add(runtime);
                byId[item.Id] = runtime;
            }
        }

        private void ApplyRestore()
        {
            if (restore is null)
            {
                return;
            }
            run.Variables.Clear();
            foreach (var pair in restore.Variables)
            {
                run.Variables[pair.Key] = pair.Value;
            }
            run.NotifyVariables();
            foreach (var saved in restore.Objects)
            {
                if (!byId.TryGetValue(saved.Id, out var runtime))
                {
                    continue;
                }
                if (runtime.Position is { } position)
                {
                    position.X = saved.X;
                    position.Y = saved.Y;
                }
                if (runtime.Size is { } size)
                {
                    size.ScaleX = saved.SizePercent / 100d;
                    size.ScaleY = saved.SizePercent / 100d;
                }
                if (runtime.Target is { } target)
                {
                    target.Opacity = 1d - saved.TransparencyPercent / 100d;
                }
                runtime.Brightness = saved.BrightnessPercent;
                runtime.Properties.SetAxisPosition("x", saved.X.ToString("R", CultureInfo.InvariantCulture));
                runtime.Properties.SetAxisPosition("y", saved.Y.ToString("R", CultureInfo.InvariantCulture));
                runtime.Properties.SetSize(saved.SizePercent);
                runtime.Properties.SetTransparency(saved.TransparencyPercent);
                SetVisible(runtime, saved.Visible);
                if (saved.SpriteOwnerId is { } ownerId && saved.SpriteIndex >= 0)
                {
                    ApplyImage(runtime, ownerId, saved.SpriteIndex);
                }
                else if (runtime.OriginalSource is { } original && runtime.Target is { } sprite)
                {
                    sprite.Source = SpriteBrightness.Apply(original, saved.BrightnessPercent);
                }
                runtime.PendingCursors.AddRange(saved.Cursors);
                runtime.PendingImageSpeed = saved.ImageSpeed;
            }
            pendingSounds.AddRange(restore.Sounds);
        }

        private void WireEvents()
        {
            if (Window.GetWindow(run.Surface) is { } window)
            {
                KeyEventHandler keyDown = (_, e) => HandleKeyDown(e);
                KeyEventHandler keyUp = (_, e) => HandleKeyUp(e);
                window.KeyDown += keyDown;
                window.KeyUp += keyUp;
                eventSubscriptions.Add(() =>
                {
                    window.KeyDown -= keyDown;
                    window.KeyUp -= keyUp;
                });
            }

            foreach (var runtime in objects)
            {
                if (runtime.Target is not Image target)
                {
                    continue;
                }
                var hasPressEvent = runtime.EventBlocks.Any(block => block.EventKind == "press");
                var hasReleaseEvent = runtime.EventBlocks.Any(block => block.EventKind == "release");
                var usesPressedProperty = UsesProperty(runtime, RuntimeObjectProperties.PressedProperty);
                var usesHoveredProperty = UsesProperty(runtime, RuntimeObjectProperties.HoveredProperty);
                if (hasPressEvent || usesPressedProperty)
                {
                    MouseButtonEventHandler pressHandler = (_, e) =>
                    {
                        if (!IsOpaqueAt(runtime, e)) return;
                        if (hasPressEvent) _ = RunEventSafe(runtime, "press");
                        if (usesPressedProperty)
                        {
                            if (runtime.Properties.SetPressed(true)) RefreshConditionEvents(runtime);
                            target.CaptureMouse();
                        }
                    };
                    target.MouseLeftButtonDown += pressHandler;
                    eventSubscriptions.Add(() => target.MouseLeftButtonDown -= pressHandler);
                }
                if (hasReleaseEvent || usesPressedProperty)
                {
                    MouseButtonEventHandler releaseHandler = (_, e) =>
                    {
                        if (runtime.Properties.SetPressed(false)) RefreshConditionEvents(runtime);
                        if (hasReleaseEvent && IsOpaqueAt(runtime, e)) _ = RunEventSafe(runtime, "release");
                        if (target.IsMouseCaptured) target.ReleaseMouseCapture();
                    };
                    target.MouseLeftButtonUp += releaseHandler;
                    eventSubscriptions.Add(() => target.MouseLeftButtonUp -= releaseHandler);
                    eventSubscriptions.Add(() =>
                    {
                        if (target.IsMouseCaptured) target.ReleaseMouseCapture();
                    });
                }
                if (runtime.EventBlocks.Any(block => block.EventKind == "hover"))
                {
                    var wasOnOpaquePixel = false;
                    MouseEventHandler moveHandler = (_, e) =>
                    {
                        var isOnOpaquePixel = IsOpaqueAt(runtime, e);
                        if (isOnOpaquePixel && !wasOnOpaquePixel) _ = RunEventSafe(runtime, "hover");
                        wasOnOpaquePixel = isOnOpaquePixel;
                    };
                    MouseEventHandler leaveHandler = (_, _) => wasOnOpaquePixel = false;
                    target.MouseMove += moveHandler;
                    target.MouseLeave += leaveHandler;
                    eventSubscriptions.Add(() =>
                    {
                        target.MouseMove -= moveHandler;
                        target.MouseLeave -= leaveHandler;
                    });
                }
                if (usesHoveredProperty)
                {
                    MouseEventHandler moveHandler = (_, e) =>
                    {
                        if (runtime.Properties.SetHovered(FindTopmostOpaqueObject(e) == runtime.Item.Id)) RefreshConditionEvents(runtime);
                    };
                    MouseEventHandler leaveHandler = (_, _) =>
                    {
                        if (runtime.Properties.SetHovered(false)) RefreshConditionEvents(runtime);
                    };
                    run.Surface.PreviewMouseMove += moveHandler;
                    run.Surface.MouseLeave += leaveHandler;
                    eventSubscriptions.Add(() =>
                    {
                        run.Surface.PreviewMouseMove -= moveHandler;
                        run.Surface.MouseLeave -= leaveHandler;
                    });
                }
            }

            if (objects.Any(runtime => runtime.EventBlocks.Any(block => block.EventKind == "frame")) || usesTouched)
            {
                EventHandler frameHandler = (_, _) => FrameTick();
                CompositionTarget.Rendering += frameHandler;
                eventSubscriptions.Add(() => CompositionTarget.Rendering -= frameHandler);
            }
        }

        private void StartBlocks()
        {
            foreach (var runtime in objects)
            {
                foreach (var block in runtime.EventBlocks)
                {
                    if (block.EventKind != "every") continue;
                    if (!int.TryParse(block.Condition, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds < 1) continue;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                    timer.Tick += (_, _) => RunTimedBlock(runtime, block);
                    runtime.Timers.Add(timer);
                    timer.Start();
                }
                if (runtime.PendingImageSpeed > 0)
                {
                    StartImageCycling(runtime, runtime.PendingImageSpeed);
                }
            }

            foreach (var saved in pendingSounds)
            {
                StartSound(saved.Path, saved.Loop, saved.PositionSeconds);
            }

            foreach (var runtime in objects)
            {
                foreach (var cursor in runtime.PendingCursors)
                {
                    runtime.ConditionStates[cursor.Start] = true;
                }
                RefreshConditionEvents(runtime);
                if (restore is null)
                {
                    _ = RunEventSafe(runtime, "start");
                }
                else
                {
                    foreach (var cursor in runtime.PendingCursors)
                    {
                        _ = RunResumedBlockAsync(runtime, cursor);
                    }
                }
            }
        }

        private async Task ExecuteCommandsAsync(ObjectRuntime runtime, int start, int end, int firstIndex = -1, bool resumed = false)
        {
            var item = runtime.Item;
            var first = firstIndex >= 0 ? firstIndex : start;
            var cursor = new ScriptCursor(start, end) { Index = first };
            if (token.IsCancellationRequested)
            {
                return;
            }
            runtime.Cursors.Add(cursor);
            try
            {
                for (var commandIndex = first; commandIndex < end; commandIndex++)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                    if (resumed && commandIndex == first && item.Commands[commandIndex].Kind == "wait")
                    {
                        continue;
                    }
                    cursor.Index = commandIndex;
                    var command = item.Commands[commandIndex];
                    switch (command.Kind)
                    {
                        case "branch-false":
                            if (!ConditionalDirectiveParser.Evaluate(command.Argument, runtime.CurrentValues(run)))
                            {
                                commandIndex = command.Target - 1;
                            }
                            break;
                        case "jump":
                            commandIndex = command.Target - 1;
                            break;
                        case "loop-jump":
                            await Task.Delay(1, token);
                            commandIndex = command.Target - 1;
                            break;
                        case "set-image":
                            var imageTarget = FindRuntime(command.TargetObjectId ?? item.Id);
                            var imageOwner = command.ResourceOwnerId ?? item.Id;
                            if (imageTarget is not null && TryResolveSprite(imageOwner, command.Argument, out var imagePath) &&
                                imageLists.GetValueOrDefault(imageOwner) is { } ownerImages)
                            {
                                ApplyImage(imageTarget, imageOwner, ownerImages.IndexOf(imagePath));
                            }
                            break;
                        case "step-image":
                            var stepTarget = FindRuntime(command.TargetObjectId ?? item.Id);
                            var stepOwner = command.ResourceOwnerId ?? item.Id;
                            if (stepTarget is not null && imageLists.GetValueOrDefault(stepOwner) is { Count: > 0 } stepImages)
                            {
                                var indexKey = (stepTarget.Item.Id, stepOwner);
                                var currentIndex = imageIndices.GetValueOrDefault(indexKey);
                                currentIndex = (currentIndex + (command.Argument == "назад" ? -1 : 1) + stepImages.Count) % stepImages.Count;
                                ApplyImage(stepTarget, stepOwner, currentIndex);
                            }
                            break;
                        case "set-transparency":
                            if (runtime.Target is { } transparencyTarget &&
                                double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var transparency))
                            {
                                transparencyTarget.Opacity = 1d - transparency / 100d;
                                if (runtime.Properties.SetTransparency(transparency)) RefreshConditionEvents(runtime);
                            }
                            break;
                        case "change-transparency":
                            if (runtime.Target is { } adjustedTransparencyTarget &&
                                double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var transparencyChange))
                            {
                                var currentTransparency = (1d - adjustedTransparencyTarget.Opacity) * 100d;
                                var adjustedTransparency = Math.Clamp(currentTransparency + transparencyChange, 0d, 100d);
                                adjustedTransparencyTarget.Opacity = 1d - adjustedTransparency / 100d;
                                if (runtime.Properties.SetTransparency(adjustedTransparency)) RefreshConditionEvents(runtime);
                            }
                            break;
                        case "set-size":
                            if (runtime.Size is { } sizeTransform &&
                                double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
                            {
                                sizeTransform.ScaleX = size / 100d;
                                sizeTransform.ScaleY = size / 100d;
                                if (runtime.Properties.SetSize(size)) RefreshConditionEvents(runtime);
                            }
                            break;
                        case "change-size":
                            if (runtime.Size is { } adjustedSizeTransform &&
                                double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var sizeChange))
                            {
                                var adjustedSize = SpriteSizeSyntax.ApplyChange(adjustedSizeTransform.ScaleX * 100d, sizeChange);
                                adjustedSizeTransform.ScaleX = adjustedSize / 100d;
                                adjustedSizeTransform.ScaleY = adjustedSize / 100d;
                                if (runtime.Properties.SetSize(adjustedSize)) RefreshConditionEvents(runtime);
                            }
                            break;
                        case "set-brightness":
                            if (double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var brightness))
                            {
                                runtime.Brightness = brightness;
                                if (runtime.Target is { } brightnessTarget && runtime.OriginalSource is { } brightnessSource)
                                {
                                    brightnessTarget.Source = SpriteBrightness.Apply(brightnessSource, brightness);
                                }
                            }
                            break;
                        case "change-brightness":
                            if (double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var brightnessChange))
                            {
                                var adjustedBrightness = Math.Clamp(runtime.Brightness + brightnessChange, 0d, 100d);
                                runtime.Brightness = adjustedBrightness;
                                if (runtime.Target is { } adjustedBrightnessTarget && runtime.OriginalSource is { } adjustedBrightnessSource)
                                {
                                    adjustedBrightnessTarget.Source = SpriteBrightness.Apply(adjustedBrightnessSource, adjustedBrightness);
                                }
                            }
                            break;
                        case "place":
                            var coordinates = command.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (coordinates.Length == 2 && int.TryParse(coordinates[0], out var x) && int.TryParse(coordinates[1], out var y) &&
                                runtime.Position is { } translation)
                            {
                                translation.X = x;
                                translation.Y = y;
                                if (runtime.Properties.SetPosition(x, y)) RefreshConditionEvents(runtime);
                            }
                            break;
                        case "move-by":
                            var moveParts = command.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (moveParts.Length == 2 &&
                                double.TryParse(moveParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var moveX) &&
                                double.TryParse(moveParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var moveY) &&
                                runtime.Position is { } movePosition)
                            {
                                MoveRuntime(runtime, movePosition.X + moveX, movePosition.Y + moveY);
                            }
                            break;
                        case "set-position":
                        case "change-position":
                            var axisParts = command.Argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                            if (axisParts.Length == 2 && runtime.Position is { } axisPosition &&
                                double.TryParse(ProjectRuntime.ResolveValue(axisParts[1], runtime.CurrentValues(run)), NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out var axisValue))
                            {
                                var axis = axisParts[0].Trim().ToLowerInvariant();
                                var target = command.Kind == "set-position"
                                    ? axisValue
                                    : (axis == "y" ? axisPosition.Y : axisPosition.X) + axisValue;
                                MoveRuntime(runtime, axis == "y" ? axisPosition.X : target, axis == "y" ? target : axisPosition.Y);
                            }
                            break;
                        case "show":
                        case "hide":
                            if (FindRuntime(command.TargetObjectId ?? item.Id) is { } visibilityTarget)
                            {
                                SetVisible(visibilityTarget, command.Kind == "show");
                            }
                            break;
                        case "launch-scene":
                        case "resume-scene":
                            cursor.Index = commandIndex + 1;
                            RequestSceneSwitch(command.Argument, command.Kind == "resume-scene");
                            return;
                        case "set-orientation":
                            run.Orientation = ResolveOrientation(run.Orientation, command.Argument);
                            run.ApplySurfaceOrientation();
                            break;
                        case "wait":
                            await Task.Delay(TimeSpan.FromMilliseconds(long.Parse(command.Argument)), token);
                            break;
                        case "play-sound":
                        case "play-sound-wait":
                            if (ProjectRuntime.TryResolveSound(command.Argument, item, compiledScene, run.SourceProject,
                                    run.Owner.ContentStore, runtime.CurrentValues(run), out var soundPath))
                            {
                                var sound = StartSound(soundPath, loop: false, positionSeconds: 0d);
                                if (command.Kind == "play-sound-wait")
                                {
                                    await sound.Completion.Task.WaitAsync(token);
                                }
                            }
                            break;
                        case "loop-sound":
                            if (ProjectRuntime.TryResolveSound(command.Argument, item, compiledScene, run.SourceProject,
                                    run.Owner.ContentStore, runtime.CurrentValues(run), out var loopPath))
                            {
                                StartSound(loopPath, loop: true, positionSeconds: 0d);
                            }
                            break;
                        case "stop-sound":
                            if (ProjectRuntime.TryResolveSound(command.Argument, item, compiledScene, run.SourceProject,
                                    run.Owner.ContentStore, runtime.CurrentValues(run), out var stopPath))
                            {
                                StopSounds(stopPath);
                            }
                            break;
                        case "set-volume":
                            if (double.TryParse(command.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var volume))
                            {
                                run.GlobalVolume = Math.Clamp(volume, 0d, 100d);
                                ApplyVolumes();
                            }
                            break;
                        case "set-sound-volume":
                            var volumeParts = command.Argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                            if (volumeParts.Length == 2 &&
                                double.TryParse(volumeParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var soundVolume) &&
                                ProjectRuntime.TryResolveSound(volumeParts[1], item, compiledScene, run.SourceProject,
                                    run.Owner.ContentStore, runtime.CurrentValues(run), out var volumePath))
                            {
                                run.SoundVolumes[volumePath] = Math.Clamp(soundVolume, 0d, 100d);
                                foreach (var activeSound in sounds.Where(candidate => string.Equals(candidate.Path, volumePath, StringComparison.OrdinalIgnoreCase)))
                                {
                                    ApplyVolume(activeSound);
                                }
                            }
                            break;
                        case "play-image":
                            if (FindRuntime(command.TargetObjectId ?? item.Id) is { } cyclingTarget &&
                                int.TryParse(command.Argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var imageSpeed))
                            {
                                StartImageCycling(cyclingTarget, imageSpeed);
                            }
                            break;
                        case "stop-image":
                            StopImageCycling(runtime);
                            break;
                        case "set-value":
                            var assignmentIndex = command.Argument.IndexOf('=');
                            var variableName = command.Argument[..assignmentIndex].Trim();
                            run.Variables[variableName] = ProjectRuntime.ResolveValue(command.Argument[(assignmentIndex + 1)..].Trim(), runtime.CurrentValues(run));
                            run.NotifyVariables();
                            RefreshAllConditions();
                            break;
                        case "change-value":
                            var changeParts = command.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (changeParts.Length == 2 && long.TryParse(changeParts[1], out var change) &&
                                long.TryParse(run.Variables.GetValueOrDefault(changeParts[0]), out var current))
                            {
                                run.Variables[changeParts[0]] = (current + change).ToString(CultureInfo.InvariantCulture);
                                run.NotifyVariables();
                                RefreshAllConditions();
                            }
                            break;
                        case "print":
                            run.Output?.Invoke(item.Name, ProjectRuntime.ResolveValue(command.Argument, runtime.CurrentValues(run)));
                            break;
                        case "ask":
                            var askSeparator = command.Argument.LastIndexOf(' ');
                            if (askSeparator > 0)
                            {
                                var askQuestion = command.Argument[..askSeparator].Trim();
                                var askVariable = command.Argument[(askSeparator + 1)..].Trim();
                                var answer = await AskAsync(askQuestion);
                                if (answer is null)
                                {
                                    return;
                                }
                                run.Variables[askVariable] = answer;
                                run.NotifyVariables();
                                RefreshAllConditions();
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

        private void RequestSceneSwitch(string sceneName, bool resume)
        {
            var target = run.Project.Scenes.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, sceneName, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                return;
            }
            request = new SceneSwitchRequest(target.Id, resume);
            sceneCancellation.Cancel();
        }

        private static ProjectOrientation ResolveOrientation(ProjectOrientation current, string mode) => mode switch
        {
            "horizontal" => ProjectOrientation.Horizontal,
            "vertical" => ProjectOrientation.Vertical,
            _ => current == ProjectOrientation.Horizontal ? ProjectOrientation.Vertical : ProjectOrientation.Horizontal
        };

        private void RunTimedBlock(ObjectRuntime runtime, ScriptEventBlock block)
        {
            if (token.IsCancellationRequested) return;
            if (runtime.Cursors.Any(cursor => cursor.Start == block.Start)) return;
            _ = RunBlockSafe(runtime, block.Start, block.End, "Каждые");
        }

        private void FrameTick()
        {
            if (token.IsCancellationRequested) return;
            try
            {
                foreach (var runtime in objects)
                {
                    foreach (var block in runtime.EventBlocks)
                    {
                        if (block.EventKind != "frame") continue;
                        if (runtime.Cursors.Any(cursor => cursor.Start == block.Start)) continue;
                        _ = RunBlockSafe(runtime, block.Start, block.End, "Каждый кадр");
                    }
                }
                if (usesTouched)
                {
                    UpdateTouches();
                }
            }
            catch (Exception exception)
            {
                AppLog.Write($"Ошибка обновления кадра сцены «{scene.Name}».", exception);
            }
        }

        private void UpdateTouches()
        {
            var width = run.Surface.Width;
            var height = run.Surface.Height;
            foreach (var runtime in objects)
            {
                runtime.Bounds = SpriteBounds(runtime, width, height);
            }
            foreach (var runtime in objects)
            {
                if (runtime.IsBackground) continue;
                var changed = false;
                foreach (var other in objects)
                {
                    if (other.IsBackground || ReferenceEquals(other, runtime)) continue;
                    changed |= runtime.Properties.SetTouched(other.Item.Name, Overlaps(runtime, other));
                }
                if (changed) RefreshConditionEvents(runtime);
            }
        }

        private static bool Overlaps(ObjectRuntime first, ObjectRuntime second) =>
            first.Visible && second.Visible && first.Bounds is { } firstBounds && second.Bounds is { } secondBounds &&
            firstBounds.Left < secondBounds.Right && secondBounds.Left < firstBounds.Right &&
            firstBounds.Top < secondBounds.Bottom && secondBounds.Top < firstBounds.Bottom;

        private static Rect? SpriteBounds(ObjectRuntime runtime, double width, double height)
        {
            if (runtime.Target?.Source is not BitmapSource bitmap) return null;
            var scaleX = runtime.Size?.ScaleX ?? 1d;
            var scaleY = runtime.Size?.ScaleY ?? 1d;
            var elementWidth = runtime.Target.ActualWidth > 0 ? runtime.Target.ActualWidth : bitmap.Width;
            var elementHeight = runtime.Target.ActualHeight > 0 ? runtime.Target.ActualHeight : bitmap.Height;
            var objectWidth = elementWidth * scaleX;
            var objectHeight = elementHeight * scaleY;
            var centerX = width / 2d + (runtime.Position?.X ?? 0d);
            var centerY = height / 2d + (runtime.Position?.Y ?? 0d);
            return new Rect(centerX - objectWidth / 2d, centerY - objectHeight / 2d, objectWidth, objectHeight);
        }

        private void HandleKeyDown(KeyEventArgs e)
        {
            if (askDepth > 0 || e.IsRepeat) return;
            var keyId = KeyboardKeySyntax.NormalizeKey(e.Key);
            if (keyId is null) return;
            SetKeyState(keyId, true);
            foreach (var runtime in objects)
            {
                var matching = runtime.EventBlocks
                    .Where(block => block.EventKind == "key" && block.Condition is { } condition &&
                        KeyboardKeySyntax.TryNormalize(condition, out var blockKey) && blockKey == keyId)
                    .ToArray();
                if (matching.Length == 0) continue;
                _ = RunKeyBlocksSafe(runtime, matching, keyId);
            }
        }

        private void HandleKeyUp(KeyEventArgs e)
        {
            if (askDepth > 0) return;
            var keyId = KeyboardKeySyntax.NormalizeKey(e.Key);
            if (keyId is null) return;
            SetKeyState(keyId, false);
        }

        private void SetKeyState(string keyId, bool pressed)
        {
            foreach (var runtime in objects)
            {
                if (runtime.Properties.SetKeyboardKey(keyId, pressed))
                {
                    RefreshConditionEvents(runtime);
                }
            }
        }

        private async Task RunKeyBlocksSafe(ObjectRuntime runtime, ScriptEventBlock[] blocks, string keyId)
        {
            try
            {
                foreach (var block in blocks)
                {
                    await ExecuteCommandsAsync(runtime, block.Start, block.End);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                AppLog.Write($"Ошибка выполнения события «клавиша {keyId}» объекта «{runtime.Item.Name}».", exception);
            }
        }

        private async Task RunEventSafe(ObjectRuntime runtime, string eventKind)
        {
            var matching = runtime.EventBlocks.Where(block => block.EventKind == eventKind).ToArray();
            if (matching.Length == 0) return;
            try
            {
                if (eventKind == "start")
                {
                    foreach (var block in matching)
                    {
                        _ = RunBlockSafe(runtime, block.Start, block.End, "start");
                    }
                    return;
                }
                foreach (var block in matching)
                {
                    await ExecuteCommandsAsync(runtime, block.Start, block.End);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                AppLog.Write($"Ошибка выполнения события «{eventKind}» объекта «{runtime.Item.Name}».", exception);
            }
        }

        private async Task RunBlockSafe(ObjectRuntime runtime, int start, int end, string label)
        {
            try
            {
                await ExecuteCommandsAsync(runtime, start, end);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                AppLog.Write($"Ошибка выполнения события «{label}» объекта «{runtime.Item.Name}», блок {start}.", exception);
            }
        }

        private async Task RunResumedBlockAsync(ObjectRuntime runtime, RuntimeCursorSnapshot saved)
        {
            try
            {
                await ExecuteCommandsAsync(runtime, saved.Start, saved.End, saved.Index, resumed: true);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                AppLog.Write($"Ошибка продолжения сценария объекта «{runtime.Item.Name}».", exception);
            }
        }

        private void RefreshConditionEvents(ObjectRuntime runtime)
        {
            var values = runtime.CurrentValues(run);
            foreach (var block in runtime.EventBlocks.Where(block => block.EventKind == "when"))
            {
                bool conditionIsTrue;
                try
                {
                    conditionIsTrue = ConditionalDirectiveParser.Evaluate(block.Condition!, values);
                }
                catch (Exception exception)
                {
                    AppLog.Write($"Ошибка проверки условия «{block.Condition}» объекта «{runtime.Item.Name}», строка {runtime.Item.Commands[block.Start - 1].Line}.", exception);
                    continue;
                }
                var wasTrue = runtime.ConditionStates.GetValueOrDefault(block.Start);
                runtime.ConditionStates[block.Start] = conditionIsTrue;
                if (conditionIsTrue && !wasTrue)
                {
                    _ = RunBlockSafe(runtime, block.Start, block.End, "Когда");
                }
            }
        }

        private void RefreshAllConditions()
        {
            foreach (var runtime in objects)
            {
                RefreshConditionEvents(runtime);
            }
        }

        private void MoveRuntime(ObjectRuntime runtime, double x, double y)
        {
            if (runtime.Position is not { } position) return;
            position.X = x;
            position.Y = y;
            var changed = runtime.Properties.SetAxisPosition("x", x.ToString("R", CultureInfo.InvariantCulture));
            changed |= runtime.Properties.SetAxisPosition("y", y.ToString("R", CultureInfo.InvariantCulture));
            if (changed) RefreshConditionEvents(runtime);
        }

        private void SetVisible(ObjectRuntime runtime, bool visible)
        {
            runtime.Visible = visible;
            if (runtime.Target is not { } target) return;
            target.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
            if (!visible && target.IsMouseCaptured)
            {
                target.ReleaseMouseCapture();
            }
        }

        private void ApplyImage(ObjectRuntime target, Guid ownerId, int index)
        {
            if (index < 0 || imageLists.GetValueOrDefault(ownerId) is not { } list || index >= list.Count) return;
            imageIndices[(target.Item.Id, ownerId)] = index;
            var source = ProjectRuntime.LoadBitmap(list[index]);
            target.OriginalSource = source;
            if (target.Target is { } image)
            {
                image.Source = SpriteBrightness.Apply(source, target.Brightness);
            }
        }

        private void StartImageCycling(ObjectRuntime runtime, int speed)
        {
            StopImageCycling(runtime);
            if (speed < 1) return;
            runtime.ImageSpeed = speed;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000d / speed) };
            timer.Tick += (_, _) => StepCycledImage(runtime);
            runtime.ImageTimer = timer;
            timer.Start();
        }

        private void StopImageCycling(ObjectRuntime runtime)
        {
            runtime.ImageSpeed = 0;
            if (runtime.ImageTimer is { } timer)
            {
                timer.Stop();
                runtime.ImageTimer = null;
            }
        }

        private void StepCycledImage(ObjectRuntime runtime)
        {
            if (token.IsCancellationRequested) return;
            if (imageLists.GetValueOrDefault(runtime.Item.Id) is not { Count: > 0 } list) return;
            var index = (imageIndices.GetValueOrDefault((runtime.Item.Id, runtime.Item.Id)) + 1) % list.Count;
            ApplyImage(runtime, runtime.Item.Id, index);
        }

        private RuntimeSound StartSound(string path, bool loop, double positionSeconds)
        {
            if (loop)
            {
                var existing = sounds.FirstOrDefault(sound => !sound.Ended && sound.Loop &&
                    string.Equals(sound.Path, path, StringComparison.OrdinalIgnoreCase));
                if (existing is not null) return existing;
            }
            var player = new MediaPlayer();
            var sound = new RuntimeSound(player, path, loop);
            sounds.Add(sound);
            player.MediaEnded += (_, _) => HandleSoundEnded(sound);
            player.MediaFailed += (_, _) => HandleSoundFailed(sound);
            player.Open(new Uri(path, UriKind.Absolute));
            if (positionSeconds > 0d)
            {
                player.MediaOpened += (_, _) =>
                {
                    try
                    {
                        player.Position = TimeSpan.FromSeconds(positionSeconds);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    player.Play();
                };
            }
            else
            {
                player.Play();
            }
            ApplyVolume(sound);
            return sound;
        }

        private void HandleSoundEnded(RuntimeSound sound)
        {
            if (sound.Loop && !token.IsCancellationRequested)
            {
                try
                {
                    sound.Player.Position = TimeSpan.Zero;
                    sound.Player.Play();
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }
            sound.Ended = true;
            sound.Completion.TrySetResult();
        }

        private void HandleSoundFailed(RuntimeSound sound)
        {
            sound.Ended = true;
            sound.Completion.TrySetResult();
        }

        private void ApplyVolume(RuntimeSound sound)
        {
            var perSound = run.SoundVolumes.GetValueOrDefault(sound.Path, 100d);
            sound.Player.Volume = Math.Clamp(run.GlobalVolume / 100d * perSound / 100d, 0d, 1d);
        }

        private void ApplyVolumes()
        {
            foreach (var sound in sounds)
            {
                ApplyVolume(sound);
            }
        }

        private void StopSounds(string path)
        {
            foreach (var sound in sounds.Where(sound => string.Equals(sound.Path, path, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                sound.Player.Stop();
                sound.Player.Close();
                sound.Ended = true;
                sound.Completion.TrySetResult();
                sounds.Remove(sound);
            }
        }

        private async Task<string?> AskAsync(string question)
        {
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (run.Surface is not Panel panel)
            {
                return string.Empty;
            }
            var input = new TextBox
            {
                FontSize = 48,
                MinWidth = 760,
                Padding = new Thickness(20, 12, 20, 12),
                Background = Brushes.White,
                Foreground = Brushes.Black,
                BorderBrush = new SolidColorBrush(Color.FromRgb(216, 210, 198)),
                BorderThickness = new Thickness(4)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = question,
                Foreground = Brushes.White,
                FontSize = 56,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 1200,
                Margin = new Thickness(0, 0, 0, 28)
            });
            stack.Children.Add(input);
            stack.Children.Add(new TextBlock
            {
                Text = "Напиши ответ и нажми «ввод»",
                Foreground = new SolidColorBrush(Color.FromRgb(216, 210, 198)),
                FontSize = 30,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 24, 0, 0)
            });
            var confirm = new Button
            {
                Content = "Готово",
                FontSize = 32,
                Padding = new Thickness(28, 10, 28, 10),
                Margin = new Thickness(0, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(234, 228, 216)),
                Foreground = Brushes.Black
            };
            stack.Children.Add(confirm);
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 23, 20, 18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(234, 228, 216)),
                BorderThickness = new Thickness(4),
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(56, 40, 56, 40),
                MaxWidth = 1400,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = stack
            };

            void Submit() => completion.TrySetResult(input.Text);

            input.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                Submit();
            };
            confirm.Click += (_, _) => Submit();

            panel.Children.Add(card);
            Panel.SetZIndex(card, 100);
            askDepth++;
            input.Focus();
            Keyboard.Focus(input);
            try
            {
                return await completion.Task.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            finally
            {
                askDepth--;
                panel.Children.Remove(card);
                if (Window.GetWindow(run.Surface) is { } window)
                {
                    window.Focus();
                }
            }
        }

        private static double SoundPosition(RuntimeSound sound)
        {
            try
            {
                return sound.Player.Position.TotalSeconds;
            }
            catch (InvalidOperationException)
            {
                return 0d;
            }
        }

        private static bool IsOpaqueAt(ObjectRuntime runtime, MouseEventArgs e) =>
            runtime.Target is { } target && target.Opacity > 0d && target.Source is BitmapSource bitmap &&
            SpriteAlphaMask.IsOpaqueAt(bitmap, e.GetPosition(target), target.ActualWidth, target.ActualHeight, runtime.IsBackground);

        private static bool UsesProperty(ObjectRuntime runtime, string property) =>
            runtime.Item.Commands.Any(command => command.Argument.Contains(property, StringComparison.OrdinalIgnoreCase) ||
                command.Condition?.Contains(property, StringComparison.OrdinalIgnoreCase) == true);

        private Guid? FindTopmostOpaqueObject(MouseEventArgs e)
        {
            var panel = run.Surface as Panel;
            var best = (Guid?)null;
            var bestIndex = int.MinValue;
            var bestZIndex = int.MinValue;
            foreach (var runtime in objects)
            {
                if (!runtime.Visible || runtime.Target is not { } target || !IsOpaqueAt(runtime, e)) continue;
                var zIndex = Panel.GetZIndex(target);
                var childIndex = panel?.Children.IndexOf(target) ?? 0;
                if (zIndex > bestZIndex || (zIndex == bestZIndex && childIndex > bestIndex))
                {
                    bestZIndex = zIndex;
                    bestIndex = childIndex;
                    best = runtime.Item.Id;
                }
            }
            return best;
        }

        private ObjectRuntime? FindRuntime(Guid id) => byId.GetValueOrDefault(id);

        private CompiledObject FindObject(Guid id) => compiledScene.Objects.First(item => item.Id == id);

        private bool TryResolveSprite(Guid ownerId, string fileName, out string path)
        {
            var directory = run.Owner.ContentStore.GetObjectResourceDirectory(run.SourceProject, scene, ownerId, "sprites");
            path = ProjectContentStore.ListResourceFiles(directory, ProjectContentStore.ImageExtensions)
                .FirstOrDefault(value => string.Equals(Path.GetFileName(value), fileName, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            return path.Length > 0;
        }
    }

    private sealed class ObjectRuntime
    {
        public ObjectRuntime(CompiledObject item, Image target, ScaleTransform? size, TranslateTransform? position)
        {
            Item = item;
            Target = target;
            Size = size;
            Position = position;
            EventBlocks = ScriptEventBlockParser.Parse(item.Commands);
        }

        public CompiledObject Item { get; }
        public Image Target { get; }
        public ScaleTransform? Size { get; }
        public TranslateTransform? Position { get; }
        public IReadOnlyList<ScriptEventBlock> EventBlocks { get; }
        public RuntimeObjectProperties Properties { get; } = new();
        public Dictionary<int, bool> ConditionStates { get; } = [];
        public List<ScriptCursor> Cursors { get; } = [];
        public List<DispatcherTimer> Timers { get; } = [];
        public List<RuntimeCursorSnapshot> PendingCursors { get; } = [];
        public BitmapSource? OriginalSource { get; set; }
        public DispatcherTimer? ImageTimer { get; set; }
        public double Brightness { get; set; } = 100d;
        public int ImageSpeed { get; set; }
        public int PendingImageSpeed { get; set; }
        public bool Visible { get; set; } = true;
        public Rect? Bounds { get; set; }
        public bool IsBackground => Item.Id == IskraObject.BackgroundId;

        public Dictionary<string, string> CurrentValues(ProjectRun run)
        {
            var values = new Dictionary<string, string>(run.Variables, StringComparer.Ordinal);
            foreach (var property in Properties.Values)
            {
                values[property.Key] = property.Value;
            }
            return values;
        }
    }

    private sealed class ScriptCursor(int start, int end)
    {
        public int Start { get; } = start;
        public int End { get; } = end;
        public int Index { get; set; } = start;
    }

    private sealed class RuntimeSound(MediaPlayer player, string path, bool loop)
    {
        public MediaPlayer Player { get; } = player;
        public string Path { get; } = path;
        public bool Loop { get; } = loop;
        public bool Ended { get; set; }
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly record struct SceneSwitchRequest(Guid SceneId, bool Resume);
}
