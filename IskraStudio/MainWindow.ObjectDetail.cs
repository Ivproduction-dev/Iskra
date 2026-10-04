using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;

namespace IskraStudio;

public partial class MainWindow
{
    private enum ObjectDetailTab
    {
        Images,
        Sounds,
        Scripts
    }

    private bool showingDetail;
    private IskraScene? detailScene;
    private IskraObject? detailItem;
    private ObjectDetailTab detailTab = ObjectDetailTab.Images;
    private Popup? activeDetailPopup;

    private readonly MediaPlayer soundPreviewPlayer = new();
    private string? soundPreviewPath;
    private Button? soundPreviewButton;

    private WaveInEvent? recorder;
    private WaveFileWriter? recorderWriter;
    private DispatcherTimer? recordingTimer;
    private DateTime recordingStarted;
    private bool isRecording;

    private void ShowObjectDetail(IskraScene scene, IskraObject item)
    {
        if (activeProject is null)
        {
            return;
        }

        StopDetailMedia();
        detailScene = scene;
        detailItem = item;
        detailTab = ObjectDetailTab.Images;
        showingDetail = true;
        workspaceSelectionMode = WorkspaceSelectionMode.None;
        selectedSceneIds.Clear();
        WorkspaceTitle.Text = item.Name;
        WorkspaceBackButton.Content = "←  " + scene.Name;
        WorkspaceMenuButton.Visibility = Visibility.Collapsed;
        WorkspaceConfirmSelectionButton.Visibility = Visibility.Collapsed;
        WorkspaceCancelSelectionButton.Visibility = Visibility.Collapsed;
        CreateProjectButton.Visibility = Visibility.Collapsed;
        UpdateFloatingCreateButton();
        BuildObjectDetail();
    }

    private void StopDetailMedia()
    {
        try
        {
            soundPreviewPlayer.Stop();
        }
        catch
        {
            // Игнорируем: превью звука необязательно.
        }
        soundPreviewPath = null;
        soundPreviewButton = null;
        StopRecording();
    }

    private void BuildObjectDetail()
    {
        WorkspaceContentHost.Children.Clear();
        if (activeProject is null || detailScene is null || detailItem is null)
        {
            return;
        }

        var layout = new DockPanel { LastChildFill = true };

        var strip = new Border
        {
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var tabGrid = new Grid { Margin = new Thickness(32, 0, 32, 0) };
        tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddDetailTab(tabGrid, "Образы", ObjectDetailTab.Images, 0);
        AddDetailTab(tabGrid, "Звуки", ObjectDetailTab.Sounds, 1);
        AddDetailTab(tabGrid, "Скрипты", ObjectDetailTab.Scripts, 2);
        strip.Child = tabGrid;
        DockPanel.SetDock(strip, Dock.Top);
        layout.Children.Add(strip);

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var content = new StackPanel
        {
            Margin = new Thickness(32, 26, 32, 40),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        BindingOperations.SetBinding(content, FrameworkElement.WidthProperty, new Binding(nameof(ScrollViewer.ActualWidth))
        {
            Source = scrollViewer,
            Converter = new InsetWidthConverter(),
            ConverterParameter = 64.0
        });

        switch (detailTab)
        {
            case ObjectDetailTab.Images:
                BuildImagesPage(content);
                break;
            case ObjectDetailTab.Sounds:
                BuildSoundsPage(content);
                break;
            default:
                BuildScriptsPage(content);
                break;
        }

        scrollViewer.Content = content;
        layout.Children.Add(scrollViewer);
        WorkspaceContentHost.Children.Add(layout);

        content.Opacity = 0;
        content.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    private void AddDetailTab(Grid grid, string label, ObjectDetailTab tab, int column)
    {
        var selected = detailTab == tab;
        var cell = new Grid();
        cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
        var button = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = new TextBlock
            {
                Text = label,
                FontSize = 15,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Regular,
                Foreground = BrushFor(selected ? "AccentBrush" : "BodyBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            },
            Padding = new Thickness(12, 12, 12, 9),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Click += (_, _) =>
        {
            if (detailTab != tab)
            {
                detailTab = tab;
                BuildObjectDetail();
            }
        };
        AutomationProperties.SetName(button, $"Вкладка {label}");
        cell.Children.Add(button);
        var underline = new Border
        {
            Background = BrushFor(selected ? "AccentBrush" : "PanelBrush"),
            Height = 3
        };
        Grid.SetRow(underline, 1);
        cell.Children.Add(underline);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private void BuildScriptsPage(StackPanel content)
    {
        if (activeProject is null || detailScene is null || detailItem is null)
        {
            return;
        }

        string text;
        try
        {
            text = projectContentStore.ReadObjectScript(activeProject, detailScene, detailItem.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Не удалось прочитать скрипт объекта.", exception);
            text = string.Empty;
        }

        var box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 320,
            FontFamily = new FontFamily("JetBrains Mono, Cascadia Code, Consolas"),
            FontSize = 14,
            Text = text,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        content.Children.Add(new Border
        {
            Background = BrushFor("CodeBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = box
        });

        var status = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            FontSize = 12,
            Foreground = BrushFor("MutedBrush")
        };
        var saveRow = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 12, 0, 0) };
        var saveButton = new Button
        {
            Style = (Style)FindResource("PrimaryButton"),
            Content = "Сохранить",
            Padding = new Thickness(18, 8, 18, 8)
        };
        saveButton.Click += (_, _) =>
        {
            if (activeProject is null || detailScene is null || detailItem is null)
            {
                return;
            }
            if (projectContentStore.TryWriteObjectScript(activeProject, detailScene, detailItem.Id, box.Text, out var error))
            {
                status.Text = "Сохранено";
                status.Foreground = BrushFor("SuccessBrush");
            }
            else
            {
                status.Text = error ?? "Не удалось сохранить скрипт.";
                status.Foreground = BrushFor("ErrorBrush");
            }
        };
        AutomationProperties.SetName(saveButton, "Сохранить скрипт");
        DockPanel.SetDock(saveButton, Dock.Right);
        saveRow.Children.Add(saveButton);
        content.Children.Add(saveRow);
        content.Children.Add(status);
    }

    private void BuildImagesPage(StackPanel content)
    {
        if (activeProject is null || detailScene is null || detailItem is null)
        {
            return;
        }

        var directory = projectContentStore.GetObjectResourceDirectory(activeProject, detailScene, detailItem.Id, "sprites");
        var files = ProjectContentStore.ListResourceFiles(directory, ProjectContentStore.ImageExtensions);

        content.Children.Add(CreateDetailHeader("Образы", files.Count, ShowImagesMenu));
        if (files.Count == 0)
        {
            content.Children.Add(CreateEmptyState("Нажмите + чтобы добавить образ", ShowImagesMenu));
            return;
        }

        var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        for (var index = 0; index < files.Count; index++)
        {
            wrap.Children.Add(CreateImageThumb(files[index], index == 0));
        }
        content.Children.Add(wrap);
    }

    private void ShowImagesMenu(Button anchor)
    {
        ShowDetailMenu(anchor, ("Нарисовать", () =>
            MessageBox.Show(this, "Рисовалка появится позже. Пока образы можно импортировать из файлов.", "Искра Студио", MessageBoxButton.OK, MessageBoxImage.Information)),
            ("Импортировать", ImportImages));
    }

    private void ImportImages()
    {
        if (activeProject is null || detailScene is null || detailItem is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",
            Title = "Выбери картинки для образа"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var directory = projectContentStore.GetObjectResourceDirectory(activeProject, detailScene, detailItem.Id, "sprites");
        foreach (var source in dialog.FileNames)
        {
            try
            {
                File.Copy(source, FreeFilePath(directory, SafeFileName(source)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Write("Не удалось импортировать образ.", exception);
                MessageBox.Show(this, $"Не удалось импортировать файл {Path.GetFileName(source)}.", "Искра Студио", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        BuildObjectDetail();
    }

    private UIElement CreateImageThumb(string path, bool isPreview)
    {
        var thumb = new Border
        {
            Width = 132,
            Margin = new Thickness(0, 0, 12, 12),
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8)
        };
        var layout = new StackPanel();
        var imageBox = new Border
        {
            Height = 100,
            CornerRadius = new CornerRadius(6),
            Background = BrushFor("SoftBrush"),
            ClipToBounds = true
        };
        if (LoadPreviewImage(path) is { } preview)
        {
            imageBox.Child = new Image { Source = preview, Stretch = Stretch.Uniform };
        }
        layout.Children.Add(imageBox);
        var nameRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 8, 0, 0) };
        var name = new TextBlock
        {
            Text = (isPreview ? "Превью · " : string.Empty) + Path.GetFileName(path),
            FontSize = 11,
            Foreground = BrushFor(isPreview ? "AccentBrush" : "MutedBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        nameRow.Children.Add(name);
        var deleteButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = "×",
            FontSize = 14,
            Padding = new Thickness(6, 0, 6, 2),
            ToolTip = "Удалить образ"
        };
        deleteButton.Click += (_, _) =>
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Write("Не удалось удалить образ.", exception);
            }
            BuildObjectDetail();
        };
        DockPanel.SetDock(deleteButton, Dock.Right);
        nameRow.Children.Add(deleteButton);
        layout.Children.Add(nameRow);
        thumb.Child = layout;
        return thumb;
    }

    private void BuildSoundsPage(StackPanel content)
    {
        if (activeProject is null || detailScene is null || detailItem is null)
        {
            return;
        }

        var directory = projectContentStore.GetObjectResourceDirectory(activeProject, detailScene, detailItem.Id, "sounds");
        var files = ProjectContentStore.ListResourceFiles(directory, ProjectContentStore.AudioExtensions);

        content.Children.Add(CreateDetailHeader("Звуки", files.Count, ShowSoundsMenu));
        if (isRecording && recordingLabel is not null)
        {
            content.Children.Add(CreateRecordingBar());
        }
        if (files.Count == 0 && !isRecording)
        {
            content.Children.Add(CreateEmptyState("Нажмите + чтобы добавить звук", ShowSoundsMenu));
            return;
        }

        foreach (var path in files)
        {
            content.Children.Add(CreateSoundRow(path));
        }
    }

    private void ShowSoundsMenu(Button anchor)
    {
        ShowDetailMenu(anchor, ("Выбрать из файла", ImportSounds), ("Записать с микрофона", StartRecording));
    }

    private void ImportSounds()
    {
        if (activeProject is null || detailScene is null || detailItem is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Звуки|*.mp3;*.m4a;*.wav;*.ogg;*.wma;*.flac",
            Title = "Выбери звуковые файлы"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var directory = projectContentStore.GetObjectResourceDirectory(activeProject, detailScene, detailItem.Id, "sounds");
        foreach (var source in dialog.FileNames)
        {
            try
            {
                File.Copy(source, FreeFilePath(directory, SafeFileName(source)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Write("Не удалось импортировать звук.", exception);
                MessageBox.Show(this, $"Не удалось импортировать файл {Path.GetFileName(source)}.", "Искра Студио", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        BuildObjectDetail();
    }

    private UIElement CreateSoundRow(string path)
    {
        var row = new Border
        {
            Margin = new Thickness(0, 0, 0, 12),
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10)
        };
        var layout = new DockPanel { LastChildFill = true };
        var playButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = "▶",
            FontSize = 15,
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = "Слушать",
            Margin = new Thickness(0, 0, 10, 0)
        };
        playButton.Click += (_, _) => ToggleSoundPreview(path, playButton);
        AutomationProperties.SetName(playButton, $"Слушать {Path.GetFileName(path)}");
        DockPanel.SetDock(playButton, Dock.Left);
        layout.Children.Add(playButton);
        var deleteButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = "×",
            FontSize = 15,
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = "Удалить звук"
        };
        deleteButton.Click += (_, _) =>
        {
            if (string.Equals(soundPreviewPath, path, StringComparison.OrdinalIgnoreCase))
            {
                StopSoundPreview();
            }
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Write("Не удалось удалить звук.", exception);
            }
            BuildObjectDetail();
        };
        DockPanel.SetDock(deleteButton, Dock.Right);
        layout.Children.Add(deleteButton);
        layout.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(path),
            FontSize = 14,
            Foreground = BrushFor("InkBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        row.Child = layout;

        if (string.Equals(soundPreviewPath, path, StringComparison.OrdinalIgnoreCase))
        {
            soundPreviewButton = playButton;
            playButton.Content = "⏹";
        }
        return row;
    }

    private void ToggleSoundPreview(string path, Button button)
    {
        if (string.Equals(soundPreviewPath, path, StringComparison.OrdinalIgnoreCase))
        {
            StopSoundPreview();
            button.Content = "▶";
            return;
        }

        StopSoundPreview();
        try
        {
            soundPreviewPlayer.Open(new Uri(path, UriKind.Absolute));
            soundPreviewPlayer.Play();
            soundPreviewPath = path;
            soundPreviewButton = button;
            button.Content = "⏹";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Write("Не удалось проиграть звук.", exception);
            MessageBox.Show(this, "Не удалось проиграть этот файл.", "Искра Студио", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopSoundPreview()
    {
        try
        {
            soundPreviewPlayer.Stop();
            soundPreviewPlayer.Close();
        }
        catch
        {
            // Игнорируем: превью звука необязательно.
        }
        if (soundPreviewButton is not null)
        {
            soundPreviewButton.Content = "▶";
            soundPreviewButton = null;
        }
        soundPreviewPath = null;
    }

    private void StartRecording()
    {
        if (isRecording || activeProject is null || detailScene is null || detailItem is null)
        {
            return;
        }

        try
        {
            var directory = projectContentStore.GetObjectResourceDirectory(activeProject, detailScene, detailItem.Id, "sounds");
            var path = FreeFilePath(directory, "Запись.wav");
            recorder = new WaveInEvent { WaveFormat = new WaveFormat(44100, 1) };
            recorderWriter = new WaveFileWriter(path, recorder.WaveFormat);
            recorder.DataAvailable += (_, e) => recorderWriter?.Write(e.Buffer, 0, e.BytesRecorded);
            recorder.RecordingStopped += (_, _) =>
            {
                recorderWriter?.Dispose();
                recorderWriter = null;
                recorder?.Dispose();
                recorder = null;
                Dispatcher.BeginInvoke(() =>
                {
                    isRecording = false;
                    recordingTimer?.Stop();
                    BuildObjectDetail();
                });
            };
            recordingStarted = DateTime.UtcNow;
            recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            recordingTimer.Tick += (_, _) =>
            {
                if (recordingLabel is not null)
                {
                    recordingLabel.Text = "● Идёт запись " + DateTime.UtcNow.Subtract(recordingStarted).ToString(@"m\:ss");
                }
            };
            recorder.StartRecording();
            isRecording = true;
            recordingTimer.Start();
            BuildObjectDetail();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Write("Не удалось начать запись с микрофона.", exception);
            MessageBox.Show(this, "Не удалось начать запись. Проверь микрофон.", "Искра Студио", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopRecording()
    {
        if (!isRecording)
        {
            return;
        }
        isRecording = false;
        recordingTimer?.Stop();
        recordingTimer = null;
        try
        {
            recorder?.StopRecording();
        }
        catch
        {
            // Игнорируем: запись уже остановлена устройством.
        }
    }

    private TextBlock? recordingLabel;

    private UIElement CreateRecordingBar()
    {
        var bar = new Border
        {
            Margin = new Thickness(0, 0, 0, 12),
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("ErrorBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10)
        };
        var layout = new DockPanel { LastChildFill = true };
        recordingLabel = new TextBlock
        {
            Text = "● Идёт запись " + DateTime.UtcNow.Subtract(recordingStarted).ToString(@"m\:ss"),
            FontSize = 14,
            Foreground = BrushFor("ErrorBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        layout.Children.Add(recordingLabel);
        var stopButton = new Button
        {
            Style = (Style)FindResource("PrimaryButton"),
            Content = "Остановить",
            Padding = new Thickness(14, 6, 14, 6)
        };
        stopButton.Click += (_, _) => StopRecording();
        DockPanel.SetDock(stopButton, Dock.Right);
        layout.Children.Add(stopButton);
        bar.Child = layout;
        return bar;
    }

    private UIElement CreateDetailHeader(string title, int count, Action<Button> add)
    {
        var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = count == 0 ? title : $"{title} · {count}",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFor("InkBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var addButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = "＋",
            FontSize = 18,
            Padding = new Thickness(12, 4, 12, 4),
            ToolTip = "Добавить"
        };
        addButton.Click += (_, _) => add(addButton);
        AutomationProperties.SetName(addButton, $"Добавить в {title.ToLowerInvariant()}");
        DockPanel.SetDock(addButton, Dock.Right);
        header.Children.Add(addButton);
        return header;
    }

    private UIElement CreateEmptyState(string hint, Action<Button> add)
    {
        var panel = new Border
        {
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 34, 22, 34)
        };
        var layout = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        layout.Children.Add(new TextBlock
        {
            Text = hint,
            FontSize = 14,
            Foreground = BrushFor("MutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18)
        });
        var addButton = new Button
        {
            Style = (Style)FindResource("FloatingActionButton"),
            Content = "+",
            ToolTip = hint,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        addButton.Click += (_, _) => add(addButton);
        AutomationProperties.SetName(addButton, hint);
        layout.Children.Add(addButton);
        panel.Child = layout;
        return panel;
    }

    private void ShowDetailMenu(Button anchor, params (string Label, Action Run)[] items)
    {
        CloseDetailMenu();

        var popup = new Popup
        {
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true
        };
        var panel = new Border
        {
            Width = 240,
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6)
        };
        var list = new StackPanel();
        foreach (var (label, run) in items)
        {
            var button = new Button
            {
                Style = (Style)FindResource("AppMenuItemButton"),
                Content = label
            };
            button.Click += (_, _) =>
            {
                CloseDetailMenu();
                run();
            };
            list.Children.Add(button);
        }
        var cancelButton = new Button
        {
            Style = (Style)FindResource("AppMenuItemButton"),
            Content = "Отмена"
        };
        cancelButton.Click += (_, _) => CloseDetailMenu();
        list.Children.Add(cancelButton);
        panel.Child = list;
        popup.Child = panel;
        popup.PlacementTarget = anchor;
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(activeDetailPopup, popup))
            {
                activeDetailPopup = null;
            }
        };
        activeDetailPopup = popup;
        popup.IsOpen = true;
    }

    private void CloseDetailMenu()
    {
        if (activeDetailPopup is not null)
        {
            activeDetailPopup.IsOpen = false;
            activeDetailPopup = null;
        }
    }

    private static string SafeFileName(string source)
    {
        var name = Path.GetFileName(source);
        foreach (var bad in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(bad, '_');
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Файл" + Path.GetExtension(source);
        }
        return name;
    }

    private static string FreeFilePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var counter = 1;
        do
        {
            counter++;
            candidate = Path.Combine(directory, $"{stem} ({counter}){extension}");
        }
        while (File.Exists(candidate));
        return candidate;
    }
}
