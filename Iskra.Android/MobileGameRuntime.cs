using System.Diagnostics;
using System.Globalization;
using System.IO;
using IskraStudio;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Iskra.Android;

internal sealed class MobileGameRuntime : IEngineHost, IDisposable
{
    private readonly ProjectStore projectStore;
    private readonly ProjectContentStore contentStore;
    private readonly IskraProject project;
    private readonly CompiledProject compiled;
    private readonly CompiledScene compiledScene;
    private readonly IskraScene initialScene;
    private readonly Action invalidate;
    private readonly Action<string> printOutput;
    private readonly Action<IReadOnlyList<KeyValuePair<string, string>>> variablesChanged;
    private readonly Func<string, Task<string?>> ask;
    private readonly Dictionary<string, MobileBitmap> bitmaps = [];
    private readonly List<AndroidSound> sounds = [];
    private readonly List<IDispatcherTimer> timers = [];
    private readonly HashSet<string> logged = new(StringComparer.OrdinalIgnoreCase);
    private readonly object audioSync = new();
    private readonly object pauseSync = new();
    private TaskCompletionSource? pauseSignal;
    private bool paused;
    private bool debugAudioActive;
    private bool disposed;
    private ScriptEngine? engine;
    private float sceneWidth;
    private float sceneHeight;
    private double lastFrameTimestamp;

    public MobileGameRuntime(ProjectStore projectStore, IskraProject project, IskraScene scene, CompiledProject compiled,
        RuntimeSceneSnapshot? snapshot, Action invalidate, Action<string> printOutput,
        Action<IReadOnlyList<KeyValuePair<string, string>>> variablesChanged, Func<string, Task<string?>> ask)
    {
        this.projectStore = projectStore;
        contentStore = new ProjectContentStore(projectStore);
        this.project = project;
        this.compiled = compiled;
        compiledScene = compiled.Scenes.FirstOrDefault(candidate => candidate.Id == scene.Id)
            ?? throw new InvalidOperationException("Скомпилированная сцена не найдена.");
        initialScene = scene;
        this.invalidate = invalidate;
        this.printOutput = printOutput;
        this.variablesChanged = variablesChanged;
        this.ask = ask;
        ApplySceneSize(compiled.Orientation);
        engine = new ScriptEngine(compiled, compiledScene, this, contentStore, project, snapshot);
        LoadBitmaps();
        foreach (var item in engine.Objects)
        {
            var directory = contentStore.GetObjectResourceDirectory(project, scene, item.Id, "sprites");
            engine.SetImageList(item.Id, ProjectContentStore.ListResourceFiles(directory, ProjectContentStore.ImageExtensions));
        }
        engine.Prepare();
        engine.SceneRequested += () => MainThread.BeginInvokeOnMainThread(HandleSceneRequested);
        _ = engine.Start();
        StartFramePump();
    }

    public event Func<Guid, bool, RuntimeSceneSnapshot?, Task>? SceneNavigation;

    public ProjectOrientation Orientation { get; private set; }

    public bool ShowAxes { get; set; }

    public ScriptEngine Engine => engine ?? throw new ObjectDisposedException(nameof(MobileGameRuntime));

    public IReadOnlyList<KeyValuePair<string, string>> GetVariablesSnapshot() =>
        Engine.Variables.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value))
            .ToList();

    public void SetPaused(bool value)
    {
        lock (pauseSync)
        {
            if (paused == value) return;
            paused = value;
            pauseSignal = value ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        }
        SetAudioPaused(value && !debugAudioActive);
    }

    public void SetDebugAudioActive(bool value)
    {
        debugAudioActive = value;
        SetAudioPaused(paused && !value);
    }

    private void SetAudioPaused(bool value)
    {
        lock (audioSync)
        {
            foreach (var sound in sounds.ToArray())
            {
                try
                {
                    if (value) sound.Pause();
                    else sound.Resume();
                }
                catch (Exception exception)
                {
                    MobileLog.Write("Не удалось изменить состояние звука при паузе.", exception);
                }
            }
        }
    }

    private void StartFramePump()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        if (dispatcher is null) return;
        var timer = dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.IsRepeating = true;
        timer.Tick += (_, _) =>
        {
            if (disposed) { timer.Stop(); return; }
            var now = Stopwatch.GetTimestamp();
            var delta = lastFrameTimestamp == 0 ? 0d : Math.Clamp((now - lastFrameTimestamp) / (double)Stopwatch.Frequency, 0d, 0.05d);
            lastFrameTimestamp = now;
            if (delta > 0) Engine.Tick(delta);
        };
        timers.Add(timer);
        timer.Start();
    }

    private void HandleSceneRequested()
    {
        if (disposed || Engine is null) return;
        var pending = Engine.PendingScene;
        if (pending is not { } request) return;
        var snapshot = request.Resume ? Engine.CaptureSnapshot() : null;
        SceneNavigation?.Invoke(request.SceneId, request.Resume, snapshot);
    }

    private void ApplySceneSize(ProjectOrientation orientation)
    {
        Orientation = orientation;
        sceneWidth = orientation == ProjectOrientation.Horizontal ? 1920 : 1080;
        sceneHeight = orientation == ProjectOrientation.Horizontal ? 1080 : 1920;
    }

    private void LoadBitmaps()
    {
        foreach (var item in compiledScene.Objects)
        {
            var directory = contentStore.GetObjectResourceDirectory(project, initialScene, item.Id, "sprites");
            foreach (var file in ProjectContentStore.ListResourceFiles(directory, ProjectContentStore.ImageExtensions))
            {
                try
                {
                    var bitmap = LoadBitmap(file);
                    if (bitmap is null) continue;
                    bitmaps[file] = new MobileBitmap(bitmap);
                }
                catch (Exception exception)
                {
                    MobileLog.Write($"Не удалось загрузить образ «{Path.GetFileName(file)}».", exception);
                }
            }
        }
    }

    private static SKBitmap? LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);
        if (codec is null) return null;
        var info = codec.Info;
        var maximumSide = Math.Max(info.Width, info.Height);
        const int limit = 4096;
        if (maximumSide <= limit)
        {
            var bitmap = new SKBitmap(info);
            var result = codec.GetPixels(info, bitmap.GetPixels());
            if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput) return bitmap;
            bitmap.Dispose();
            return SKBitmap.Decode(path);
        }
        var scaled = codec.GetScaledDimensions((float)limit / maximumSide);
        var target = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var resized = new SKBitmap(target);
        var scaledResult = codec.GetPixels(target, resized.GetPixels());
        if (scaledResult is SKCodecResult.Success or SKCodecResult.IncompleteInput) return resized;
        resized.Dispose();
        return SKBitmap.Decode(path);
    }

    public void Draw(SKCanvas canvas, int width, int height)
    {
        var scale = Math.Min(width / sceneWidth, height / sceneHeight);
        var viewportLeft = (width - sceneWidth * scale) / 2;
        var viewportTop = (height - sceneHeight * scale) / 2;
        canvas.Save();
        canvas.Translate(viewportLeft, viewportTop);
        canvas.Scale(scale);
        canvas.DrawColor(SKColors.Black);
        foreach (var item in Engine.Objects.OrderBy(item => item.Layer))
        {
            DrawObject(canvas, item, item.IsBackground);
        }
        canvas.Restore();
        if (ShowAxes)
        {
            using var xAxis = new SKPaint { Color = new SKColor(200, 95, 73, 235), StrokeWidth = 2, IsAntialias = true };
            using var yAxis = new SKPaint { Color = new SKColor(111, 177, 145, 235), StrokeWidth = 2, IsAntialias = true };
            canvas.DrawLine(0, height / 2f, width, height / 2f, xAxis);
            canvas.DrawLine(width / 2f, 0, width / 2f, height, yAxis);
        }
    }

    private void DrawObject(SKCanvas canvas, EngineObject item, bool isBackground)
    {
        if (!item.Visible) return;
        var file = Engine.CurrentImageFile(item);
        if (file is null || !bitmaps.TryGetValue(file, out var bitmap)) return;
        var image = bitmap.Bitmap;
        var size = (float)(item.SizePercent / 100d);
        var rect = isBackground ? new SKRect(0, 0, sceneWidth, sceneHeight) :
            new SKRect(sceneWidth / 2 + (float)item.X - image.Width * size / 2f,
                sceneHeight / 2 + (float)item.Y - image.Height * size / 2f,
                sceneWidth / 2 + (float)item.X + image.Width * size / 2f,
                sceneHeight / 2 + (float)item.Y + image.Height * size / 2f);
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, (byte)Math.Clamp(255 * (100 - item.TransparencyPercent) / 100, 0, 255)) };
        if (item.BrightnessPercent < 100)
        {
            var brightness = (float)(item.BrightnessPercent / 100d);
            paint.ColorFilter = SKColorFilter.CreateColorMatrix([brightness, 0, 0, 0, 0, 0, brightness, 0, 0, 0, 0, 0, brightness, 0, 0, 0, 0, 0, 1, 0]);
        }
        var source = isBackground ? CoverSourceRect(image.Width, image.Height) : new SKRect(0, 0, image.Width, image.Height);
        if (isBackground || (item.DirectionDegrees == 0d && !item.FlipX && !item.FlipY))
        {
            canvas.DrawBitmap(image, source, rect, new SKSamplingOptions(SKFilterMode.Linear), paint);
            return;
        }
        canvas.Save();
        var centerX = rect.MidX;
        var centerY = rect.MidY;
        if (item.DirectionDegrees != 0d) canvas.RotateDegrees((float)item.DirectionDegrees, centerX, centerY);
        canvas.Scale(item.FlipX ? -1f : 1f, item.FlipY ? -1f : 1f, centerX, centerY);
        canvas.DrawBitmap(image, source, rect, new SKSamplingOptions(SKFilterMode.Linear), paint);
        canvas.Restore();
    }

    private SKRect CoverSourceRect(int width, int height)
    {
        var imageAspect = (float)width / height;
        var targetAspect = sceneWidth / sceneHeight;
        var source = new SKRect(0, 0, width, height);
        if (imageAspect > targetAspect)
        {
            var cropWidth = height * targetAspect;
            source.Left = (width - cropWidth) / 2;
            source.Right = source.Left + cropWidth;
        }
        else
        {
            var cropHeight = width / targetAspect;
            source.Top = (height - cropHeight) / 2;
            source.Bottom = source.Top + cropHeight;
        }
        return source;
    }

    public void HandlePointer(SKTouchAction action, SKPoint point, float viewWidth, float viewHeight, CancellationToken token)
    {
        if (viewWidth <= 0 || viewHeight <= 0) return;
        var scale = Math.Min(viewWidth / sceneWidth, viewHeight / sceneHeight);
        var viewportLeft = (viewWidth - sceneWidth * scale) / 2;
        var viewportTop = (viewHeight - sceneHeight * scale) / 2;
        var logical = new SKPoint((point.X - viewportLeft) / scale, (point.Y - viewportTop) / scale);
        var hit = TopmostAt(logical.X, logical.Y);
        if (action == SKTouchAction.Moved)
        {
            Engine.PointerMoved(hit);
            return;
        }
        if (action == SKTouchAction.Pressed)
        {
            if (hit is null) return;
            Engine.PointerPressed(hit.Value);
            return;
        }
        if (action == SKTouchAction.Released)
        {
            if (hit is null) return;
            Engine.PointerReleased(hit.Value, hit.Value);
        }
    }

    public Guid? TopmostAt(double x, double y)
    {
        foreach (var item in Engine.Objects.Where(item => item.Visible).OrderByDescending(item => item.Layer))
        {
            if (item.IsBackground) continue;
            if (IsOverPoint(item, x, y)) return item.Id;
        }
        return null;
    }

    public bool IsOverPoint(EngineObject item, double x, double y)
    {
        var file = Engine.CurrentImageFile(item);
        if (file is null || !bitmaps.TryGetValue(file, out var bitmap)) return false;
        var image = bitmap.Bitmap;
        var size = (float)(item.SizePercent / 100d);
        var width = image.Width * size;
        var height = image.Height * size;
        var left = sceneWidth / 2 + item.X - width / 2;
        var top = sceneHeight / 2 + item.Y - height / 2;
        if (x < left || y < top || x >= left + width || y >= top + height) return false;
        var radians = item.DirectionDegrees * Math.PI / 180d;
        var cosine = Math.Abs(Math.Cos(radians));
        var sine = Math.Abs(Math.Sin(radians));
        var rotatedWidth = width * cosine + height * sine;
        var rotatedHeight = width * sine + height * cosine;
        var centerX = sceneWidth / 2 + item.X;
        var centerY = sceneHeight / 2 + item.Y;
        if (Math.Abs(x - centerX) > rotatedWidth / 2 || Math.Abs(y - centerY) > rotatedHeight / 2) return false;
        var pixelX = Math.Clamp((int)((x - left) / width * image.Width), 0, image.Width - 1);
        var pixelY = Math.Clamp((int)((y - top) / height * image.Height), 0, image.Height - 1);
        return image.GetPixel(pixelX, pixelY).Alpha > 8;
    }

    public bool Overlaps(EngineObject first, EngineObject second)
    {
        var firstRect = BoundsOf(first);
        var secondRect = BoundsOf(second);
        if (firstRect is null || secondRect is null) return false;
        return firstRect.Value.IntersectsWith(secondRect.Value);
    }

    private SKRect? BoundsOf(EngineObject item)
    {
        var file = Engine.CurrentImageFile(item);
        if (file is null || !bitmaps.TryGetValue(file, out var bitmap)) return null;
        var image = bitmap.Bitmap;
        var size = (float)(item.SizePercent / 100d);
        var width = image.Width * size;
        var height = image.Height * size;
        var radians = item.DirectionDegrees * Math.PI / 180d;
        var cosine = Math.Abs(Math.Cos(radians));
        var sine = Math.Abs(Math.Sin(radians));
        var rotatedWidth = width * cosine + height * sine;
        var rotatedHeight = width * sine + height * cosine;
        var centerX = sceneWidth / 2 + item.X;
        var centerY = sceneHeight / 2 + item.Y;
        return new SKRect((float)(centerX - rotatedWidth / 2), (float)(centerY - rotatedHeight / 2), (float)(centerX + rotatedWidth / 2), (float)(centerY + rotatedHeight / 2));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime?.Cancel();
        foreach (var timer in timers) timer.Stop();
        timers.Clear();
        Engine?.Cancel();
        lock (audioSync)
        {
            foreach (var sound in sounds.ToArray())
            {
                try { sound.StopWithoutNotify(); }
                catch (Exception exception) { MobileLog.Write("Не удалось освободить звук сцены.", exception); }
            }
            sounds.Clear();
        }
        foreach (var bitmap in bitmaps.Values) bitmap.Bitmap.Dispose();
        bitmaps.Clear();
    }

    public double SurfaceWidth => sceneWidth;

    public double SurfaceHeight => sceneHeight;

    public bool Paused
    {
        get { lock (pauseSync) return paused; }
    }

    public Task WaitWhilePausedAsync()
    {
        TaskCompletionSource? signal;
        lock (pauseSync)
        {
            if (!paused) return Task.CompletedTask;
            signal = pauseSignal;
        }
        return signal?.Task ?? Task.CompletedTask;
    }

    public CancellationToken Token
    {
        get
        {
            lifetime ??= new CancellationTokenSource();
            return lifetime.Token;
        }
    }

    private CancellationTokenSource? lifetime;

    public void Output(string objectName, string text) => printOutput(text);

    public void VariablesChanged(IReadOnlyList<RuntimeDebugVariable> variables)
    {
        var snapshot = variables
            .Where(item => !string.IsNullOrEmpty(item.Name))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new KeyValuePair<string, string>(item.Name, item.Value))
            .ToList();
        variablesChanged(snapshot);
    }

    public async Task<string?> AskAsync(string question)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                completion.TrySetResult(await ask(question));
            }
            catch (Exception exception)
            {
                MobileLog.Write("Не удалось показать вопрос.", exception);
                completion.TrySetResult(string.Empty);
            }
        });
        return await completion.Task;
    }

    public IEngineSound? PlaySound(string path, bool loop, double positionSeconds)
    {
        try
        {
            var activity = Platform.CurrentActivity;
            if (activity is null) return null;
            var player = global::Android.Media.MediaPlayer.Create(activity, global::Android.Net.Uri.FromFile(new Java.IO.File(path)));
            if (player is null) return null;
            var sound = new AndroidSound(player, path, loop, () => SafeNotifyEnded(path, loop));
            player.Completion += sound.HandleCompleted;
            player.Error += sound.HandleCompleted;
            lock (audioSync) sounds.Add(sound);
            if (positionSeconds > 0d)
            {
                try { player.SeekTo((int)(positionSeconds * 1000)); }
                catch (Exception exception) { MobileLog.Write("Не удалось перемотать звук.", exception); }
            }
            player.Start();
            var volume = (float)Math.Clamp(Engine.GlobalVolume / 100d, 0d, 1d);
            player.SetVolume(volume, volume);
            return sound;
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось воспроизвести звук сцены.", exception);
            return null;
        }
    }

    private void SafeNotifyEnded(string path, bool loop)
    {
        if (disposed) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (disposed || Engine is null) return;
            Engine.NotifySoundEnded(path, loop);
        });
    }

    public void OrientationChanged(ProjectOrientation orientation)
    {
        ApplySceneSize(orientation);
        invalidate();
    }

    public void SceneSwitchRequested(Guid sceneId, bool resume)
    {
    }

    public void StateChanged() => invalidate();

    private sealed class MobileBitmap(SKBitmap bitmap)
    {
        public SKBitmap Bitmap { get; } = bitmap;
    }

    private sealed class AndroidSound : IEngineSound
    {
        private readonly global::Android.Media.MediaPlayer player;
        private readonly string path;
        private readonly Action onEnded;
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AndroidSound(global::Android.Media.MediaPlayer player, string path, bool loop, Action onEnded)
        {
            this.player = player;
            this.path = path;
            Loop = loop;
            this.onEnded = onEnded;
        }

        public void OnCompletedPublic(object? sender, EventArgs args) => HandleCompleted(sender, args);

        public void OnErrorPublic(object? sender, EventArgs args) => HandleCompleted(sender, args);

        public Task Completion => completion.Task;

        public double PositionSeconds
        {
            get
            {
                try { return player.CurrentPosition / 1000d; }
                catch (Exception) { return 0d; }
            }
        }

        public bool Loop { get; set; }

        public void SetVolume(double volume) => player.SetVolume((float)volume, (float)volume);

        public void Pause() => player.Pause();

        public void Resume() => player.Start();

        public void Stop()
        {
            player.Completion -= HandleCompleted;
            player.Error -= HandleCompleted;
            StopWithoutNotify();
            completion.TrySetResult();
        }

        public void StopWithoutNotify() => StopCore();

        public void HandleCompleted(object? sender, EventArgs args)
        {
            onEnded();
            completion.TrySetResult();
        }

        private void StopCore()
        {
            try
            {
                player.Stop();
                player.Release();
                player.Dispose();
            }
            catch (Exception exception)
            {
                MobileLog.Write("Не удалось остановить звук.", exception);
            }
        }
    }
}
