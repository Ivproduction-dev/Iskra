using IskraStudio;

namespace Iskra.Android;

public sealed class ProjectLibraryPage : ContentPage
{
    private readonly ProjectStore projectStore;
    private readonly ProjectArchiveService archiveService;
    private readonly VerticalStackLayout projectRows = new() { Spacing = 10 };
    private readonly Border emptyPanel;
    private readonly Label emptyMessage = MobileUi.Text("Нет проектов. Создай первый через кнопку +.", 15, color: MobileTheme.TextMuted);

    public ProjectLibraryPage(ProjectStore projectStore, ProjectArchiveService archiveService)
    {
        this.projectStore = projectStore;
        this.archiveService = archiveService;
        Title = "Искра Студио";
        BackgroundColor = MobileTheme.SurfacePrimary;
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        NavigationPage.SetHasNavigationBar(this, false);

        var header = MobileUi.TopBar("Искра Студио", more: ShowLibraryMenuAsync);
        var content = new VerticalStackLayout { Padding = new Thickness(20, 12, 20, 104), Spacing = 8 };
        content.Add(MobileUi.Text("Твоя мастерская", 30));
        content.Add(MobileUi.Text("Создавай проекты и собирай сцены.", 15, color: MobileTheme.TextMuted));

        var sectionTitle = new Grid
        {
            Margin = new Thickness(0, 22, 0, 8),
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star))
        };
        sectionTitle.Add(MobileUi.Text("Список проектов", 18, true), 0, 0);
        content.Add(sectionTitle);

        emptyPanel = MobileUi.Panel(new VerticalStackLayout
        {
            Spacing = 14,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new GraphicsView { Drawable = new EmptyProjectsDrawable(), WidthRequest = 72, HeightRequest = 72, HorizontalOptions = LayoutOptions.Center },
                MobileUi.Text("Нет проектов", 19, true),
                emptyMessage
            }
        }, new Thickness(20));
        emptyPanel.Margin = new Thickness(0, 12, 0, 0);
        var scroll = new ScrollView { Content = content };
        var root = new Grid { RowDefinitions = new RowDefinitionCollection(new RowDefinition(60), new RowDefinition(GridLength.Star)) };
        root.Add(header, 0, 0);
        root.Add(scroll, 0, 1);
        var createButton = new Button
        {
            Text = "+",
            FontSize = 32,
            TextColor = Colors.White,
            BackgroundColor = MobileTheme.Accent,
            CornerRadius = 30,
            WidthRequest = 60,
            HeightRequest = 60,
            Padding = 0,
            Margin = new Thickness(20),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
            AutomationId = "CreateProjectButton"
        };
        SemanticProperties.SetDescription(createButton, "Создать проект");
        createButton.Clicked += async (_, _) => await OpenCreateProjectAsync();
        root.Add(createButton, 0, 1);
        content.Add(emptyPanel);
        content.Add(projectRows);
        Content = root;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshProjects();
    }

    private void RefreshProjects()
    {
        projectRows.Clear();
        var projects = projectStore.LoadAll(out _);
        emptyMessage.IsVisible = projects.Count == 0;
        emptyPanel.IsVisible = projects.Count == 0;
        projectRows.IsVisible = projects.Count > 0;
        foreach (var project in projects)
        {
            projectRows.Add(CreateProjectRow(project));
        }
    }

    private View CreateProjectRow(IskraProject project)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(48), new ColumnDefinition(GridLength.Star), new ColumnDefinition(48)),
            ColumnSpacing = 12
        };
        row.Add(new GraphicsView { Drawable = new ProjectIconDrawable(), WidthRequest = 40, HeightRequest = 40, VerticalOptions = LayoutOptions.Center }, 0, 0);
        row.Add(MobileUi.Text(project.Name, 16, true), 1, 0);
        row.Add(MobileUi.IconButton("⋮", $"Действия проекта {project.Name}", async (_, _) => await ShowProjectMenuAsync(project)), 2, 0);
        var panel = MobileUi.Panel(row, new Thickness(12, 8));
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            projectStore.MarkOpened(project);
            await Navigation.PushAsync(new SceneListPage(projectStore, project, archiveService));
        };
        panel.GestureRecognizers.Add(tap);
        return panel;
    }

    private async Task OpenCreateProjectAsync()
    {
        var page = new CreateProjectPage(projectStore);
        page.Completed += async _ =>
        {
            await Navigation.PopModalAsync();
            RefreshProjects();
        };
        page.Cancelled += async () => await Navigation.PopModalAsync();
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async Task ShowLibraryMenuAsync()
    {
        var action = await DisplayActionSheetAsync("Искра Студио", "Закрыть", null, "Импортировать проект .iskp", "Настройки", "Журнал", "О программе");
        if (action == "Импортировать проект .iskp")
        {
            await ImportProjectAsync();
        }
        else if (action == "Настройки")
        {
            await Navigation.PushAsync(new SettingsPage());
        }
        else if (action == "Журнал")
        {
            await Navigation.PushAsync(new MobileLogPage());
        }
        else if (action == "О программе")
        {
            await DisplayAlertAsync("О программе", "Искра Студио\nСреда для создания интерактивных проектов.\n\n© 2026 Ivproduction-dev\nЛицензия MIT", "Понятно");
        }
    }

    private async Task ShowProjectMenuAsync(IskraProject project)
    {
        var action = await DisplayActionSheetAsync($"Проект «{project.Name}»", "Отмена", "Удалить", "Переименовать", "Экспортировать .iskp");
        if (action == "Удалить")
        {
            if (await DisplayAlertAsync("Удалить проект?", $"Проект «{project.Name}» и все его ресурсы будут удалены.", "Удалить", "Отмена"))
            {
                if (!projectStore.TryDelete(project, out var error))
                {
                    await DisplayAlertAsync("Не удалось удалить проект", error, "Понятно");
                }
                RefreshProjects();
            }
        }
        else if (action == "Переименовать")
        {
            var newName = await DisplayPromptAsync("Переименовать проект", "Новое имя проекта", initialValue: project.Name, maxLength: 50);
            if (!string.IsNullOrWhiteSpace(newName) && !projectStore.TryUpdate(project, newName, project.Orientation, projectStore.LoadAll(out _), out _, out var error))
            {
                await DisplayAlertAsync("Не удалось переименовать проект", error, "Понятно");
            }
            RefreshProjects();
        }
        else if (action == "Экспортировать .iskp")
        {
            await ExportProjectAsync(project);
        }
    }

    private async Task ExportProjectAsync(IskraProject project)
    {
        var path = MobileExport.PrepareCacheFile(project.Name);
        try
        {
            await Task.Run(() => archiveService.Export(project, path));
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = $"Экспорт проекта «{project.Name}»",
                File = new ShareFile(path, "application/zip")
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MobileLog.Write("Не удалось экспортировать проект.", exception);
            await DisplayAlertAsync("Экспорт не удался", "Проверь файлы проекта и попробуй ещё раз.", "Понятно");
        }
    }

    private async Task ImportProjectAsync()
    {
        FileResult? selectedFile;
        try
        {
            selectedFile = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Выбери проект .iskp" });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MobileLog.Write("Не удалось открыть выбор файла проекта.", exception);
            await DisplayAlertAsync("Не удалось открыть файл", "Попробуй выбрать файл проекта ещё раз.", "Понятно");
            return;
        }
        if (selectedFile is null)
        {
            return;
        }
        if (!string.Equals(Path.GetExtension(selectedFile.FileName), ".iskp", StringComparison.OrdinalIgnoreCase))
        {
            await DisplayAlertAsync("Неподдерживаемый файл", "Выбери файл проекта с расширением .iskp.", "Понятно");
            return;
        }

        var temporaryArchive = Path.Combine(FileSystem.CacheDirectory, $"import-{Guid.NewGuid():N}.iskp");
        try
        {
            await using (var source = await selectedFile.OpenReadAsync())
            await using (var destination = File.Create(temporaryArchive))
            {
                await source.CopyToAsync(destination);
            }

            var suggestedName = await Task.Run(() => archiveService.ReadProjectName(temporaryArchive));
            var name = await DisplayPromptAsync("Импортировать проект", "Имя проекта", initialValue: suggestedName, maxLength: 50);
            if (!string.IsNullOrWhiteSpace(name))
            {
                await Task.Run(() => archiveService.Import(temporaryArchive, name.Trim()));
                RefreshProjects();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            MobileLog.Write("Не удалось импортировать проект .iskp.", exception);
            await DisplayAlertAsync("Импорт не удался", "Что-то пошло не так. Лог с ошибкой сохранён в log.txt.", "Понятно");
        }
        finally
        {
            if (File.Exists(temporaryArchive))
            {
                File.Delete(temporaryArchive);
            }
        }
    }

    private sealed class ProjectIconDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.FillColor = MobileTheme.AccentSoft;
            canvas.FillRoundedRectangle(0, 0, dirtyRect.Width, dirtyRect.Height, 12);
            canvas.StrokeColor = MobileTheme.Accent;
            canvas.StrokeSize = 2;
            var cx = dirtyRect.Width / 2;
            var cy = dirtyRect.Height / 2;
            canvas.DrawLine(cx, cy - 9, cx, cy + 9);
            canvas.DrawLine(cx - 9, cy, cx + 9, cy);
            canvas.DrawLine(cx - 6, cy - 6, cx + 6, cy + 6);
            canvas.DrawLine(cx + 6, cy - 6, cx - 6, cy + 6);
        }
    }

    private sealed class EmptyProjectsDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.FillColor = MobileTheme.AccentSoft;
            canvas.FillRoundedRectangle(4, 4, dirtyRect.Width - 8, dirtyRect.Height - 8, 20);
            canvas.FillColor = MobileTheme.SurfacePanel;
            canvas.FillRoundedRectangle(17, 20, 38, 34, 7);
            canvas.StrokeColor = MobileTheme.Border;
            canvas.StrokeSize = 2;
            canvas.DrawRoundedRectangle(17, 20, 38, 34, 7);
            canvas.StrokeColor = MobileTheme.Accent;
            canvas.StrokeSize = 2.5f;
            canvas.DrawLine(27, 37, 45, 37);
            canvas.DrawLine(36, 28, 36, 46);
        }
    }
}
