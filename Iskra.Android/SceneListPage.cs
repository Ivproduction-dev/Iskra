using IskraStudio;

namespace Iskra.Android;

public sealed class SceneListPage : ContentPage
{
    private readonly ProjectStore projectStore;
    private readonly ProjectContentStore contentStore;
    private readonly ProjectArchiveService archiveService;
    private IskraProject project;
    private readonly VerticalStackLayout sceneRows = new() { Spacing = 10 };
    private readonly Label emptyLabel = MobileUi.Text("В проекте пока нет сцен.", 15, color: MobileTheme.TextMuted);
    private readonly HashSet<Guid> selectedSceneIds = [];
    private readonly Grid root = new();
    private readonly Grid selectionBar = new();
    private View? floatingActions;
    private bool selectionMode;

    public SceneListPage(ProjectStore projectStore, IskraProject project, ProjectArchiveService archiveService)
    {
        this.projectStore = projectStore;
        contentStore = new ProjectContentStore(projectStore);
        this.project = project;
        this.archiveService = archiveService;
        BackgroundColor = MobileTheme.SurfacePrimary;
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        NavigationPage.SetHasNavigationBar(this, false);

        var header = MobileUi.TopBar("Список сцен", back: () => Navigation.PopAsync(), more: ShowProjectMenuAsync);
        var intro = new VerticalStackLayout { Padding = new Thickness(20, 20, 20, 108), Spacing = 14 };
        intro.Add(MobileUi.Text(project.Name, 28));
        intro.Add(MobileUi.Text("Сцены", 18, true));
        intro.Add(emptyLabel);
        intro.Add(sceneRows);
        root.RowDefinitions = new RowDefinitionCollection(new RowDefinition(60), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star));
        root.Add(header, 0, 0);
        BuildSelectionBar();
        root.Add(selectionBar, 0, 1);
        root.Add(new ScrollView { Content = intro }, 0, 2);
        var actions = new HorizontalStackLayout { Spacing = 10, Margin = new Thickness(20), HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End };
        var runButton = FloatingPlayButton();
        runButton.Clicked += async (_, _) => await RunProjectAsync();
        var addButton = FloatingAddButton();
        addButton.Clicked += async (_, _) => await CreateSceneAsync();
        actions.Add(runButton);
        actions.Add(addButton);
        floatingActions = actions;
        root.Add(actions, 0, 2);
        Content = root;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Reload();
    }

    private void Reload()
    {
        sceneRows.Clear();
        selectedSceneIds.RemoveWhere(id => project.Scenes.All(scene => scene.Id != id));
        emptyLabel.IsVisible = project.Scenes.Count == 0;
        foreach (var scene in project.Scenes)
        {
            sceneRows.Add(SceneRow(scene));
        }
    }

    private View SceneRow(IskraScene scene)
    {        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(42), new ColumnDefinition(GridLength.Star), new ColumnDefinition(48)),
            ColumnSpacing = 12
        };
        var icon = selectionMode
            ? SelectionIndicator(selectedSceneIds.Contains(scene.Id))
            : CreateSceneThumbnail(scene);
        row.Add(icon, 0, 0);
        row.Add(MobileUi.Text(scene.Name, 16, true), 1, 0);
        if (!selectionMode)
        {
            row.Add(MobileUi.IconButton("⋮", $"Действия сцены {scene.Name}", async (_, _) => await ShowSceneMenuAsync(scene)), 2, 0);
        }
        var panel = MobileUi.Panel(row, new Thickness(12, 6));
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (selectionMode)
            {
                if (!selectedSceneIds.Add(scene.Id)) selectedSceneIds.Remove(scene.Id);
                BuildSelectionBar();
                Reload();
                return;
            }
            await Navigation.PushAsync(new SceneEditorPage(projectStore, project, scene));
        };
        panel.GestureRecognizers.Add(tap);
        return panel;
    }

    private View CreateSceneThumbnail(IskraScene scene)
    {
        var path = new ProjectContentStore(projectStore).GetScenePreviewPath(project, scene);
        if (File.Exists(path))
        {
            return new Image
            {
                Source = ImageSource.FromFile(path),
                WidthRequest = 34,
                HeightRequest = 34,
                Aspect = Aspect.AspectFill,
                VerticalOptions = LayoutOptions.Center
            };
        }
        return new GraphicsView { Drawable = new SceneIconDrawable(), WidthRequest = 34, HeightRequest = 34, VerticalOptions = LayoutOptions.Center };
    }

    private void BuildSelectionBar()
    {
        selectionBar.ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star));
        selectionBar.Padding = new Thickness(12, 4);
        selectionBar.ColumnSpacing = 8;
        selectionBar.IsVisible = selectionMode;
        if (floatingActions is not null) floatingActions.IsVisible = !selectionMode;
        if (!selectionMode) return;
        var selectAll = MobileUi.Action("Все", (_, _) =>
        {
            selectedSceneIds.Clear();
            foreach (var scene in project.Scenes) selectedSceneIds.Add(scene.Id);
            BuildSelectionBar();
            Reload();
        }, secondary: true);
        var delete = MobileUi.Action($"Удалить ({selectedSceneIds.Count})", async (_, _) => await DeleteSelectedScenesAsync());
        delete.IsEnabled = selectedSceneIds.Count > 0;
        var cancel = MobileUi.Action("Отмена", (_, _) =>
        {
            selectionMode = false;
            selectedSceneIds.Clear();
            BuildSelectionBar();
            Reload();
        }, secondary: true);
        selectionBar.Children.Clear();
        selectionBar.Add(selectAll, 0, 0);
        selectionBar.Add(delete, 1, 0);
        selectionBar.Add(cancel, 2, 0);
    }

    private static View SelectionIndicator(bool selected) => new Border
    {
        WidthRequest = 26,
        HeightRequest = 26,
        Stroke = selected ? MobileTheme.Accent : MobileTheme.Border,
        BackgroundColor = selected ? MobileTheme.Accent : Colors.Transparent,
        StrokeThickness = 2,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 7 },
        Content = selected ? MobileUi.Text("✓", 17, true, Colors.White) : null,
        VerticalOptions = LayoutOptions.Center,
        HorizontalOptions = LayoutOptions.Center
    };

    private async Task DeleteSelectedScenesAsync()
    {
        var selected = project.Scenes.Where(scene => selectedSceneIds.Contains(scene.Id)).ToList();
        if (selected.Count == 0) return;
        var names = string.Join(", ", selected.Select(scene => scene.Name));
        if (!await DisplayAlertAsync("Удалить сцены?", $"Будут удалены {selected.Count} сцен и их ресурсы: {names}", "Удалить", "Отмена")) return;
        if (!contentStore.TryDeleteScenes(project, selected, out var error))
        {
            await DisplayAlertAsync("Не удалось удалить сцены", error ?? "Попробуй ещё раз.", "Понятно");
            return;
        }
        selectedSceneIds.Clear();
        selectionMode = false;
        BuildSelectionBar();
        Reload();
    }

    private async Task CreateSceneAsync()
    {
        await Navigation.PushModalAsync(new NavigationPage(new NameInputPage(
            "Новая сцена",
            "Добавь сцену в проект. Если имя оставить пустым, Искра подберёт его сама.",
            string.Empty,
            value =>
            {
                if (!contentStore.TryCreateScene(project, value, out _, out var error))
                {
                    return Task.FromResult<string?>(error);
                }
                Reload();
                return Task.FromResult<string?>(null);
            })));
    }

    private async Task RunProjectAsync()
    {
        await MobileSceneLauncher.LaunchAsync(this, projectStore, project, currentScene: null);
    }

    private async Task ShowSceneMenuAsync(IskraScene scene)
    {
        var action = await DisplayActionSheetAsync($"Сцена «{scene.Name}»", "Отмена", "Удалить", "Переименовать");
        if (action == "Удалить")
        {
            if (await DisplayAlertAsync("Удалить сцену?", $"Сцена «{scene.Name}» и её ресурсы будут удалены.", "Удалить", "Отмена"))
            {
                if (!contentStore.TryDeleteScenes(project, [scene], out var error))
                {
                    await DisplayAlertAsync("Не удалось удалить сцену", error, "Понятно");
                }
                Reload();
            }
        }
        else if (action == "Переименовать")
        {
            await Navigation.PushModalAsync(new NavigationPage(new NameInputPage(
                "Переименовать сцену",
                $"Текущее имя: {scene.Name}",
                scene.Name,
                value =>
                {
                    if (!contentStore.TryRenameScene(project, scene, value, out var error))
                    {
                        return Task.FromResult<string?>(error);
                    }
                    Reload();
                    return Task.FromResult<string?>(null);
                })));
        }
    }

    private async Task ShowProjectMenuAsync()
    {
        var action = await DisplayActionSheetAsync("Опции проекта", "Закрыть", "Удалить проект", "Выбрать сцены", "Переименовать проект", "Экспортировать .iskp");
        if (action == "Выбрать сцены")
        {
            selectionMode = true;
            selectedSceneIds.Clear();
            BuildSelectionBar();
            Reload();
        }
        else if (action == "Переименовать проект")
        {
            await Navigation.PushModalAsync(new NavigationPage(new NameInputPage(
                "Переименовать проект", "Подтверди новое имя проекта.", project.Name,
                value =>
                {
                    if (!projectStore.TryUpdate(project, value, project.Orientation, projectStore.LoadAll(out _), out var updated, out var error) || updated is null)
                    {
                        return Task.FromResult<string?>(error);
                    }
                    project = updated;
                    Title = project.Name;
                    Reload();
                    return Task.FromResult<string?>(null);
                })));
        }
        else if (action == "Удалить проект")
        {
            if (await DisplayAlertAsync("Удалить проект?", $"Проект «{project.Name}» и все его ресурсы будут удалены.", "Удалить", "Отмена"))
            {
                if (!projectStore.TryDelete(project, out var error))
                {
                    await DisplayAlertAsync("Не удалось удалить проект", error, "Понятно");
                    return;
                }
                await Navigation.PopAsync();
            }
        }
        else if (action == "Экспортировать .iskp")
        {
            var path = MobileExport.PrepareCacheFile(project.Name);
            try
            {
                await Task.Run(() => archiveService.Export(project, path));
                await Share.Default.RequestAsync(new ShareFileRequest("Экспорт проекта Искры", new ShareFile(path, "application/zip")));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                MobileLog.Write("Не удалось экспортировать проект.", exception);
                await DisplayAlertAsync("Экспорт не удался", "Проверь файлы проекта и попробуй ещё раз.", "Понятно");
            }
        }
    }

    private static Button FloatingAddButton()
    {
        var button = new Button
        {
            Text = "+", FontSize = 32, TextColor = Colors.White, BackgroundColor = MobileTheme.Accent,
            CornerRadius = 30, WidthRequest = 60, HeightRequest = 60, Padding = 0,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End
        };
        SemanticProperties.SetDescription(button, "Добавить сцену");
        return button;
    }

    private static Button FloatingPlayButton()
    {
        var button = new Button
        {
            Text = "▶", FontSize = 21, TextColor = Colors.White, BackgroundColor = MobileTheme.Accent,
            CornerRadius = 28, WidthRequest = 56, HeightRequest = 56, Padding = 0
        };
        SemanticProperties.SetDescription(button, "Запустить проект");
        return button;
    }

    private sealed class SceneIconDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.FillColor = MobileTheme.AccentSoft;
            canvas.FillRoundedRectangle(1, 1, dirtyRect.Width - 2, dirtyRect.Height - 2, 10);
            canvas.StrokeColor = MobileTheme.Accent;
            canvas.StrokeSize = 2;
            canvas.DrawRoundedRectangle(9, 9, dirtyRect.Width - 18, dirtyRect.Height - 18, 3);
        }
    }
}
