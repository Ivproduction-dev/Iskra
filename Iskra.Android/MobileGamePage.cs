using IskraStudio;
using SkiaSharp;
using SkiaSharp.Views.Maui.Controls;
using SkiaSharp.Views.Maui;
using Microsoft.Maui.Layouts;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Iskra.Android;

internal sealed class MobileGamePage : ContentPage
{
    private readonly MobileGameRuntime runtime;
    private readonly SKCanvasView canvas = new() { EnableTouchEvents = true, IgnorePixelScaling = true };
    private readonly CancellationTokenSource lifetime = new();
    private readonly AbsoluteLayout overlay = new();
    private readonly BoxView pauseScrim = new() { BackgroundColor = Color.FromArgb("#730B0A09"), IsVisible = false, InputTransparent = true };
    private readonly Border pausePanel;
    private readonly Border debugPanel;
    private readonly VerticalStackLayout outputRows = new() { Spacing = 6 };
    private readonly VerticalStackLayout variableRows = new() { Spacing = 4 };
    private readonly ScrollView outputScroll;
    private readonly ScrollView variableScroll;
    private readonly Button outputTab;
    private readonly Button variablesTab;
    private readonly Label debugTabTitle;
    private readonly Grid debugContent;
    private Button axesButton = null!;
    private bool isPaused;
    private bool pauseMenuVisible;
    private bool debugVisible;
    private bool showAxes;
    private double debugX;
    private double debugY;
    private double debugWidth = 420;
    private double debugHeight = 360;
    private double panStartX;
    private double panStartY;
    private double resizeStartWidth;
    private double resizeStartHeight;
    private double pendingResizeWidth;
    private double pendingResizeHeight;
    private bool resizeFramePending;
    private bool showingVariables = true;
    private bool disposed;
    private bool previewSaved;
    private bool lifecycleSubscribed;
    private bool variableRefreshPending;
    private readonly ProjectStore pageProjectStore;
    private readonly IskraProject pageProject;
    private readonly IskraScene pageScene;
    private readonly List<string> printBuffer = [];
    private IReadOnlyList<KeyValuePair<string, string>> latestVariables = [];
#if ANDROID
    private global::Android.Content.PM.ScreenOrientation? previousOrientation;
#endif

    public MobileGamePage(ProjectStore projectStore, IskraProject project, IskraScene scene, CompiledProject compiled, RuntimeSceneSnapshot? snapshot)
    {
        pageProjectStore = projectStore;
        pageProject = project;
        pageScene = scene;
        runtime = new MobileGameRuntime(projectStore, project, scene, compiled, snapshot, Invalidate, AppendPrint, RefreshVariables,
            question => DisplayPromptAsync("Вопрос", question, "Готово", "Отмена"));
        runtime.SceneNavigation += async (sceneId, resume, snapshotForScene) =>
        {
            var target = project.Scenes.FirstOrDefault(candidate => candidate.Id == sceneId);
            if (target is null) return;
            await Navigation.PushAsync(new MobileGamePage(projectStore, project, target, compiled, snapshotForScene));
        };
        BackgroundColor = Colors.Black;
        NavigationPage.SetHasNavigationBar(this, false);
        canvas.PaintSurface += DrawScene;
        canvas.Touch += HandleTouch;
        outputScroll = new ScrollView { Content = outputRows, VerticalScrollBarVisibility = ScrollBarVisibility.Always };
        variableScroll = new ScrollView { Content = variableRows, VerticalScrollBarVisibility = ScrollBarVisibility.Always };
        outputTab = DebugTab("Вывод");
        variablesTab = DebugTab("Переменные");
        debugTabTitle = MobileUi.Text("Debug menu", 16, true, Colors.White);
        debugContent = new Grid();
        debugContent.Add(variableScroll);
        debugContent.Add(outputScroll);
        outputScroll.IsVisible = false;
        debugPanel = CreateDebugPanel();
        pausePanel = CreatePausePanel();
        overlay.Children.Add(pauseScrim);
        AbsoluteLayout.SetLayoutBounds(pauseScrim, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(pauseScrim, AbsoluteLayoutFlags.All);
        overlay.Children.Add(debugPanel);
        overlay.Children.Add(pausePanel);
        overlay.IsVisible = false;

        var root = new Grid { RowDefinitions = new RowDefinitionCollection(new RowDefinition(GridLength.Star)) };
        root.Add(canvas, 0, 0);
        root.Add(overlay, 0, 0);
        var closeButton = new Button
        {
            Text = "×",
            FontSize = 22,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#66000000"),
            BorderColor = Colors.Transparent,
            BorderWidth = 0,
            CornerRadius = 22,
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 0,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 12, 12, 0)
        };
        SemanticProperties.SetDescription(closeButton, "Закрыть игру");
        closeButton.Clicked += async (_, _) => await Navigation.PopModalAsync();
        root.Add(closeButton, 0, 0);
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        Loaded += (_, _) => SubscribeLifecycle();
        root.SizeChanged += (_, _) =>
        {
            ConstrainDebugPanel();
            UpdatePausePanelLayout();
        };
        Content = root;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SubscribeLifecycle();
        KeyboardBridge.Attach(runtime);
#if ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is not null)
        {
            previousOrientation = activity.RequestedOrientation;
            activity.RequestedOrientation = runtime.Orientation == ProjectOrientation.Horizontal
                ? global::Android.Content.PM.ScreenOrientation.Landscape
                : global::Android.Content.PM.ScreenOrientation.Portrait;
        }
#endif
    }

    protected override void OnDisappearing()
    {
        disposed = true;
        KeyboardBridge.Detach(runtime);
        if (Window is { } window && lifecycleSubscribed)
        {
            window.Stopped -= Window_Stopped;
            window.Resumed -= Window_Resumed;
            lifecycleSubscribed = false;
        }
        lifetime.Cancel();
        runtime.Dispose();
#if ANDROID
        if (previousOrientation is { } orientation && Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is { } activity)
            activity.RequestedOrientation = orientation;
#endif
        base.OnDisappearing();
    }

    private void SubscribeLifecycle()
    {
        if (lifecycleSubscribed || Window is not { } window) return;
        window.Stopped += Window_Stopped;
        window.Resumed += Window_Resumed;
        lifecycleSubscribed = true;
    }

    private void Window_Stopped(object? sender, EventArgs e)
    {
        if (!isPaused)
        {
            SetPaused(true);
        }
    }

    private void Window_Resumed(object? sender, EventArgs e)
    {
        if (!pauseMenuVisible && !debugVisible)
        {
            SetPaused(true);
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (!isPaused)
        {
            SetPaused(true);
        }
        else if (debugVisible)
        {
            SetDebugVisible(false);
        }
        else
        {
            SetPaused(false);
        }
        return true;
    }

    private void DrawScene(object? sender, SKPaintSurfaceEventArgs e)
    {
        e.Surface.Canvas.Clear(SKColors.Black);
        if (!disposed)
        {
            runtime.Draw(e.Surface.Canvas, e.Info.Width, e.Info.Height);
        }
        if (!previewSaved && !disposed)
        {
            previewSaved = true;
            SaveScenePreview(e.Surface);
        }
    }

    private void SaveScenePreview(SKSurface surface)
    {
        try
        {
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 80);
            var path = new ProjectContentStore(pageProjectStore).GetScenePreviewPath(pageProject, pageScene);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, data.ToArray());
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось сохранить превью сцены.", exception);
        }
    }

    private void HandleTouch(object? sender, SKTouchEventArgs e)
    {
        if (!disposed && !isPaused && (e.ActionType is SKTouchAction.Pressed or SKTouchAction.Released or SKTouchAction.Moved))
            runtime.HandlePointer(e.ActionType, e.Location, canvas.CanvasSize.Width, canvas.CanvasSize.Height, lifetime.Token);
        e.Handled = true;
    }

    private void Invalidate()
    {
        if (disposed) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!disposed)
            {
                canvas.InvalidateSurface();
            }
        });
    }

    private void AppendPrint(string message)
    {
        lock (printBuffer)
        {
            printBuffer.Add(message);
            while (printBuffer.Count > 500) printBuffer.RemoveAt(0);
        }
        if (!debugVisible) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (disposed || !debugVisible) return;
            outputRows.Add(MobileUi.Text(message, 14, color: Colors.White));
            while (outputRows.Children.Count > 500) outputRows.Children.RemoveAt(0);
            if (!showingVariables) _ = outputScroll.ScrollToAsync(0, outputScroll.ContentSize.Height, false);
        });
    }

    private void RefreshVariables(IReadOnlyList<KeyValuePair<string, string>> snapshot)
    {
        latestVariables = snapshot;
        if (!debugVisible || disposed) return;
        if (variableRefreshPending) return;
        variableRefreshPending = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () =>
        {
            variableRefreshPending = false;
            if (!debugVisible || disposed) return;
            BuildVariableRows();
        });
    }

    private void BuildVariableRows()
    {
        variableRows.Clear();
        if (latestVariables.Count == 0)
        {
            variableRows.Add(MobileUi.Text("В проекте пока нет переменных.", 14, color: Color.FromArgb("#AAA59C")));
            return;
        }
        foreach (var variable in latestVariables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)),
                ColumnSpacing = 12,
                Padding = new Thickness(0, 4)
            };
            row.Add(MobileUi.Text(variable.Key, 14, color: Color.FromArgb("#DFDBD3")), 0, 0);
            row.Add(MobileUi.Text(variable.Value, 14, true, Colors.White), 1, 0);
            variableRows.Add(row);
        }
    }

    private void RebuildOutputRows()
    {
        outputRows.Clear();
        string[] messages;
        lock (printBuffer)
        {
            messages = printBuffer.ToArray();
        }
        if (messages.Length == 0)
        {
            outputRows.Add(MobileUi.Text("Здесь появится текст из команды «Печать».", 14, color: Color.FromArgb("#AAA59C")));
            return;
        }
        foreach (var message in messages)
        {
            outputRows.Add(MobileUi.Text(message, 14, color: Colors.White));
        }
    }

    private Border CreatePausePanel()
    {
        var continueBtn = new Button
        {
            BackgroundColor = MobileTheme.Accent,
            BorderColor = Colors.Transparent,
            BorderWidth = 0,
            CornerRadius = 14,
            HeightRequest = 56,
            HorizontalOptions = LayoutOptions.Fill
        };
        var continueContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(28), new ColumnDefinition(GridLength.Star)),
            ColumnSpacing = 10,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        continueContent.Add(new GraphicsView { Drawable = new PlayIconDrawable(), WidthRequest = 28, HeightRequest = 28, InputTransparent = true }, 0, 0);
        continueContent.Add(new Label { Text = "Продолжить", FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, VerticalTextAlignment = TextAlignment.Center, InputTransparent = true }, 1, 0);
        var continueGrid = new Grid { HeightRequest = 56, HorizontalOptions = LayoutOptions.Fill };
        continueGrid.Add(continueBtn);
        continueGrid.Add(continueContent);
        continueBtn.Clicked += (_, _) => ContinueFromOverlay();
        SemanticProperties.SetDescription(continueBtn, "Продолжить игру");

        var debugButton = PauseIconAction(new DebugIconDrawable(), "Debug", () => SetDebugVisible(!debugVisible), out _);
        var axesAction = PauseIconAction(new AxesIconDrawable(), "Оси", ToggleAxes, out var axesButtonControl);
        axesButton = axesButtonControl;
        var exitButton = PauseIconAction(new ExitIconDrawable(), "Выйти", async () => await Navigation.PopModalAsync(), out _, Color.FromArgb("#FF3D1A1A"), Color.FromArgb("#FF7A3030"));

        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)),
            ColumnSpacing = 10
        };
        actions.Add(debugButton, 0, 0);
        actions.Add(axesAction, 1, 0);
        actions.Add(exitButton, 2, 0);

        var titleRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(24), new ColumnDefinition(GridLength.Star)),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 0, 0, 4)
        };
        titleRow.Add(new GraphicsView { Drawable = new PauseIconDrawable(), WidthRequest = 24, HeightRequest = 24, VerticalOptions = LayoutOptions.Center }, 0, 0);
        titleRow.Add(MobileUi.Text("Пауза", 18, true, Colors.White), 1, 0);

        var body = new VerticalStackLayout
        {
            Spacing = 14,
            Children = { titleRow, continueGrid, actions }
        };
        return new Border
        {
            Content = body,
            Padding = new Thickness(20, 18),
            BackgroundColor = Color.FromArgb("#F0211F1C"),
            Stroke = Color.FromArgb("#FF49443E"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 24 },
            Margin = new Thickness(16, 0, 16, 28),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            IsVisible = false
        };
    }

    private View PauseIconAction(IDrawable icon, string label, Action action, out Button button, Color? bgColor = null, Color? borderColor = null)
    {
        button = new Button
        {
            BackgroundColor = bgColor ?? Color.FromArgb("#FF2D2A27"),
            BorderColor = borderColor ?? Color.FromArgb("#FF49443E"),
            BorderWidth = 1,
            CornerRadius = 14,
            HeightRequest = 76,
            HorizontalOptions = LayoutOptions.Fill
        };
        SemanticProperties.SetDescription(button, label);
        var iconView = new GraphicsView { Drawable = icon, WidthRequest = 28, HeightRequest = 28, InputTransparent = true };
        var labelView = new Label { Text = label, FontSize = 12, TextColor = Color.FromArgb("#DFDBD3"), HorizontalTextAlignment = TextAlignment.Center, InputTransparent = true };
        var stack = new VerticalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
            Children = { iconView, labelView }
        };
        var grid = new Grid { HorizontalOptions = LayoutOptions.Fill };
        grid.Add(button);
        grid.Add(stack);
        button.Clicked += (_, _) => action();
        return grid;
    }

    private Border CreateDebugPanel()
    {
        outputTab.Clicked += (_, _) => SelectDebugTab(showVariables: false);
        variablesTab.Clicked += (_, _) => SelectDebugTab(showVariables: true);
        var tabs = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)),
            ColumnSpacing = 8
        };
        tabs.Add(outputTab, 0, 0);
        tabs.Add(variablesTab, 1, 0);
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(48)),
            HeightRequest = 48
        };
        header.Add(debugTabTitle, 0, 0);
        var close = MobileUi.IconButton("×", "Закрыть Debug menu", (_, _) => SetDebugVisible(false));
        close.TextColor = Colors.White;
        header.Add(close, 1, 0);
        var drag = new PanGestureRecognizer();
        drag.PanUpdated += DragDebugPanel;
        debugTabTitle.GestureRecognizers.Add(drag);
        var resizeGrip = new Border
        {
            Content = MobileUi.Text("◢", 17, true, Color.FromArgb("#FFAAA59C")),
            WidthRequest = 48,
            HeightRequest = 40,
            Padding = new Thickness(8),
            BackgroundColor = Colors.Transparent,
            Stroke = Colors.Transparent,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center
        };
        AutomationProperties.SetName(resizeGrip, "Изменить размер панели");
        var resize = new PanGestureRecognizer();
        resize.PanUpdated += ResizeDebugPanel;
        resizeGrip.GestureRecognizers.Add(resize);
        var body = new Grid
        {
            RowDefinitions = new RowDefinitionCollection(new RowDefinition(48), new RowDefinition(48), new RowDefinition(GridLength.Star), new RowDefinition(40)),
            RowSpacing = 6
        };
        body.Add(header, 0, 0);
        body.Add(tabs, 0, 1);
        body.Add(debugContent, 0, 2);
        body.Add(resizeGrip, 0, 3);
        return new Border
        {
            Content = body,
            Padding = new Thickness(12),
            BackgroundColor = Color.FromArgb("#FF211F1C"),
            Stroke = Color.FromArgb("#FF49443E"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            IsVisible = false
        };
    }

    private Button DebugTab(string text) => new()
    {
        Text = text,
        FontSize = 14,
        MinimumHeightRequest = 44,
        BackgroundColor = Colors.Transparent,
        TextColor = Color.FromArgb("#DFDBD3"),
        CornerRadius = 8,
        Padding = new Thickness(10, 6)
    };

    private void SetPaused(bool paused)
    {
        isPaused = paused;
        pauseMenuVisible = paused;
        if (!paused) debugVisible = false;
        runtime.SetPaused(paused);
        runtime.SetDebugAudioActive(false);
        UpdateOverlayVisibility();
        if (paused)
        {
            latestVariables = runtime.GetVariablesSnapshot();
            BuildVariableRows();
        }
        Invalidate();
    }

    private void SetDebugVisible(bool visible)
    {
        debugVisible = visible && pauseMenuVisible;
        if (debugVisible && isPaused)
        {
            isPaused = false;
            runtime.SetPaused(false);
        }
        else if (!debugVisible && pauseMenuVisible && !isPaused)
        {
            isPaused = true;
            runtime.SetPaused(true);
        }
        runtime.SetDebugAudioActive(debugVisible);
        UpdateOverlayVisibility();
        if (debugVisible)
        {
            SelectDebugTab(showVariables: false);
            RebuildOutputRows();
            latestVariables = runtime.GetVariablesSnapshot();
            BuildVariableRows();
            debugX = 12;
            debugY = 12;
            ConstrainDebugPanel();
            ApplyDebugPanelBounds();
        }
    }

    private void ContinueFromOverlay()
    {
        debugVisible = false;
        pauseMenuVisible = false;
        isPaused = false;
        runtime.SetDebugAudioActive(false);
        runtime.SetPaused(false);
        UpdateOverlayVisibility();
        Invalidate();
    }

    private void UpdateOverlayVisibility()
    {
        var visible = pauseMenuVisible || debugVisible;
        overlay.IsVisible = visible;
        pauseScrim.IsVisible = visible;
        pausePanel.IsVisible = pauseMenuVisible;
        debugPanel.IsVisible = debugVisible;
        UpdatePausePanelLayout();
    }

    private void SelectDebugTab(bool showVariables)
    {
        showingVariables = showVariables;
        variableScroll.IsVisible = showVariables;
        outputScroll.IsVisible = !showVariables;
        variablesTab.BackgroundColor = showVariables ? MobileTheme.Accent : Colors.Transparent;
        outputTab.BackgroundColor = showVariables ? Colors.Transparent : MobileTheme.Accent;
        debugTabTitle.Text = showVariables ? "Debug menu · Переменные" : "Debug menu · Вывод";
        if (showVariables)
        {
            BuildVariableRows();
        }
    }

    private void ToggleAxes()
    {
        showAxes = !showAxes;
        runtime.ShowAxes = showAxes;
        axesButton.BackgroundColor = showAxes ? MobileTheme.Accent : Color.FromArgb("#FF2D2A27");
        axesButton.BorderColor = showAxes ? MobileTheme.Accent : Color.FromArgb("#FF49443E");
        Invalidate();
    }

    private void DragDebugPanel(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType == GestureStatus.Started)
        {
            panStartX = debugX;
            panStartY = debugY;
        }
        else if (e.StatusType == GestureStatus.Running)
        {
            var targetX = Math.Clamp(panStartX + e.TotalX, 12, Math.Max(12, overlay.Width - debugWidth - 12));
            var targetY = Math.Clamp(panStartY + e.TotalY, 12, Math.Max(12, overlay.Height - debugHeight - (debugVisible ? 260 : 84)));
            debugPanel.TranslationX = targetX - debugX;
            debugPanel.TranslationY = targetY - debugY;
        }
        else if (e.StatusType is GestureStatus.Completed or GestureStatus.Canceled)
        {
            debugX += debugPanel.TranslationX;
            debugY += debugPanel.TranslationY;
            debugPanel.TranslationX = 0;
            debugPanel.TranslationY = 0;
            ConstrainDebugPanel();
        }
    }

    private void ResizeDebugPanel(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType == GestureStatus.Started)
        {
            resizeStartWidth = debugWidth;
            resizeStartHeight = debugHeight;
            pendingResizeWidth = debugWidth;
            pendingResizeHeight = debugHeight;
        }
        else if (e.StatusType == GestureStatus.Running)
        {
            pendingResizeWidth = resizeStartWidth + e.TotalX;
            pendingResizeHeight = resizeStartHeight + e.TotalY;
            ScheduleDebugPanelResize();
        }
        else if (e.StatusType is GestureStatus.Completed or GestureStatus.Canceled)
        {
            debugWidth = pendingResizeWidth;
            debugHeight = pendingResizeHeight;
            ConstrainDebugPanel();
        }
    }

    private void ScheduleDebugPanelResize()
    {
        if (resizeFramePending) return;
        resizeFramePending = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () =>
        {
            resizeFramePending = false;
            debugWidth = pendingResizeWidth;
            debugHeight = pendingResizeHeight;
            ConstrainDebugPanel();
        });
    }

    private void ConstrainDebugPanel()
    {
        if (overlay.Width <= 0 || overlay.Height <= 0) return;
        var maximumWidth = Math.Max(1, overlay.Width - 24);
        var reservedBottom = debugVisible ? 260 : 96;
        var maximumHeight = Math.Max(1, overlay.Height - reservedBottom);
        debugWidth = Math.Clamp(debugWidth, Math.Min(280, maximumWidth), maximumWidth);
        debugHeight = Math.Clamp(debugHeight, Math.Min(220, maximumHeight), maximumHeight);
        debugX = Math.Clamp(debugX, 12, Math.Max(12, overlay.Width - debugWidth - 12));
        debugY = Math.Clamp(debugY, 12, Math.Max(12, overlay.Height - debugHeight - (debugVisible ? 260 : 84)));
        ApplyDebugPanelBounds();
    }

    private void ApplyDebugPanelBounds()
    {
        AbsoluteLayout.SetLayoutBounds(debugPanel, new Rect(debugX, debugY, debugWidth, debugHeight));
        AbsoluteLayout.SetLayoutFlags(debugPanel, AbsoluteLayoutFlags.None);
    }

    private void UpdatePausePanelLayout()
    {
        if (overlay.Width <= 0 || overlay.Height <= 0) return;
        if (debugVisible)
        {
            var narrowWidth = Math.Max(1, Math.Min(320, overlay.Width - 32));
            if (Math.Abs(pausePanel.WidthRequest - narrowWidth) > 1) pausePanel.WidthRequest = narrowWidth;
            AbsoluteLayout.SetLayoutBounds(pausePanel, new Rect(1, 1, -1, -1));
            AbsoluteLayout.SetLayoutFlags(pausePanel, AbsoluteLayoutFlags.PositionProportional);
            return;
        }
        var width = Math.Max(1, Math.Min(480, overlay.Width - 32));
        if (Math.Abs(pausePanel.WidthRequest - width) > 1) pausePanel.WidthRequest = width;
        AbsoluteLayout.SetLayoutBounds(pausePanel, new Rect(0.5, 1, -1, -1));
        AbsoluteLayout.SetLayoutFlags(pausePanel, AbsoluteLayoutFlags.PositionProportional);
    }
}

internal sealed class PlayIconDrawable : IDrawable
{
    public void Draw(ICanvas canvas, RectF r)
    {
        canvas.FillColor = Colors.White;
        float cx = r.Width / 2, cy = r.Height / 2;
        float h = r.Height * 0.52f, w = h * 0.87f;
        var path = new PathF();
        path.MoveTo(cx - w * 0.38f, cy - h / 2);
        path.LineTo(cx + w * 0.62f, cy);
        path.LineTo(cx - w * 0.38f, cy + h / 2);
        path.Close();
        canvas.FillPath(path);
    }
}

internal sealed class PauseIconDrawable : IDrawable
{
    public void Draw(ICanvas canvas, RectF r)
    {
        canvas.FillColor = Color.FromArgb("#DFDBD3");
        float bw = r.Width * 0.22f, bh = r.Height * 0.62f;
        float gap = r.Width * 0.18f;
        float cx = r.Width / 2, cy = r.Height / 2;
        canvas.FillRoundedRectangle(cx - gap / 2 - bw, cy - bh / 2, bw, bh, 2);
        canvas.FillRoundedRectangle(cx + gap / 2, cy - bh / 2, bw, bh, 2);
    }
}

internal sealed class DebugIconDrawable : IDrawable
{
    public void Draw(ICanvas canvas, RectF r)
    {
        canvas.StrokeColor = Color.FromArgb("#DFDBD3");
        canvas.StrokeSize = 2f;
        canvas.StrokeLineCap = LineCap.Round;
        float m = r.Width * 0.14f;
        float lx = m, rx = r.Width - m;
        float y1 = r.Height * 0.28f, y2 = r.Height * 0.50f, y3 = r.Height * 0.72f;
        canvas.DrawLine(lx, y1, rx, y1);
        canvas.DrawLine(lx, y2, rx * 0.68f, y2);
        canvas.DrawLine(lx, y3, rx * 0.82f, y3);
    }
}

internal sealed class AxesIconDrawable : IDrawable
{
    public void Draw(ICanvas canvas, RectF r)
    {
        canvas.StrokeColor = Color.FromArgb("#DFDBD3");
        canvas.StrokeSize = 2f;
        canvas.StrokeLineCap = LineCap.Round;
        float cx = r.Width / 2, cy = r.Height / 2;
        float arm = r.Width * 0.36f;
        canvas.DrawLine(cx - arm, cy, cx + arm, cy);
        canvas.DrawLine(cx, cy - arm, cx, cy + arm);
        canvas.FillColor = Color.FromArgb("#DFDBD3");
        canvas.FillEllipse(cx - 2, cy - 2, 4, 4);
        float ah = arm * 0.28f, aw = arm * 0.18f;
        var px = new PathF();
        px.MoveTo(cx + arm, cy);
        px.LineTo(cx + arm - ah, cy - aw);
        px.LineTo(cx + arm - ah, cy + aw);
        px.Close();
        canvas.FillPath(px);
        var py = new PathF();
        py.MoveTo(cx, cy - arm);
        py.LineTo(cx - aw, cy - arm + ah);
        py.LineTo(cx + aw, cy - arm + ah);
        py.Close();
        canvas.FillPath(py);
    }
}

internal sealed class ExitIconDrawable : IDrawable
{
    public void Draw(ICanvas canvas, RectF r)
    {
        canvas.StrokeColor = Color.FromArgb("#F08080");
        canvas.StrokeSize = 2f;
        canvas.StrokeLineCap = LineCap.Round;
        float m = r.Width * 0.16f;
        float cx = r.Width / 2, cy = r.Height / 2;
        float boxR = r.Width * 0.34f;
        canvas.DrawRoundedRectangle(m, m, boxR * 2 - m * 0.4f, r.Height - m * 2, 3);
        float arrowX = cx + r.Width * 0.10f;
        float arrowLen = r.Width * 0.32f;
        float ah = arrowLen * 0.45f;
        canvas.DrawLine(arrowX, cy, arrowX + arrowLen, cy);
        canvas.DrawLine(arrowX + arrowLen - ah * 0.7f, cy - ah * 0.55f, arrowX + arrowLen, cy);
        canvas.DrawLine(arrowX + arrowLen - ah * 0.7f, cy + ah * 0.55f, arrowX + arrowLen, cy);
        float tw = r.Width * 0.10f;
        float ty1 = cy - r.Height * 0.22f, ty2 = cy + r.Height * 0.22f;
        canvas.DrawLine(m + boxR - tw, ty1, m + boxR - tw, ty2);
    }
}
