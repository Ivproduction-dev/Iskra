using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace IskraStudio;

public partial class MainWindow
{
    private readonly ProjectRuntime projectRuntime;
    private readonly Random runtimeRandom = new();
    private readonly string[] runtimeFacts =
    [
        "У осьминога три сердца, а кровь голубого цвета.",
        "Самый короткий день в году всё равно длится 24 часа.",
        "Первые игровые пиксели помещались в несколько байтов памяти.",
        "Свет от Солнца достигает Земли примерно за восемь минут.",
        "Бананы с ботанической точки зрения считаются ягодами.",
        "В космосе звук не распространяется без среды.",
        "Клавиатура QWERTY появилась раньше современных компьютеров.",
        "Медоносная пчела может запомнить маршрут до цветка."
    ];
    private readonly List<RuntimeDiagnostic> projectDiagnostics = [];
    private DispatcherTimer? runtimeFactTimer;
    private DispatcherTimer? runtimeToastTimer;
    private DispatcherTimer? diagnosticScrollTimer;
    private GameWindow? runningGameWindow;
    private CancellationTokenSource? runningGameCancellation;
    private bool openingDiagnosticFromLaunch;

    private async void RunProject_Click(object sender, RoutedEventArgs e)
    {
        if (activeProject is null || showingPainting || runningGameWindow is not null)
        {
            return;
        }

        FlushScriptSave();
        var project = activeProject;
        AppLog.Write($"Запуск проекта начат: {project.Name}.");
        EditorRunOverlay.Visibility = Visibility.Collapsed;
        SetRuntimeLaunchState("Проверяем все сценарии проекта…", loading: true);
        RuntimeLaunchOverlay.Visibility = Visibility.Visible;
        UpdateWorkspaceModalLayer();
        StartRuntimeFacts();
        try
        {
            var progress = new Progress<string>(message => SetRuntimeLaunchState(message, loading: true));
            var compilation = await Task.Run(() => projectRuntime.Compile(project, progress: progress));
            if (!ReferenceEquals(activeProject, project))
            {
                RuntimeLaunchOverlay.Visibility = Visibility.Collapsed;
                EditorRunOverlay.Visibility = Visibility.Collapsed;
                UpdateWorkspaceModalLayer();
                return;
            }

            SetProjectDiagnostics(compilation.Diagnostics);
            if (compilation.HasErrors || compilation.Project is null)
            {
                StopRuntimeFacts();
                RuntimeLaunchOverlay.Visibility = Visibility.Collapsed;
                EditorRunOverlay.Visibility = Visibility.Collapsed;
                UpdateWorkspaceModalLayer();
                openingDiagnosticFromLaunch = true;
                OpenDiagnosticsDialog();
                return;
            }

            var scene = activeScene is not null && project.Scenes.Any(item => item.Id == activeScene.Id)
                ? project.Scenes.First(item => item.Id == activeScene.Id)
                : project.Scenes[0];
            runtimeDebugPanel.BeginRun();
            SetRuntimeLaunchState("Открываем игровое окно…", loading: true);
            var gameWindow = new GameWindow(project.Orientation) { Owner = this };
            PositionGameWindowBesideEditor(gameWindow);
            runningGameWindow = gameWindow;
            runningGameCancellation = new CancellationTokenSource();
            gameWindow.Closed += (_, _) =>
            {
                runningGameCancellation?.Cancel();
                runningGameCancellation?.Dispose();
                runningGameCancellation = null;
                runningGameWindow = null;
                StopRuntimeFacts();
                RuntimeLaunchOverlay.Visibility = Visibility.Collapsed;
                EditorRunOverlay.Visibility = Visibility.Collapsed;
                UpdateWorkspaceModalLayer();
            };
            gameWindow.Show();
            RuntimeLaunchOverlay.Visibility = Visibility.Collapsed;
            EditorRunStatus.Text = "Закройте окно игры, чтобы продолжить разработку";
            EditorRunOverlay.Visibility = Visibility.Visible;
            var runtimeTask = projectRuntime.RunSceneAsync(compilation.Project, scene, project, gameWindow.SceneSurface,
                runtimeDebugPanel.AddOutput,
                runtimeDebugPanel.UpdateVariables,
                runningGameCancellation.Token);
            _ = runtimeTask.ContinueWith(
                task => AppLog.Write("Выполнение сцены прервано из-за ошибки.", task.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
        catch (Exception exception)
        {
            StopRuntimeFacts();
            RuntimeLaunchOverlay.Visibility = Visibility.Collapsed;
            EditorRunOverlay.Visibility = Visibility.Collapsed;
            UpdateWorkspaceModalLayer();
            AppLog.Write("Не удалось запустить проект.", exception);
            SetProjectDiagnostics(
            [
                new RuntimeDiagnostic(RuntimeDiagnosticSeverity.Error, "Не удалось подготовить проект к запуску. Подробности записаны в журнал.",
                    activeScene?.Name ?? "Проект", "", 0, activeScene?.Id ?? Guid.Empty, Guid.Empty)
            ]);
            openingDiagnosticFromLaunch = true;
            OpenDiagnosticsDialog();
        }
    }

    private void PositionGameWindowBesideEditor(GameWindow gameWindow)
    {
        var workArea = SystemParameters.WorkArea;
        var debugStripWidth = 420d;
        var gameLeft = Math.Clamp(Left + debugStripWidth, workArea.Left, workArea.Right - gameWindow.MinWidth);
        gameWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        gameWindow.Left = gameLeft;
        gameWindow.Top = Math.Clamp(Top, workArea.Top, workArea.Bottom - Math.Min(gameWindow.Height, workArea.Height));
        gameWindow.Width = Math.Min(gameWindow.Width, workArea.Right - gameLeft);
        gameWindow.Height = Math.Min(gameWindow.Height, workArea.Height);
    }

    private void SetRuntimeLaunchState(string message, bool loading)
    {
        RuntimeLaunchStatus.Text = message;
        RuntimeLaunchFact.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        RuntimeLaunchProgress.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        RuntimeLaunchLogo.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        RuntimeLaunchCard.Background = loading ? BrushFor("PanelBrush") : BrushFor("CanvasBrush");
        RuntimeLaunchCard.BorderBrush = loading ? BrushFor("HairlineBrush") : Brushes.Transparent;
        RuntimeLaunchOverlay.Background = loading ? new SolidColorBrush(Color.FromArgb(128, 23, 20, 18)) : Brushes.Transparent;
    }

    private void StartRuntimeFacts()
    {
        RuntimeLaunchFact.Text = runtimeFacts[runtimeRandom.Next(runtimeFacts.Length)];
        runtimeFactTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        runtimeFactTimer.Tick -= RuntimeFactTimer_Tick;
        runtimeFactTimer.Tick += RuntimeFactTimer_Tick;
        runtimeFactTimer.Start();
    }

    private void RuntimeFactTimer_Tick(object? sender, EventArgs e) =>
        RuntimeLaunchFact.Text = runtimeFacts[runtimeRandom.Next(runtimeFacts.Length)];

    private void StopRuntimeFacts() => runtimeFactTimer?.Stop();

    private void SetProjectDiagnostics(IEnumerable<RuntimeDiagnostic> diagnostics)
    {
        projectDiagnostics.Clear();
        projectDiagnostics.AddRange(diagnostics);
        var errors = projectDiagnostics.Count(item => item.Severity == RuntimeDiagnosticSeverity.Error);
        var warnings = projectDiagnostics.Count(item => item.Severity == RuntimeDiagnosticSeverity.Warning);
        DiagnosticsBadgeButton.Visibility = activeProject is not null && !showingPainting && projectDiagnostics.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsBadgeButton.Background = BrushFor(errors > 0 ? "ErrorBrush" : "WarningBrush");
        DiagnosticsBadgeButton.Content = errors > 0
            ? $"!  {errors + warnings}"
            : $"!  {warnings}";
        DiagnosticsBadgeButton.ToolTip = errors > 0
            ? $"Ошибки и предупреждения: {errors} и {warnings}"
            : $"Предупреждения: {warnings}";
    }

    private async void RefreshProjectDiagnostics()
    {
        var project = activeProject;
        if (project is null || showingPainting)
        {
            SetProjectDiagnostics([]);
            return;
        }
        try
        {
            var result = await Task.Run(() => projectRuntime.Compile(project, createCache: false));
            if (ReferenceEquals(activeProject, project) && !showingPainting)
            {
                SetProjectDiagnostics(result.Diagnostics);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Не удалось обновить диагностику проекта.", exception);
        }
    }

    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e) => OpenDiagnosticsDialog();

    private void UpdateWorkspaceModalLayer()
    {
        var modalVisible = RuntimeLaunchOverlay.Visibility == Visibility.Visible || DiagnosticsOverlay.Visibility == Visibility.Visible;
        Panel.SetZIndex(WorkspaceView, modalVisible ? 30 : 0);
    }

    private void OpenDiagnosticsDialog()
    {
        var errors = projectDiagnostics.Count(item => item.Severity == RuntimeDiagnosticSeverity.Error);
        var warnings = projectDiagnostics.Count(item => item.Severity == RuntimeDiagnosticSeverity.Warning);
        DiagnosticsTitle.Text = openingDiagnosticFromLaunch && errors > 0
            ? "Произошла ошибка при компиляции"
            : "Диагностика проекта";
        DiagnosticsSubtitle.Text = errors > 0
            ? "Исправь ошибки, чтобы запустить игру. Выбери запись, чтобы открыть нужный скрипт."
            : "Выбери предупреждение, чтобы открыть соответствующий скрипт.";
        DiagnosticsCounts.Text = errors > 0 && warnings > 0
            ? $"Ошибки: {errors}  ·  Предупреждения: {warnings}"
            : errors > 0 ? $"Ошибки: {errors}" : $"Предупреждения: {warnings}";
        RuntimeDiagnosticsList.SelectedItem = null;
        RuntimeDiagnosticsList.ItemsSource = null;
        RuntimeDiagnosticsList.ItemsSource = projectDiagnostics.ToList();
        DiagnosticsOverlay.Visibility = Visibility.Visible;
        UpdateWorkspaceModalLayer();
        openingDiagnosticFromLaunch = false;
    }

    private void CloseDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsOverlay.Visibility = Visibility.Collapsed;
        UpdateWorkspaceModalLayer();
    }

    private void DiagnosticsOverlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, DiagnosticsOverlay))
        {
            DiagnosticsOverlay.Visibility = Visibility.Collapsed;
            UpdateWorkspaceModalLayer();
        }
    }

    private void RuntimeDiagnosticsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RuntimeDiagnosticsList.SelectedItem is not RuntimeDiagnostic diagnostic || activeProject is null || diagnostic.Line <= 0)
        {
            return;
        }
        var scene = activeProject.Scenes.FirstOrDefault(item => item.Id == diagnostic.SceneId);
        IskraObject? item = diagnostic.ObjectId == IskraObject.BackgroundId
            ? new IskraObject { Id = IskraObject.BackgroundId, Name = "Фон" }
            : scene?.Objects.FirstOrDefault(value => value.Id == diagnostic.ObjectId);
        if (scene is null || item is null)
        {
            return;
        }
        DiagnosticsOverlay.Visibility = Visibility.Collapsed;
        UpdateWorkspaceModalLayer();
        ShowObjectDetail(scene, item, ObjectDetailTab.Scripts);
        Dispatcher.BeginInvoke(() =>
        {
            if (detailScriptEditor is { } editor)
            {
                ScrollScriptToLineSmoothly(editor, diagnostic.Line);
                editor.Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    private void ScrollScriptToLineSmoothly(ICSharpCode.AvalonEdit.TextEditor editor, int line)
    {
        diagnosticScrollTimer?.Stop();
        if (editor.Document.LineCount == 0)
        {
            return;
        }

        var targetLine = Math.Clamp(line, 1, editor.Document.LineCount);
        var textView = editor.TextArea.TextView;
        var targetOffset = textView.GetVisualTopByDocumentLine(targetLine) - editor.ViewportHeight * 0.35;
        targetOffset = Math.Clamp(targetOffset, 0, Math.Max(0, editor.ExtentHeight - editor.ViewportHeight));
        var startOffset = editor.VerticalOffset;
        if (Math.Abs(targetOffset - startOffset) < 1)
        {
            editor.ScrollToLine(targetLine);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        diagnosticScrollTimer = timer;
        timer.Tick += (_, _) =>
        {
            var progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / 420, 0, 1);
            var easedProgress = 1 - Math.Pow(1 - progress, 3);
            editor.ScrollToVerticalOffset(startOffset + (targetOffset - startOffset) * easedProgress);
            if (progress < 1)
            {
                return;
            }

            timer.Stop();
            if (ReferenceEquals(diagnosticScrollTimer, timer))
            {
                diagnosticScrollTimer = null;
            }
        };
        timer.Start();
    }

    private void ClearProjectCodeCache_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceToolsPopup.IsOpen = false;
        if (activeProject is null)
        {
            return;
        }
        if (projectRuntime.ClearCache(activeProject, out var error))
        {
            ShowRuntimeToast("Кэш кода очищен");
        }
        else
        {
            ShowRuntimeToast(error ?? "Не удалось очистить кэш кода");
        }
    }

    private void ShowRuntimeToast(string message)
    {
        RuntimeToastText.Text = message;
        RuntimeToastOverlay.Visibility = Visibility.Visible;
        runtimeToastTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        runtimeToastTimer.Stop();
        runtimeToastTimer.Tick -= RuntimeToastTimer_Tick;
        runtimeToastTimer.Tick += RuntimeToastTimer_Tick;
        runtimeToastTimer.Start();
    }

    private void RuntimeToastTimer_Tick(object? sender, EventArgs e)
    {
        runtimeToastTimer?.Stop();
        RuntimeToastOverlay.Visibility = Visibility.Collapsed;
    }
}
