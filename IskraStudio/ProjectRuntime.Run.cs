using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IskraStudio;

internal readonly record struct SceneSwitchRequest(Guid SceneId, bool Resume);

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
        var run = new ProjectRun(this, sourceProject);
        try
        {
            var currentScene = scene;
            var resume = false;
            while (currentScene is not null && !cancellationToken.IsCancellationRequested)
            {
                var compiledScene = project.Scenes.FirstOrDefault(candidate => candidate.Id == currentScene.Id);
                if (compiledScene is null) break;
                var snapshot = resume ? run.LoadSnapshot(currentScene.Id) : null;
                var host = new WpfSceneHost(sceneSurface, contentStore, sourceProject, output, variablesUpdated, cancellationToken);
                var engine = new ScriptEngine(project, compiledScene, host, contentStore, sourceProject, snapshot);
                host.Attach(engine);
                engine.Prepare();
                host.Build();
                _ = engine.Start();
                SceneSwitchRequest? request = null;
                try
                {
                    request = await host.RunAsync();
                }
                finally
                {
                    engine.Cancel();
                    host.Dispose();
                }
                if (request is null || cancellationToken.IsCancellationRequested) break;
                run.SaveSnapshot(currentScene.Id, engine.CaptureSnapshot());
                currentScene = sourceProject.Scenes.FirstOrDefault(candidate => candidate.Id == request.Value.SceneId);
                resume = request.Value.Resume;
            }
        }
        finally
        {
            run.Dispose();
        }
    }
}

internal sealed class ProjectRun(ProjectRuntime owner, IskraProject sourceProject)
{
    private readonly HashSet<Guid> visitedScenes = [];
    private readonly string snapshotDirectory = Path.Combine(owner.GetProjectCacheDirectory(sourceProject), "SceneSnapshots");

    public void SaveSnapshot(Guid sceneId, RuntimeSceneSnapshot snapshot)
    {
        visitedScenes.Add(sceneId);
        try
        {
            Directory.CreateDirectory(snapshotDirectory);
            File.WriteAllText(Path.Combine(snapshotDirectory, sceneId.ToString("N") + ".json"),
                System.Text.Json.JsonSerializer.Serialize(snapshot));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CoreLog.Write("Не удалось сохранить снапшот сцены.", exception);
        }
    }

    public RuntimeSceneSnapshot? LoadSnapshot(Guid sceneId)
    {
        if (!visitedScenes.Contains(sceneId)) return null;
        try
        {
            var path = Path.Combine(snapshotDirectory, sceneId.ToString("N") + ".json");
            return File.Exists(path)
                ? System.Text.Json.JsonSerializer.Deserialize<RuntimeSceneSnapshot>(File.ReadAllText(path))
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            CoreLog.Write("Не удалось прочитать снапшот сцены.", exception);
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
            CoreLog.Write("Не удалось удалить временные снапшоты сцен.", exception);
        }
    }
}

internal sealed class WpfSceneHost : IEngineHost, IDisposable
{
    private readonly FrameworkElement surface;
    private readonly ProjectContentStore contentStore;
    private readonly IskraProject sourceProject;
    private readonly Action<string, string>? output;
    private readonly Action<IReadOnlyList<RuntimeDebugVariable>>? variablesUpdated;
    private readonly CancellationToken token;
    private readonly Dictionary<Guid, ObjectVisual> visuals = [];
    private readonly List<Action> eventSubscriptions = [];
    private readonly Stopwatch frameStopwatch = Stopwatch.StartNew();
    private long lastFrameTimestamp;
    private Panel? panel;
    private int askDepth;

    public WpfSceneHost(FrameworkElement surface, ProjectContentStore contentStore, IskraProject sourceProject,
        Action<string, string>? output, Action<IReadOnlyList<RuntimeDebugVariable>>? variablesUpdated, CancellationToken token)
    {
        this.surface = surface;
        this.contentStore = contentStore;
        this.sourceProject = sourceProject;
        this.output = output;
        this.variablesUpdated = variablesUpdated;
        this.token = token;
    }

    public ScriptEngine Engine { get; private set; } = null!;

    private sealed class ObjectVisual
    {
        public Image Target { get; init; } = null!;
        public ScaleTransform Size { get; init; } = null!;
        public RotateTransform Direction { get; init; } = null!;
        public TranslateTransform Position { get; init; } = null!;
        public BitmapSource? OriginalSource { get; set; }
        public string? AppliedFile { get; set; }
        public double AppliedBrightness { get; set; } = double.NaN;
    }

    private sealed class WpfSound : IEngineSound
    {
        private readonly MediaPlayer player;
        private readonly Action<WpfSound> onEnded;
        private readonly Action<WpfSound> onFailed;

        public WpfSound(string path, double positionSeconds, Action<WpfSound> onEnded, Action<WpfSound> onFailed)
        {
            player = new MediaPlayer();
            this.onEnded = onEnded;
            this.onFailed = onFailed;
            CompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            player.MediaEnded += (_, _) => { onEnded(this); CompletionSource.TrySetResult(); };
            player.MediaFailed += (_, _) => { onFailed(this); CompletionSource.TrySetResult(); };
            player.Open(new Uri(path, UriKind.Absolute));
            if (positionSeconds > 0d)
            {
                player.MediaOpened += (_, _) =>
                {
                    try { player.Position = TimeSpan.FromSeconds(positionSeconds); }
                    catch (InvalidOperationException) { }
                    player.Play();
                };
            }
            else
            {
                player.Play();
            }
        }

        private void TrySetPosition(TimeSpan position)
        {
            try { player.Position = position; }
            catch (InvalidOperationException) { }
        }

        public TaskCompletionSource CompletionSource { get; }

        public Task Completion => CompletionSource.Task;

        public double PositionSeconds
        {
            get
            {
                try { return player.Position.TotalSeconds; }
                catch (InvalidOperationException) { return 0d; }
            }
        }

        public bool Loop { get; set; }

        public void SetVolume(double volume)
        {
            player.Volume = Math.Clamp(volume, 0d, 1d);
        }

        public void Pause() => player.Pause();

        public void Resume() => player.Play();

        public void Stop()
        {
            player.Stop();
            player.Close();
        }
    }

    public void Attach(ScriptEngine scriptEngine)
    {
        Engine = scriptEngine;
    }

    public void Build()
    {
        panel = surface as Panel;
        panel?.Children.Clear();
        foreach (var item in Engine.Objects)
        {
            var directory = contentStore.GetObjectResourceDirectory(sourceProject, SourceScene, item.Id, "sprites");
            Engine.SetImageList(item.Id, ProjectContentStore.ListResourceFiles(directory, ProjectContentStore.ImageExtensions));
        }
        foreach (var item in Engine.Objects.OrderBy(candidate => candidate.IsBackground ? 0 : 1))
        {
            var scale = new ScaleTransform();
            var direction = new RotateTransform();
            var translation = new TranslateTransform();
            var transform = new TransformGroup();
            transform.Children.Add(scale);
            transform.Children.Add(direction);
            transform.Children.Add(translation);
            var image = new Image
            {
                Stretch = item.IsBackground ? Stretch.UniformToFill : Stretch.None,
                HorizontalAlignment = item.IsBackground ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
                VerticalAlignment = item.IsBackground ? VerticalAlignment.Stretch : VerticalAlignment.Center,
                RenderTransform = transform,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = true
            };
            panel?.Children.Add(image);
            Panel.SetZIndex(image, item.IsBackground ? 0 : item.Layer);
            visuals[item.Id] = new ObjectVisual
            {
                Target = image,
                Size = scale,
                Direction = direction,
                Position = translation
            };
        }
        ApplyVisuals();
        WireEvents();
        StartFramePump();
    }

    private IskraScene SourceScene => sourceProject.Scenes.First(candidate => candidate.Id == Engine.SceneId);

    public async Task<SceneSwitchRequest?> RunAsync()
    {
        var completion = new TaskCompletionSource<SceneSwitchRequest?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSceneRequested()
        {
            var pending = Engine.PendingScene;
            if (pending is { } request) completion.TrySetResult(new SceneSwitchRequest(request.SceneId, request.Resume));
        }
        Engine.SceneRequested += OnSceneRequested;
        var idle = Task.Delay(Timeout.InfiniteTimeSpan, token);
        _ = idle.ContinueWith(completed => _ = completed.Exception, TaskContinuationOptions.OnlyOnFaulted);
        try
        {
            await Task.WhenAny(idle, completion.Task);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            Engine.SceneRequested -= OnSceneRequested;
        }
        return token.IsCancellationRequested ? null : completion.Task.Status == TaskStatus.RanToCompletion ? completion.Task.Result : null;
    }

    private void StartFramePump()
    {
        EventHandler handler = (_, _) =>
        {
            var now = frameStopwatch.ElapsedMilliseconds;
            var delta = lastFrameTimestamp == 0 ? 0d : Math.Clamp((now - lastFrameTimestamp) / 1000d, 0d, 0.05d);
            lastFrameTimestamp = now;
            if (delta > 0) Engine.Tick(delta);
            ApplyVisuals();
        };
        CompositionTarget.Rendering += handler;
        eventSubscriptions.Add(() => CompositionTarget.Rendering -= handler);
    }

    public void ApplyVisuals()
    {
        if (Engine is null) return;
        foreach (var item in Engine.Objects)
        {
            if (!visuals.TryGetValue(item.Id, out var visual)) continue;
            visual.Position.X = item.X;
            visual.Position.Y = item.Y;
            visual.Size.ScaleX = item.SizePercent / 100d * (item.FlipX ? -1d : 1d);
            visual.Size.ScaleY = item.SizePercent / 100d * (item.FlipY ? -1d : 1d);
            visual.Direction.Angle = item.DirectionDegrees;
            visual.Target.Opacity = 1d - item.TransparencyPercent / 100d;
            visual.Target.Visibility = item.Visible ? Visibility.Visible : Visibility.Hidden;
            Panel.SetZIndex(visual.Target, item.Layer);
            var file = Engine.CurrentImageFile(item);
            if (file != visual.AppliedFile)
            {
                visual.AppliedFile = file;
                visual.OriginalSource = file is null ? null : SpriteLoader.LoadBitmap(file);
                visual.Target.Source = visual.OriginalSource is null
                    ? null
                    : SpriteBrightness.Apply(visual.OriginalSource, item.BrightnessPercent);
                visual.AppliedBrightness = item.BrightnessPercent;
            }
            else if (visual.AppliedBrightness != item.BrightnessPercent && visual.OriginalSource is not null)
            {
                visual.AppliedBrightness = item.BrightnessPercent;
                visual.Target.Source = SpriteBrightness.Apply(visual.OriginalSource, item.BrightnessPercent);
            }
        }
    }

    private void WireEvents()
    {
        if (Window.GetWindow(surface) is { } window)
        {
            KeyEventHandler keyDown = (_, e) =>
            {
                if (e.IsRepeat) return;
                var keyId = KeyboardKeyMapper.NormalizeKey(e.Key);
                if (keyId is not null) Engine.KeyDown(keyId);
            };
            KeyEventHandler keyUp = (_, e) =>
            {
                var keyId = KeyboardKeyMapper.NormalizeKey(e.Key);
                if (keyId is not null) Engine.KeyUp(keyId);
            };
            window.KeyDown += keyDown;
            window.KeyUp += keyUp;
            eventSubscriptions.Add(() => { window.KeyDown -= keyDown; window.KeyUp -= keyUp; });
        }

        foreach (var item in Engine.Objects)
        {
            if (visuals[item.Id].Target is not Image target) continue;
            var hasPress = item.EventBlocks.Any(block => block.EventKind == "press");
            var hasRelease = item.EventBlocks.Any(block => block.EventKind == "release");
            var usesPressed = UsesProperty(item, "Свойство.нажат");
            if (hasPress || usesPressed)
            {
                MouseButtonEventHandler press = (_, e) =>
                {
                    if (!IsOpaqueAt(item, e)) return;
                    target.CaptureMouse();
                    Engine.PointerPressed(item.Id);
                };
                target.MouseLeftButtonDown += press;
                eventSubscriptions.Add(() => target.MouseLeftButtonDown -= press);
            }
            if (hasRelease || usesPressed)
            {
                MouseButtonEventHandler release = (_, e) =>
                {
                    Engine.PointerReleased(item.Id, IsOpaqueAt(item, e) ? item.Id : null);
                    if (target.IsMouseCaptured) target.ReleaseMouseCapture();
                };
                target.MouseLeftButtonUp += release;
                eventSubscriptions.Add(() => target.MouseLeftButtonUp -= release);
                eventSubscriptions.Add(() => { if (target.IsMouseCaptured) target.ReleaseMouseCapture(); });
            }
            if (item.EventBlocks.Any(block => block.EventKind == "hover"))
            {
                var wasOnOpaque = false;
                MouseEventHandler move = (_, e) =>
                {
                    var onOpaque = IsOpaqueAt(item, e);
                    if (onOpaque && !wasOnOpaque) Engine.PointerMoved(item.Id);
                    wasOnOpaque = onOpaque;
                };
                MouseEventHandler leave = (_, _) => wasOnOpaque = false;
                target.MouseMove += move;
                target.MouseLeave += leave;
                eventSubscriptions.Add(() => { target.MouseMove -= move; target.MouseLeave -= leave; });
            }
            if (UsesProperty(item, "Свойство.наведен"))
            {
                MouseEventHandler surfaceMove = (_, e) => Engine.PointerMoved(TopmostAt(e.GetPosition(surface)));
                MouseEventHandler surfaceLeave = (_, _) => Engine.PointerMoved(null);
                surface.PreviewMouseMove += surfaceMove;
                surface.MouseLeave += surfaceLeave;
                eventSubscriptions.Add(() => { surface.PreviewMouseMove -= surfaceMove; surface.MouseLeave -= surfaceLeave; });
            }
        }
    }

    private static bool UsesProperty(EngineObject item, string property) =>
        item.Commands.Any(command => command.Argument.Contains(property, StringComparison.OrdinalIgnoreCase) ||
            command.Condition?.Contains(property, StringComparison.OrdinalIgnoreCase) == true);

    private bool IsOpaqueAt(EngineObject item, MouseEventArgs e)
    {
        if (!visuals.TryGetValue(item.Id, out var visual)) return false;
        if (visual.Target.Opacity <= 0d || visual.Target.Source is not BitmapSource bitmap) return false;
        return SpriteAlphaMask.IsOpaqueAt(bitmap, e.GetPosition(visual.Target), visual.Target.ActualWidth, visual.Target.ActualHeight, item.IsBackground);
    }

    private Guid? TopmostAt(Point point)
    {
        var best = (Guid?)null;
        var bestZ = int.MinValue;
        var bestIndex = int.MinValue;
        var children = (surface as Panel)?.Children;
        foreach (var item in Engine.Objects)
        {
            if (!visuals.TryGetValue(item.Id, out var visual)) continue;
            if (!item.Visible || visual.Target.Opacity <= 0d || visual.Target.Source is not BitmapSource bitmap) continue;
            if (!SpriteAlphaMask.IsOpaqueAt(bitmap, point, visual.Target.ActualWidth, visual.Target.ActualHeight, item.IsBackground)) continue;
            var zIndex = Panel.GetZIndex(visual.Target);
            var childIndex = children?.IndexOf(visual.Target) ?? 0;
            if (zIndex > bestZ || (zIndex == bestZ && childIndex > bestIndex))
            {
                bestZ = zIndex;
                bestIndex = childIndex;
                best = item.Id;
            }
        }
        return best;
    }

    private async Task<string?> AskAsyncCore(string question)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (surface is not Panel hostPanel) return string.Empty;
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

        hostPanel.Children.Add(card);
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
            hostPanel.Children.Remove(card);
            if (Window.GetWindow(surface) is { } window) window.Focus();
        }
    }

    public void Dispose()
    {
        foreach (var unsubscribe in eventSubscriptions)
        {
            unsubscribe();
        }
        eventSubscriptions.Clear();
        Engine?.Cancel();
        panel?.Children.Clear();
    }

    public double SurfaceWidth => surface.ActualWidth > 0 ? surface.ActualWidth : surface.Width;

    public double SurfaceHeight => surface.ActualHeight > 0 ? surface.ActualHeight : surface.Height;

    public bool Paused => false;

    public Task WaitWhilePausedAsync() => Task.CompletedTask;

    public CancellationToken Token => token;

    public void Output(string objectName, string text) => output?.Invoke(objectName, text);

    public void VariablesChanged(IReadOnlyList<RuntimeDebugVariable> variables) => variablesUpdated?.Invoke(variables);

    public Task<string?> AskAsync(string question) => AskAsyncCore(question);

    public IEngineSound? PlaySound(string path, bool loop, double positionSeconds)
    {
        try
        {
            return new WpfSound(path, positionSeconds, OnSoundEnded, OnSoundFailed);
        }
        catch (Exception exception)
        {
            CoreLog.Write("Не удалось воспроизвести звук.", exception);
            return null;
        }

        void OnSoundEnded(WpfSound sound)
        {
            try
            {
                Engine.NotifySoundEnded(path, sound.Loop);
            }
            catch (Exception exception)
            {
                CoreLog.Write("Не удалось обработать окончание звука.", exception);
            }
        }

        void OnSoundFailed(WpfSound sound)
        {
        }
    }

    public void OrientationChanged(ProjectOrientation orientation)
    {
        var horizontal = orientation == ProjectOrientation.Horizontal;
        surface.Width = horizontal ? 1920d : 1080d;
        surface.Height = horizontal ? 1080d : 1920d;
    }

    public void SceneSwitchRequested(Guid sceneId, bool resume)
    {
    }

    public void StateChanged() => ApplyVisuals();

    public bool Overlaps(EngineObject first, EngineObject second)
    {
        if (!visuals.TryGetValue(first.Id, out var firstVisual) || !visuals.TryGetValue(second.Id, out var secondVisual)) return false;
        if (firstVisual.OriginalSource is null || secondVisual.OriginalSource is null) return false;
        var width = SurfaceWidth;
        var height = SurfaceHeight;
        var firstBounds = ComputeBounds(first, firstVisual, width, height);
        var secondBounds = ComputeBounds(second, secondVisual, width, height);
        if (firstBounds is null || secondBounds is null) return false;
        return SpriteAlphaMask.Intersects(
            firstVisual.OriginalSource, firstBounds.Value, firstBounds.Value.Width, firstBounds.Value.Height,
            first.FlipX, first.FlipY, first.DirectionDegrees,
            secondVisual.OriginalSource, secondBounds.Value, secondBounds.Value.Width, secondBounds.Value.Height,
            second.FlipX, second.FlipY, second.DirectionDegrees);
    }

    private static Rect? ComputeBounds(EngineObject item, ObjectVisual visual, double width, double height)
    {
        if (visual.Target.Source is not BitmapSource bitmap) return null;
        var elementWidth = visual.Target.ActualWidth > 0 ? visual.Target.ActualWidth : bitmap.Width;
        var elementHeight = visual.Target.ActualHeight > 0 ? visual.Target.ActualHeight : bitmap.Height;
        var objectWidth = elementWidth * Math.Abs(item.SizePercent / 100d);
        var objectHeight = elementHeight * Math.Abs(item.SizePercent / 100d);
        var centerX = width / 2d + item.X;
        var centerY = height / 2d + item.Y;
        var radians = item.DirectionDegrees * Math.PI / 180d;
        var cosine = Math.Abs(Math.Cos(radians));
        var sine = Math.Abs(Math.Sin(radians));
        var rotatedWidth = objectWidth * cosine + objectHeight * sine;
        var rotatedHeight = objectWidth * sine + objectHeight * cosine;
        return new Rect(centerX - rotatedWidth / 2d, centerY - rotatedHeight / 2d, rotatedWidth, rotatedHeight);
    }

    public Guid? TopmostAt(double x, double y) => TopmostAt(new Point(x, y));

    public bool IsOverPoint(EngineObject item, double x, double y)
    {
        if (!visuals.TryGetValue(item.Id, out var visual)) return false;
        if (visual.Target.Source is not BitmapSource bitmap) return false;
        return SpriteAlphaMask.IsOpaqueAt(bitmap, new Point(x, y), visual.Target.ActualWidth, visual.Target.ActualHeight, item.IsBackground);
    }
}
