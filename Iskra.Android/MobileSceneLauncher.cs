using IskraStudio;

namespace Iskra.Android;

internal static class MobileSceneLauncher
{
    public static async Task LaunchAsync(Page host, ProjectStore projectStore, IskraProject project, IskraScene? currentScene)
    {
        var selectedScene = await ChooseSceneAsync(host, project.Scenes, currentScene);
        if (selectedScene is null)
        {
            return;
        }

        RuntimeCompileResult compilation;
        try
        {
            var compiler = new ScriptCompiler(projectStore, new ProjectContentStore(projectStore));
            compilation = compiler.Compile(project, createCache: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MobileLog.Write("Не удалось проверить скрипты проекта.", exception);
            await host.DisplayAlertAsync("Не удалось проверить проект", "Проверь доступ к файлам проекта и попробуй ещё раз.", "Понятно");
            return;
        }

        if (compilation.HasErrors || compilation.Project is null)
        {
            var errors = compilation.Diagnostics
                .Where(item => item.Severity == RuntimeDiagnosticSeverity.Error)
                .Take(6)
                .Select(item => $"{item.SceneName} · {item.ObjectName}, строка {item.Line}: {item.Message}");
            await host.DisplayAlertAsync("Ошибки в проекте", string.Join("\n", errors), "Понятно");
            return;
        }

        var unsupported = AndroidScriptSupport.FindUnsupported(compilation.Project);
        if (unsupported.Count > 0)
        {
            var list = string.Join(", ", unsupported.Take(8));
            var proceed = await host.DisplayAlertAsync(
                "Не всё поддерживается",
                $"На Android пока не работают: {list}. Запустить проект?",
                "Запустить",
                "Отмена");
            if (!proceed)
            {
                return;
            }
        }

        await host.Navigation.PushModalAsync(new NavigationPage(new MobileGamePage(projectStore, project, selectedScene, compilation.Project, null)));
    }

    public static async Task<IskraScene?> ChooseSceneAsync(Page host, IReadOnlyList<IskraScene> scenes, IskraScene? currentScene)
    {
        if (scenes.Count == 0)
        {
            await host.DisplayAlertAsync("Нет сцен", "Создай сцену, чтобы запустить проект.", "Понятно");
            return null;
        }

        if (!SceneLaunchPolicy.RequiresSceneChoice(scenes, currentScene))
        {
            return scenes[0];
        }

        var picker = new ScenePickerPage(scenes, currentScene);
        await host.Navigation.PushModalAsync(new NavigationPage(picker));
        return await picker.Result;
    }
}

internal sealed class ScenePickerPage : ContentPage
{
    private readonly TaskCompletionSource<IskraScene?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IskraScene? currentScene;

    public ScenePickerPage(IReadOnlyList<IskraScene> scenes, IskraScene? currentScene)
    {
        this.currentScene = currentScene;
        Result = result.Task;
        Title = "Выбор сцены";
        BackgroundColor = MobileTheme.SurfacePrimary;
        SafeAreaEdges = SafeAreaEdges.All;
        NavigationPage.SetHasNavigationBar(this, false);

        var header = MobileUi.TopBar("Выбор сцены", back: CancelAsync);
        var body = new VerticalStackLayout { Padding = new Thickness(20, 18), Spacing = 12 };
        body.Add(MobileUi.Text("Какую сцену запустить?", 24, true));
        body.Add(MobileUi.Text("Выбери сцену из списка. Текущая сцена отмечена.", 14, color: MobileTheme.TextMuted));
        var rows = new VerticalStackLayout { Spacing = 8 };
        foreach (var scene in scenes)
        {
            rows.Add(SceneRow(scene));
        }
        body.Add(rows);

        var content = new ScrollView { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Always };
        var root = new Grid { RowDefinitions = new RowDefinitionCollection(new RowDefinition(60), new RowDefinition(GridLength.Star)) };
        root.Add(header, 0, 0);
        root.Add(content, 0, 1);
        Content = root;
    }

    public Task<IskraScene?> Result { get; }

    protected override bool OnBackButtonPressed()
    {
        _ = CancelAsync();
        return true;
    }

    private View SceneRow(IskraScene scene)
    {
        var isCurrent = currentScene?.Id == scene.Id;
        var detail = isCurrent
            ? MobileUi.Text("Текущая сцена", 13, color: MobileTheme.TextMuted)
            : MobileUi.Text("Нажми, чтобы запустить", 13, color: MobileTheme.TextMuted);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(42), new ColumnDefinition(GridLength.Star), new ColumnDefinition(32)),
            ColumnSpacing = 12
        };
        row.Add(new GraphicsView { Drawable = new PickerSceneIcon(isCurrent), WidthRequest = 32, HeightRequest = 32, VerticalOptions = LayoutOptions.Center }, 0, 0);
        row.Add(new VerticalStackLayout { Spacing = 2, Children = { MobileUi.Text(scene.Name, 16, true), detail } }, 1, 0);
        if (isCurrent)
        {
            row.Add(MobileUi.Text("●", 12, true, MobileTheme.Accent), 2, 0);
        }
        var panel = MobileUi.Panel(row, new Thickness(14, 10));
        panel.Stroke = isCurrent ? MobileTheme.Accent : MobileTheme.Border;
        panel.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await ChooseAsync(scene)) });
        return panel;
    }

    private async Task ChooseAsync(IskraScene scene)
    {
        await Navigation.PopModalAsync();
        result.TrySetResult(scene);
    }

    private async Task CancelAsync()
    {
        await Navigation.PopModalAsync();
        result.TrySetResult(null);
    }

    private sealed class PickerSceneIcon(bool selected) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.FillColor = selected ? MobileTheme.AccentSoft : MobileTheme.SurfaceSelected;
            canvas.FillRoundedRectangle(2, 2, dirtyRect.Width - 4, dirtyRect.Height - 4, 8);
            canvas.StrokeColor = selected ? MobileTheme.Accent : MobileTheme.TextMuted;
            canvas.StrokeSize = 2;
            canvas.DrawRoundedRectangle(2, 2, dirtyRect.Width - 4, dirtyRect.Height - 4, 8);
            canvas.DrawLine(9, dirtyRect.Height / 2, dirtyRect.Width - 9, dirtyRect.Height / 2);
        }
    }
}
