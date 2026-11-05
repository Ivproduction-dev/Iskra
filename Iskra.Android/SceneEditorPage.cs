using IskraStudio;

namespace Iskra.Android;

public sealed class SceneEditorPage : ContentPage
{
    private readonly ProjectStore projectStore;
    private readonly ProjectContentStore contentStore;
    private readonly IskraProject project;
    private readonly IskraScene scene;
    private readonly VerticalStackLayout objectRows = new() { Spacing = 10 };
    private readonly Label emptyLabel = MobileUi.Text("Нет объектов", 15, color: MobileTheme.TextMuted);

    public SceneEditorPage(ProjectStore projectStore, IskraProject project, IskraScene scene)
    {
        this.projectStore = projectStore;
        contentStore = new ProjectContentStore(projectStore);
        this.project = project;
        this.scene = scene;
        BackgroundColor = MobileTheme.SurfacePrimary;
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        NavigationPage.SetHasNavigationBar(this, false);

        var header = MobileUi.TopBar(scene.Name, back: () => Navigation.PopAsync(), more: ShowProjectMenuAsync);
        var body = new VerticalStackLayout { Padding = new Thickness(20, 18, 20, 104), Spacing = 14 };
        body.Add(MobileUi.Text("Фон", 18, true));
        body.Add(BackgroundRow());
        body.Add(new BoxView { HeightRequest = 1, BackgroundColor = MobileTheme.Border, Margin = new Thickness(0, 6) });
        body.Add(MobileUi.Text("Объекты", 18, true));
        body.Add(emptyLabel);
        body.Add(objectRows);

        var root = new Grid { RowDefinitions = new RowDefinitionCollection(new RowDefinition(60), new RowDefinition(GridLength.Star)) };
        root.Add(header, 0, 0);
        root.Add(new ScrollView { Content = body }, 0, 1);
        var actions = new HorizontalStackLayout { Spacing = 10, Margin = new Thickness(20), HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End };
        var runButton = new Button
        {
            Text = "▶", FontSize = 21, TextColor = Colors.White, BackgroundColor = MobileTheme.Accent,
            CornerRadius = 28, WidthRequest = 56, HeightRequest = 56, Padding = 0
        };
        SemanticProperties.SetDescription(runButton, "Запустить сцену");
        runButton.Clicked += async (_, _) => await MobileSceneLauncher.LaunchAsync(this, projectStore, project, scene);
        actions.Add(runButton);
        var addButton = new Button
        {
            Text = "+", FontSize = 32, TextColor = Colors.White, BackgroundColor = MobileTheme.Accent,
            CornerRadius = 30, WidthRequest = 60, HeightRequest = 60, Padding = 0,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End
        };
        SemanticProperties.SetDescription(addButton, "Добавить объект");
        addButton.Clicked += async (_, _) => await CreateObjectAsync();
        actions.Add(addButton);
        root.Add(actions, 0, 1);
        Content = root;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ReloadObjects();
    }

    private View BackgroundRow()
    {
        var background = new IskraObject { Id = IskraObject.BackgroundId, Name = "Фон" };
        var panel = MobileUi.Panel(new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                MobileUi.Text("Фон", 16, true),
                MobileUi.Text("Нажми, чтобы добавить образ или изменить скрипт.", 14, color: MobileTheme.TextMuted)
            }
        });
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await Navigation.PushAsync(new ObjectEditorPage(projectStore, project, scene, background));
        panel.GestureRecognizers.Add(tap);
        return panel;
    }

    private void ReloadObjects()
    {
        objectRows.Clear();
        emptyLabel.IsVisible = scene.Objects.Count == 0;
        foreach (var item in scene.Objects)
        {
            objectRows.Add(ObjectRow(item));
        }
    }

    private View ObjectRow(IskraObject item)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(42), new ColumnDefinition(GridLength.Star), new ColumnDefinition(48)),
            ColumnSpacing = 12
        };
        row.Add(ObjectThumbnail(item), 0, 0);
        row.Add(MobileUi.Text(item.Name, 16, true), 1, 0);
        row.Add(MobileUi.IconButton("⋮", $"Действия объекта {item.Name}", async (_, _) => await ShowObjectMenuAsync(item)), 2, 0);
        var panel = MobileUi.Panel(row, new Thickness(12, 6));
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await Navigation.PushAsync(new ObjectEditorPage(projectStore, project, scene, item));
        panel.GestureRecognizers.Add(tap);
        return panel;
    }

    private static GraphicsView ObjectIcon() => new()
    {
        Drawable = new ObjectIconDrawable(),
        WidthRequest = 32,
        HeightRequest = 32,
        VerticalOptions = LayoutOptions.Center
    };

    private View ObjectThumbnail(IskraObject item)
    {
        var directory = contentStore.GetObjectResourceDirectory(project, scene, item.Id, "sprites");
        var preview = ProjectContentStore.FindFirstPreviewImage(directory);
        if (preview is not null)
        {
            return new Image
            {
                Source = ImageSource.FromFile(preview),
                WidthRequest = 34,
                HeightRequest = 34,
                Aspect = Aspect.AspectFill,
                VerticalOptions = LayoutOptions.Center
            };
        }
        return ObjectIcon();
    }

    private async Task CreateObjectAsync()
    {
        await Navigation.PushModalAsync(new NavigationPage(new NameInputPage(
            "Новый объект",
            "Объект появится в этой сцене.",
            string.Empty,
            value =>
            {
                if (!contentStore.TryCreateObject(project, scene, value, out _, out var error))
                {
                    return Task.FromResult<string?>(error);
                }
                ReloadObjects();
                return Task.FromResult<string?>(null);
            })));
    }

    private async Task ShowObjectMenuAsync(IskraObject item)
    {
        var action = await DisplayActionSheetAsync($"Объект «{item.Name}»", "Отмена", "Удалить", "Переименовать");
        if (action == "Удалить")
        {
            if (await DisplayAlertAsync("Удалить объект?", $"Объект «{item.Name}» и его ресурсы будут удалены.", "Удалить", "Отмена"))
            {
                if (!contentStore.TryDeleteObject(project, scene, item, out var error))
                {
                    await DisplayAlertAsync("Не удалось удалить объект", error, "Понятно");
                }
                ReloadObjects();
            }
        }
        else if (action == "Переименовать")
        {
            await Navigation.PushModalAsync(new NavigationPage(new NameInputPage(
                "Переименовать объект", $"Текущее имя: {item.Name}", item.Name,
                value =>
                {
                    if (!contentStore.TryRenameObject(project, scene, item, value, out var error))
                    {
                        return Task.FromResult<string?>(error);
                    }
                    ReloadObjects();
                    return Task.FromResult<string?>(null);
                })));
        }
    }

    private async Task ShowProjectMenuAsync()
    {
        var action = await DisplayActionSheetAsync("Сцена", "Закрыть", null, "Опции проекта");
        if (action == "Опции проекта")
        {
            await DisplayAlertAsync("Опции проекта", $"{project.Name}\n{(project.Orientation == ProjectOrientation.Horizontal ? "Горизонтальная" : "Вертикальная")}", "Готово");
        }
    }

    private sealed class ObjectIconDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.FillColor = MobileTheme.SurfaceSelected;
            canvas.FillEllipse(3, 3, dirtyRect.Width - 6, dirtyRect.Height - 6);
            canvas.FillColor = MobileTheme.Accent;
            canvas.FillEllipse(dirtyRect.Width / 2 - 4, dirtyRect.Height / 2 - 4, 8, 8);
        }
    }
}
