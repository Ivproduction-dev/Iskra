using IskraStudio;
using SkiaSharp;
using RoundRectangle = Microsoft.Maui.Controls.Shapes.RoundRectangle;

namespace Iskra.Android;

public sealed class ObjectEditorPage : ContentPage
{
    private enum ObjectTab { Script, Images, Sounds }

    private readonly ProjectStore projectStore;
    private readonly ProjectContentStore contentStore;
    private readonly IskraProject project;
    private readonly IskraScene scene;
    private readonly IskraObject item;
    private readonly ScriptSyntaxEditor scriptEditor = new() { FontFamily = "monospace", FontSize = 16, AutoSize = EditorAutoSizeOption.Disabled };
    private readonly Border scriptContainer;
    private readonly ContentView resourceContainer = new() { IsVisible = false };
    private readonly HorizontalStackLayout suggestionRows = new() { Spacing = 6 };
    private readonly ScrollView suggestionStrip = new() { Orientation = ScrollOrientation.Horizontal, HeightRequest = 48, IsVisible = false };
    private readonly Grid body = new();
    private readonly Label scriptStatus = new()
    {
        FontSize = 13,
        LineBreakMode = LineBreakMode.WordWrap,
        IsVisible = false
    };
    private readonly Button scriptTab = new() { Text = "Скрипты" };
    private readonly Button imagesTab = new() { Text = "Образы" };
    private readonly Button soundsTab = new() { Text = "Звуки" };
    private ObjectTab currentTab;
    private CancellationTokenSource? pendingSave;

    public ObjectEditorPage(ProjectStore projectStore, IskraProject project, IskraScene scene, IskraObject item)
    {
        this.projectStore = projectStore;
        contentStore = new ProjectContentStore(projectStore);
        this.project = project;
        this.scene = scene;
        this.item = item;
        BackgroundColor = MobileTheme.SurfacePrimary;
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        NavigationPage.SetHasNavigationBar(this, false);
        scriptEditor.Text = contentStore.ReadObjectScript(project, scene, item.Id);
        scriptEditor.TextColor = Color.FromArgb("#FAF9F5");
        scriptEditor.BackgroundColor = Color.FromArgb("#181715");
        scriptEditor.MinimumHeightRequest = 240;
        scriptEditor.TextChanged += ScriptChanged;
        suggestionEditorSetup();
        scriptContainer = new Border
        {
            Content = new VerticalStackLayout { Spacing = 6, Children = { scriptEditor, scriptStatus, suggestionStrip } },
            BackgroundColor = Color.FromArgb("#181715"),
            Stroke = MobileTheme.Border,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(8)
        };

        var header = MobileUi.TopBar(item.Name, back: () => Navigation.PopAsync(), more: ShowObjectMenuAsync);
        var tabs = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)),
            Padding = new Thickness(12, 0),
            ColumnSpacing = 8
        };
        ConfigureTab(scriptTab, ObjectTab.Script);
        ConfigureTab(imagesTab, ObjectTab.Images);
        ConfigureTab(soundsTab, ObjectTab.Sounds);
        tabs.Add(scriptTab, 0, 0);
        tabs.Add(imagesTab, 1, 0);
        tabs.Add(soundsTab, 2, 0);
        body.Padding = new Thickness(16, 12, 16, 96);
        body.Add(scriptContainer);
        body.Add(resourceContainer);
        var root = new Grid { RowDefinitions = new RowDefinitionCollection(new RowDefinition(60), new RowDefinition(56), new RowDefinition(GridLength.Star)) };
        root.Add(header, 0, 0);
        root.Add(tabs, 0, 1);
        root.Add(body, 0, 2);
        var runButton = new Button
        {
            Text = "▶", FontSize = 21, TextColor = Colors.White, BackgroundColor = MobileTheme.Accent,
            CornerRadius = 28, WidthRequest = 56, HeightRequest = 56, Padding = 0,
            Margin = new Thickness(20), HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End
        };
        SemanticProperties.SetDescription(runButton, "Запустить сцену");
        runButton.Clicked += async (_, _) => await MobileSceneLauncher.LaunchAsync(this, projectStore, project, scene);
        root.Add(runButton, 0, 2);
        Content = root;
        ShowTab(ObjectTab.Script);

        void suggestionEditorSetup()
        {
            suggestionStrip.Content = suggestionRows;
            scriptEditor.TextChanged += (_, _) => UpdateScriptSuggestions(suggestionStrip);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (currentTab != ObjectTab.Script)
        {
            ShowTab(currentTab);
        }
    }

    protected override void OnDisappearing()
    {
        SaveScript(scriptEditor.Text ?? string.Empty);
        pendingSave?.Cancel();
        pendingSave?.Dispose();
        pendingSave = null;
        base.OnDisappearing();
    }

    private void ConfigureTab(Button button, ObjectTab tab)
    {
        button.FontSize = 14;
        button.Padding = new Thickness(8, 8);
        button.CornerRadius = 8;
        button.Clicked += (_, _) => ShowTab(tab);
    }

    private void ShowTab(ObjectTab tab)
    {
        if (currentTab == ObjectTab.Script && tab != ObjectTab.Script)
        {
            pendingSave?.Cancel();
            pendingSave?.Dispose();
            pendingSave = null;
            SaveScript(scriptEditor.Text ?? string.Empty);
        }
        currentTab = tab;
        scriptTab.BackgroundColor = tab == ObjectTab.Script ? MobileTheme.AccentSoft : Colors.Transparent;
        scriptTab.TextColor = tab == ObjectTab.Script ? MobileTheme.Accent : MobileTheme.TextBody;
        imagesTab.BackgroundColor = tab == ObjectTab.Images ? MobileTheme.AccentSoft : Colors.Transparent;
        imagesTab.TextColor = tab == ObjectTab.Images ? MobileTheme.Accent : MobileTheme.TextBody;
        soundsTab.BackgroundColor = tab == ObjectTab.Sounds ? MobileTheme.AccentSoft : Colors.Transparent;
        soundsTab.TextColor = tab == ObjectTab.Sounds ? MobileTheme.Accent : MobileTheme.TextBody;

        if (tab == ObjectTab.Script)
        {
            scriptContainer.IsVisible = true;
            resourceContainer.IsVisible = false;
        }
        else
        {
            scriptContainer.IsVisible = false;
            resourceContainer.Content = BuildResourceTab(tab == ObjectTab.Images ? "sprites" : "sounds");
            resourceContainer.IsVisible = true;
        }
    }

    private void ScriptChanged(object? sender, TextChangedEventArgs e)
    {
        pendingSave?.Cancel();
        pendingSave?.Dispose();
        var cancellation = new CancellationTokenSource();
        pendingSave = cancellation;
        _ = SaveAfterPauseAsync(scriptEditor.Text ?? string.Empty, cancellation.Token);
    }

    private static readonly string[] ScriptCommands =
    [
        "При. запуске", "При. нажатии", "При. отпускании", "При. наведении",
        "Когда. условие", "Как.Только условие",
        "Задать имя = значение", "Присвоить имя = значение", "Задать изображение. ",
        "Изменить изображение. перед", "Изменить изображение. назад", "Задать прозрачность. 100",
        "Изменить прозрачность. 10", "Задать размер. 100", "Изменить размер. 10",
        "Задать яркость. 100", "Изменить яркость. 10",
        "Поместить X Y", "Ждать 1 секунду", "Если условие то", "Иначе если условие то",
        "Иначе", "Вечно", "Пока условие то", "Конец", "Печать \"текст\"",
        "Показать", "Спрятать", "Воспроизвести звук. файл.mp3", "Воспроизвести звук ждать. файл.mp3"
    ];

    private void UpdateScriptSuggestions(ScrollView strip)
    {
        var text = scriptEditor.Text ?? string.Empty;
        var cursor = Math.Clamp(scriptEditor.CursorPosition, 0, text.Length);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, cursor - 1)) + 1;
        var prefix = text[lineStart..cursor].TrimStart();
        suggestionRows.Clear();
        if (prefix.Length < 2)
        {
            strip.IsVisible = false;
            return;
        }
        var matches = ScriptCommands
            .Where(command => command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToArray();
        strip.IsVisible = matches.Length > 0;
        foreach (var command in matches)
        {
            var button = new Button
            {
                Text = command.TrimEnd(),
                FontSize = 13,
                Padding = new Thickness(10, 5),
                CornerRadius = 8,
                BackgroundColor = MobileTheme.SurfacePanel,
                TextColor = MobileTheme.TextBody,
                BorderColor = MobileTheme.Border,
                BorderWidth = 1,
                MinimumHeightRequest = 40
            };
            button.Clicked += (_, _) => InsertScriptSuggestion(command, lineStart, prefix);
            suggestionRows.Add(button);
        }
    }

    private void InsertScriptSuggestion(string command, int lineStart, string prefix)
    {
        var text = scriptEditor.Text ?? string.Empty;
        var cursor = Math.Clamp(scriptEditor.CursorPosition, 0, text.Length);
        var insertion = command;
        var replaceStart = cursor - prefix.Length;
        scriptEditor.Text = text[..replaceStart] + insertion + text[cursor..];
        scriptEditor.CursorPosition = replaceStart + insertion.Length;
    }

    private async Task SaveAfterPauseAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(500, cancellationToken);
            SaveScript(text);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SaveScript(string text)
    {
        if (!contentStore.TryWriteObjectScript(project, scene, item.Id, text, out var error))
        {
            MobileLog.Write($"Не удалось сохранить скрипт объекта «{item.Name}»: {error}");
        }
        ShowScriptDiagnostics(text);
    }

    private void ShowScriptDiagnostics(string text)
    {
        var diagnostics = new List<RuntimeDiagnostic>();
        var compiler = new ScriptCompiler(projectStore, contentStore);
        var objects = new List<IskraObject> { new() { Id = IskraObject.BackgroundId, Name = "Фон" } };
        objects.AddRange(scene.Objects);
        _ = compiler.CompileScript(project, scene, item, text, objects, diagnostics);
        var problem = diagnostics.FirstOrDefault();
        if (problem is null)
        {
            scriptStatus.IsVisible = false;
            return;
        }
        scriptStatus.Text = $"Строка {problem.Line}: {problem.Message}";
        scriptStatus.TextColor = problem.Severity == RuntimeDiagnosticSeverity.Error ? Color.FromArgb("#F08080") : Color.FromArgb("#E6B75E");
        scriptStatus.IsVisible = true;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => JumpToLine(problem.Line);
        scriptStatus.GestureRecognizers.Clear();
        scriptStatus.GestureRecognizers.Add(tap);
    }

    private void JumpToLine(int line)
    {
        var text = scriptEditor.Text ?? string.Empty;
        var index = 0;
        var current = 1;
        while (current < line && index < text.Length)
        {
            if (text[index] == '\n') current++;
            index++;
        }
        scriptEditor.Focus();
        scriptEditor.CursorPosition = Math.Min(index, text.Length);
    }

    private View BuildResourceTab(string resourceType)
    {
        return resourceType == "sprites" ? BuildImagesTab() : BuildSoundsTab();
    }

    private View BuildImagesTab()
    {
        var resourceRows = new VerticalStackLayout { Spacing = 8 };
        var stack = new VerticalStackLayout { Spacing = 14 };
        var heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(48))
        };
        heading.Add(MobileUi.Text("Образы объекта", 18, true), 0, 0);
        heading.Add(MobileUi.IconButton("＋", "Добавить образ", async (_, _) => await ShowAddImageMenuAsync()), 1, 0);
        stack.Add(heading);

        var directory = contentStore.GetObjectResourceDirectory(project, scene, item.Id, "sprites");
        var files = ProjectContentStore.ListResourceFiles(directory, ProjectContentStore.ImageExtensions);
        if (files.Count == 0)
        {
            stack.Add(MobileUi.Panel(new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    MobileUi.Text("Нет образов", 15, color: MobileTheme.TextMuted),
                    MobileUi.Text("Нарисуй образ или импортируй картинку.", 13, color: MobileTheme.TextMuted)
                }
            }, new Thickness(16)));
        }

        var preview = files.FirstOrDefault();
        foreach (var file in files)
        {
            var thumbnail = new Image
            {
                Source = ThumbnailSource(file),
                Aspect = Aspect.AspectFit,
                WidthRequest = 52,
                HeightRequest = 52
            };
            var thumbnailBorder = new Border
            {
                Content = thumbnail,
                WidthRequest = 56,
                HeightRequest = 56,
                BackgroundColor = MobileTheme.SurfaceSoft,
                Stroke = MobileTheme.Border,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = 2
            };
            var name = MobileUi.Text(Path.GetFileName(file), 14, true);
            var details = MobileUi.Text(
                string.Equals(file, preview, StringComparison.Ordinal) ? "Превью объекта" : "Нажми, чтобы рисовать",
                11, color: MobileTheme.TextMuted);
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection(
                    new ColumnDefinition(56), new ColumnDefinition(GridLength.Star), new ColumnDefinition(44)),
                ColumnSpacing = 10
            };
            var nameStack = new VerticalStackLayout
            {
                Spacing = 1,
                VerticalOptions = LayoutOptions.Center,
                Children = { name, details }
            };
            row.Add(thumbnailBorder, 0, 0);
            row.Add(nameStack, 1, 0);
            row.Add(MobileUi.IconButton("⋮", $"Действия файла {Path.GetFileName(file)}",
                async (_, _) => await ShowResourceMenuAsync(file, "sprites")), 2, 0);
            AddOpenGesture(thumbnailBorder);
            AddOpenGesture(nameStack);
            resourceRows.Add(MobileUi.Panel(row, new Thickness(8, 6)));

            void AddOpenGesture(View view)
            {
                var tap = new TapGestureRecognizer();
                tap.Tapped += async (_, _) => await OpenPaintingAsync(file);
                view.GestureRecognizers.Add(tap);
            }
        }

        stack.Add(resourceRows);
        return new ScrollView { Content = stack };
    }

    private View BuildSoundsTab()
    {
        var resourceRows = new VerticalStackLayout { Spacing = 10 };
        var stack = new VerticalStackLayout { Spacing = 14 };
        var heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(48))
        };
        heading.Add(MobileUi.Text("Звуки объекта", 18, true), 0, 0);
        heading.Add(MobileUi.IconButton("＋", "Добавить звук", async (_, _) => await ImportResourceAsync("sounds")), 1, 0);
        stack.Add(heading);
        var directory = contentStore.GetObjectResourceDirectory(project, scene, item.Id, "sounds");
        var files = ProjectContentStore.ListResourceFiles(directory, ProjectContentStore.AudioExtensions);
        if (files.Count == 0)
        {
            stack.Add(MobileUi.Panel(MobileUi.Text("Нет звуков", 15, color: MobileTheme.TextMuted), new Thickness(16)));
        }
        foreach (var file in files)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(42), new ColumnDefinition(GridLength.Star), new ColumnDefinition(48)),
                ColumnSpacing = 10
            };
            row.Add(MobileUi.Text("♫", 20, true, MobileTheme.Accent), 0, 0);
            var name = MobileUi.Text(Path.GetFileName(file), 14);
            row.Add(name, 1, 0);
            var tap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
            tap.Tapped += async (_, _) => await CopyResourceNameAsync(file);
            name.GestureRecognizers.Add(tap);
            row.Add(MobileUi.IconButton("⋮", $"Действия файла {Path.GetFileName(file)}",
                async (_, _) => await ShowResourceMenuAsync(file, "sounds")), 2, 0);
            resourceRows.Add(MobileUi.Panel(row, new Thickness(10, 5)));
        }
        stack.Add(resourceRows);
        return new ScrollView { Content = stack };
    }

    private static ImageSource ThumbnailSource(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
            {
                return ImageSource.FromFile(file);
            }
            var bounds = codec.Info;
            if (bounds.Width < 1 || bounds.Height < 1)
            {
                return ImageSource.FromFile(file);
            }
            var scale = Math.Min(1.0, Math.Min(112.0 / bounds.Width, 112.0 / bounds.Height));
            var dimensions = codec.GetScaledDimensions((float)scale);
            var info = new SKImageInfo(
                Math.Max(1, dimensions.Width), Math.Max(1, dimensions.Height),
                SKColorType.Bgra8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(info);
            var result = codec.GetPixels(info, bitmap.GetPixels());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                return ImageSource.FromFile(file);
            }
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            if (data is null)
            {
                return ImageSource.FromFile(file);
            }
            var bytes = data.ToArray();
            return ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось прочитать миниатюру образа.", exception);
            return ImageSource.FromFile(file);
        }
    }

    private async Task ShowAddImageMenuAsync()
    {
        var action = await DisplayActionSheetAsync("Добавить образ", "Отмена", null, "Нарисовать", "Импортировать");
        if (action == "Нарисовать")
        {
            await CreatePaintingAsync();
        }
        else if (action == "Импортировать")
        {
            await ImportResourceAsync("sprites");
        }
    }

    private async Task CreatePaintingAsync()
    {
        var name = await DisplayPromptAsync(
            "Новый рисунок", "Название образа", "Создать", "Отмена", initialValue: "Рисунок", maxLength: 50);
        if (name is null)
        {
            return;
        }
        name = name.Trim();
        if (name.Length == 0 || name.Length > 50 || name is "." or ".." || name.EndsWith('.') ||
            HasInvalidFileName(name))
        {
            await DisplayAlertAsync("Неподходящее имя", "Введи имя без недопустимых символов.", "Понятно");
            return;
        }
        try
        {
            await Navigation.PushAsync(new PaintDroidPage(projectStore, project, scene, item, null, name));
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось открыть рисунок.", exception);
            await DisplayAlertAsync("Не удалось открыть рисунок", "Попробуй ещё раз.", "Понятно");
        }
    }

    private async Task OpenPaintingAsync(string file)
    {
        try
        {
            await Navigation.PushAsync(new PaintDroidPage(projectStore, project, scene, item, file, null));
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось открыть рисунок.", exception);
            await DisplayAlertAsync("Не удалось открыть рисунок", "Попробуй ещё раз.", "Понятно");
        }
    }

    private async Task ImportResourceAsync(string resourceType)
    {
        var isImage = resourceType == "sprites";
        FileResult? selected;
        try
        {
            selected = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = isImage ? "Выбери образ" : "Выбери звук" });
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось открыть файл ресурса.", exception);
            await DisplayAlertAsync("Не удалось открыть файл", "Попробуй выбрать его ещё раз.", "Понятно");
            return;
        }
        if (selected is null) return;
        var extensions = isImage ? ProjectContentStore.ImageExtensions : ProjectContentStore.AudioExtensions;
        if (!extensions.Contains(Path.GetExtension(selected.FileName)))
        {
            await DisplayAlertAsync("Неподдерживаемый формат", isImage ? "Поддерживаются PNG, JPG, BMP, GIF и WEBP." : "Поддерживаются MP3, M4A, WAV, OGG, WMA и FLAC.", "Понятно");
            return;
        }

        string? temporary = null;
        try
        {
            var destinationDirectory = contentStore.EnsureObjectResourceDirectory(project, scene, item.Id, resourceType);
            var destinationPath = AvailablePath(destinationDirectory, SafeFileName(selected.FileName));
            temporary = destinationPath + ".tmp";
            await using (var source = await selected.OpenReadAsync())
            await using (var destination = File.Create(temporary))
            {
                await source.CopyToAsync(destination);
            }
            File.Move(temporary, destinationPath);
            temporary = null;
            ShowTab(currentTab);
        }
        catch (Exception exception)
        {
            if (temporary is not null)
            {
                TryDeleteFile(temporary);
            }
            MobileLog.Write("Не удалось импортировать ресурс.", exception);
            await DisplayAlertAsync("Не удалось добавить файл", "Проверь доступное место и попробуй ещё раз.", "Понятно");
        }
    }

    private async Task ShowResourceMenuAsync(string file, string resourceType)
    {
        string? action;
        if (resourceType == "sprites")
        {
            action = await DisplayActionSheetAsync(
                Path.GetFileName(file), "Отмена", "Удалить", "Редактировать", "Скопировать образ", "Скопировать имя", "Переименовать");
            if (action == "Редактировать")
            {
                await OpenPaintingAsync(file);
                return;
            }
            if (action == "Скопировать образ")
            {
                await CopyResourceAsync(file, "образ");
                return;
            }
            if (action == "Скопировать имя")
            {
                await CopyResourceNameAsync(file);
                return;
            }
            if (action == "Переименовать")
            {
                await RenameResourceAsync(file);
                return;
            }
        }
        else
        {
            action = await DisplayActionSheetAsync(
                Path.GetFileName(file), "Отмена", "Удалить", "Скопировать звук", "Скопировать имя", "Переименовать");
            if (action == "Скопировать звук")
            {
                await CopyResourceAsync(file, "звук");
                return;
            }
            if (action == "Скопировать имя")
            {
                await CopyResourceNameAsync(file);
                return;
            }
            if (action == "Переименовать")
            {
                await RenameResourceAsync(file);
                return;
            }
        }

        if (action != "Удалить")
        {
            return;
        }
        if (await DisplayAlertAsync("Удалить файл?", Path.GetFileName(file), "Удалить", "Отмена"))
        {
            try
            {
                File.Delete(file);
                ShowTab(currentTab);
            }
            catch (Exception exception)
            {
                MobileLog.Write("Не удалось удалить ресурс.", exception);
                await DisplayAlertAsync("Не удалось удалить файл", "Попробуй ещё раз.", "Понятно");
            }
        }
    }

    private async Task CopyResourceAsync(string file, string label)
    {
        try
        {
            var directory = Path.GetDirectoryName(file)!;
            var destination = AvailablePath(directory, Path.GetFileName(file));
            File.Copy(file, destination);
            ShowTab(currentTab);
            ShowToast($"{label} скопирован: {Path.GetFileName(destination)}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MobileLog.Write($"Не удалось скопировать {label}.", exception);
            await DisplayAlertAsync("Не удалось скопировать файл", "Проверь свободное место и попробуй ещё раз.", "Понятно");
        }
    }

    private async Task CopyResourceNameAsync(string file)
    {
        try
        {
            await Clipboard.Default.SetTextAsync(Path.GetFileName(file));
            ShowToast($"Имя скопировано: {Path.GetFileName(file)}");
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось скопировать имя ресурса.", exception);
            await DisplayAlertAsync("Не удалось скопировать имя", "Попробуй ещё раз.", "Понятно");
        }
    }

    private static void ShowToast(string message)
    {
#if ANDROID
        global::Android.Widget.Toast.MakeText(global::Android.App.Application.Context, message, global::Android.Widget.ToastLength.Short)?.Show();
#endif
    }

    private async Task RenameResourceAsync(string file)
    {
        var extension = Path.GetExtension(file);
        var oldName = Path.GetFileNameWithoutExtension(file);
        var name = await DisplayPromptAsync(
            "Переименовать файл", "Новое имя", "Переименовать", "Отмена", initialValue: oldName, maxLength: 50);
        if (name is null)
        {
            return;
        }
        name = name.Trim();
        if (name.Length == 0 || name.Length > 50 || name is "." or ".." || name.EndsWith('.') ||
            HasInvalidFileName(name))
        {
            await DisplayAlertAsync("Неподходящее имя", "Введи имя без недопустимых символов.", "Понятно");
            return;
        }
        var directory = Path.GetDirectoryName(file)!;
        var target = Path.Combine(directory, name + extension);
        if (string.Equals(target, file, StringComparison.Ordinal))
        {
            return;
        }
        if (File.Exists(target))
        {
            await DisplayAlertAsync("Файл уже существует", "Выбери другое имя.", "Понятно");
            return;
        }
        try
        {
            File.Move(file, target);
            ShowTab(currentTab);
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось переименовать ресурс.", exception);
            await DisplayAlertAsync("Не удалось переименовать файл", "Попробуй ещё раз.", "Понятно");
        }
    }

    private async Task ShowObjectMenuAsync()
    {
        var action = await DisplayActionSheetAsync(item.Name, "Закрыть", null, "Информация об объекте");
        if (action == "Информация об объекте")
        {
            await DisplayAlertAsync(item.Name, $"Сцена: {scene.Name}\nПроект: {project.Name}", "Готово");
        }
    }

    private static readonly char[] WindowsInvalidFileNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static bool HasInvalidFileName(string name) =>
        name.Any(character => character < ' ') ||
        name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
        name.IndexOfAny(WindowsInvalidFileNameChars) >= 0;

    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var safeName = string.Concat(name.Select(character =>
            character < ' ' || WindowsInvalidFileNameChars.Contains(character) ? '_' : character)).Trim();
        return safeName.Length == 0 ? "файл" : safeName;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось удалить временный файл ресурса.", exception);
        }
    }

    private static string AvailablePath(string directory, string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        var candidate = Path.Combine(directory, safeName);
        if (!File.Exists(candidate)) return candidate;
        var extension = Path.GetExtension(safeName);
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var suffix = 2;
        do
        {
            candidate = Path.Combine(directory, $"{stem} {suffix++}{extension}");
        }
        while (File.Exists(candidate));
        return candidate;
    }
}
