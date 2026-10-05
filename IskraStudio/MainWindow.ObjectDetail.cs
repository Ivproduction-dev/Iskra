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
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
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

    private readonly MediaPlayer soundPreviewPlayer = new();
    private string? soundPreviewPath;
    private Button? soundPreviewButton;

    private WaveInEvent? recorder;
    private WaveFileWriter? recorderWriter;
    private readonly object recorderLock = new object();
    private DispatcherTimer? recordingTimer;
    private DateTime recordingStarted;
    private bool isRecording;

    private void ShowObjectDetail(IskraScene scene, IskraObject item, ObjectDetailTab tab = ObjectDetailTab.Images)
    {
        if (activeProject is null)
        {
            return;
        }

        StopDetailMedia();
        detailScene = scene;
        detailItem = item;
        detailTab = tab;
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
        FlushScriptSave();
        detailScriptEditor = null;
        detailScriptStatus = null;
        detailScriptDirty = false;
        try
        {
            soundPreviewPlayer.Stop();
        }
        catch
        {
        }
        soundPreviewPath = null;
        soundPreviewButton = null;
        StopRecording();
    }

    private void BuildObjectDetail(int slideDirection = 0)
    {
        WorkspaceContentHost.Children.Clear();
        if (activeProject is null || detailScene is null || detailItem is null)
        {
            return;
        }

        var layout = new DockPanel { LastChildFill = true };
        AttachDetailGestures(layout);

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
        AddDetailTab(tabGrid, "Образы", "eye", ObjectDetailTab.Images, 0);
        AddDetailTab(tabGrid, "Звуки", "sound", ObjectDetailTab.Sounds, 1);
        AddDetailTab(tabGrid, "Скрипты", "code", ObjectDetailTab.Scripts, 2);
        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 6)
        };
        for (var dotIndex = 0; dotIndex <= (int)ObjectDetailTab.Scripts; dotIndex++)
        {
            dots.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 6,
                Height = 6,
                Margin = new Thickness(3, 0, 3, 0),
                Fill = BrushFor(dotIndex == (int)detailTab ? "AccentBrush" : "HairlineBrush")
            });
        }
        var stripStack = new StackPanel();
        stripStack.Children.Add(tabGrid);
        stripStack.Children.Add(dots);
        strip.Child = stripStack;
        DockPanel.SetDock(strip, Dock.Top);
        layout.Children.Add(strip);

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        ApplyThinScrollBars(scrollViewer);
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

        var root = new Grid();
        root.Children.Add(layout);

        if (detailTab != ObjectDetailTab.Scripts)
        {
            var floatingAdd = new Button
            {
                Style = (Style)FindResource("FloatingActionButton"),
                Content = "＋",
                ToolTip = detailTab == ObjectDetailTab.Images ? "Добавить образ" : "Добавить звук",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 18)
            };
            floatingAdd.Click += (_, _) =>
            {
                if (detailTab == ObjectDetailTab.Images)
                {
                    ShowDetailSheet(("Нарисовать", "pencil", () =>
                    {
                        if (detailScene is null || detailItem is null)
                        {
                            return;
                        }
                        ShowSpriteNameDialog("Новый рисунок", "Как назвать спрайт?", "Рисовать",
                            spriteName => StartPainting(detailScene, detailItem, null, 1024, 1024, spriteName));
                    }), ("Импортировать", "import", () =>
                    {
                        ShowSpriteNameDialog("Импорт образов", "Как назвать спрайт? Для нескольких файлов добавятся номера.", "Выбрать",
                            spriteName => ImportImagesNamed(spriteName));
                    }));
                }
                else
                {
                    ShowDetailSheet(("Выбрать из файла", "file", ImportSounds), ("Записать с микрофона", "mic", StartRecording));
                }
            };
            AutomationProperties.SetName(floatingAdd, floatingAdd.ToolTip.ToString());
            Panel.SetZIndex(floatingAdd, 15);
            root.Children.Add(floatingAdd);
        }

        detailSheetOverlay = new Grid
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0))
        };
        detailSheetOverlay.MouseDown += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, detailSheetOverlay))
            {
                CloseDetailSheet();
            }
        };
        Panel.SetZIndex(detailSheetOverlay, 20);
        root.Children.Add(detailSheetOverlay);

        WorkspaceContentHost.Children.Add(root);

        content.Opacity = 0;
        detailDragContent = content;
        var slide = new TranslateTransform { X = slideDirection * 60 };
        content.RenderTransform = slide;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        content.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
        slide.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(slideDirection * 60, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
    }

    private void SwitchDetailTab(ObjectDetailTab tab, int direction)
    {
        if (detailTab == tab)
        {
            return;
        }
        FlushScriptSave();
        StopSoundPreview();
        detailTab = tab;
        BuildObjectDetail(direction);
    }

    private Point? detailPressPoint;
    private bool detailPressTouch;
    private bool detailPressAllowed;
    private StackPanel? detailDragContent;

    private void AttachDetailGestures(DockPanel layout)
    {
        layout.PreviewTouchDown += (_, e) =>
        {
            detailPressPoint = e.GetTouchPoint(layout).Position;
            detailPressTouch = true;
            detailPressAllowed = true;
        };
        layout.PreviewTouchMove += (_, e) =>
        {
            if (detailPressPoint is not { } start || !detailPressTouch || !detailPressAllowed)
            {
                return;
            }
            var position = e.GetTouchPoint(layout).Position;
            if (Math.Abs(position.Y - start.Y) > Math.Abs(position.X - start.X) + 10)
            {
                CancelDetailDrag();
                return;
            }
            DragDetailContent(position.X - start.X);
        };
        layout.PreviewTouchUp += (_, e) =>
        {
            if (detailPressPoint is not { } start || !detailPressTouch || !detailPressAllowed)
            {
                detailPressPoint = null;
                return;
            }
            detailPressPoint = null;
            FinishDetailDrag(e.GetTouchPoint(layout).Position.X - start.X, 80);
        };
        layout.PreviewMouseLeftButtonDown += (_, e) =>
        {
            detailPressAllowed = !IsDetailSwipeBlocked(e.OriginalSource as DependencyObject);
            detailPressPoint = detailPressAllowed ? e.GetPosition(layout) : null;
            detailPressTouch = false;
        };
        layout.PreviewMouseMove += (_, e) =>
        {
            if (detailPressPoint is not { } start || detailPressTouch || !detailPressAllowed || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }
            DragDetailContent(e.GetPosition(layout).X - start.X);
        };
        layout.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (detailPressPoint is not { } start || detailPressTouch || !detailPressAllowed)
            {
                detailPressPoint = null;
                return;
            }
            detailPressPoint = null;
            FinishDetailDrag(e.GetPosition(layout).X - start.X, 120);
        };
    }

    private static bool IsDetailSwipeBlocked(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is TextEditor || current is ButtonBase || current is ScrollBar || current is TextBoxBase)
            {
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void DragDetailContent(double delta)
    {
        if (detailDragContent?.RenderTransform is not TranslateTransform slide)
        {
            slide = new TranslateTransform();
            if (detailDragContent is not null)
            {
                detailDragContent.RenderTransform = slide;
            }
        }
        slide.X = Math.Clamp(delta, -140, 140);
    }

    private void FinishDetailDrag(double delta, double threshold)
    {
        if (Math.Abs(delta) >= threshold)
        {
            SwipeDetailTab(delta < 0 ? 1 : -1);
            return;
        }
        SnapDetailContentBack();
    }

    private void CancelDetailDrag()
    {
        detailPressPoint = null;
        SnapDetailContentBack();
    }

    private void SnapDetailContentBack()
    {
        if (detailDragContent?.RenderTransform is TranslateTransform slide)
        {
            slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(slide.X, 0, TimeSpan.FromMilliseconds(180))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    private void SwipeDetailTab(int direction)
    {
        var index = (int)detailTab + direction;
        if (index < 0 || index > (int)ObjectDetailTab.Scripts)
        {
            return;
        }
        SwitchDetailTab((ObjectDetailTab)index, direction);
    }

    private void AddDetailTab(Grid grid, string label, string icon, ObjectDetailTab tab, int column)
    {
        var selected = detailTab == tab;
        var cell = new Grid();
        cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
        var button = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Padding = new Thickness(12, 9, 12, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var cellContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        cellContent.Children.Add(CreateSheetIcon(icon, 26, BrushFor(selected ? "AccentBrush" : "BodyBrush")));
        cellContent.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Regular,
            Foreground = BrushFor(selected ? "AccentBrush" : "BodyBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        });
        button.Content = cellContent;
        button.Click += (_, _) =>
        {
            SwitchDetailTab(tab, Math.Sign((int)tab - (int)detailTab));
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

    private static IHighlightingDefinition? iskraHighlighting;

    private IHighlightingDefinition IskraHighlighting => iskraHighlighting ??= LoadIskraHighlighting();

    private static IHighlightingDefinition LoadIskraHighlighting()
    {
        const string definition = """
            <SyntaxDefinition name="Искра" extensions=".isk" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Keyword" foreground="#A9583E" fontWeight="bold" />
              <Color name="Number" foreground="#2F7947" />
              <RuleSet>
                <Rule color="Keyword">(?i)\b(задать|присвоить|изменить|печать)\b</Rule>
                <Rule color="Number">\b\d+\b</Rule>
              </RuleSet>
            </SyntaxDefinition>
            """;
        using var reader = System.Xml.XmlReader.Create(new StringReader(definition));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private void ApplyThinScrollBars(FrameworkElement scope)
    {
        var muted = ((SolidColorBrush)BrushFor("MutedBrush")).Color;
        var accent = ((SolidColorBrush)BrushFor("AccentBrush")).Color;
        string hex(byte a, Color c) => $"#{a:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        var thumbIdle = hex(0x77, muted);
        var thumbHover = hex(0xFF, accent);

        ControlTemplate vertical = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
            "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"ScrollBar\">" +
            "<Grid Width=\"10\" Background=\"Transparent\">" +
            "<Track Name=\"PART_Track\" IsDirectionReversed=\"True\">" +
            "<Track.DecreaseRepeatButton><RepeatButton Command=\"ScrollBar.PageUpCommand\" Opacity=\"0\" Focusable=\"False\" /></Track.DecreaseRepeatButton>" +
            "<Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType=\"Thumb\">" +
            "<Border CornerRadius=\"4\" Margin=\"2,1\" MinHeight=\"24\" Background=\"" + thumbIdle + "\" Name=\"Body\" />" +
            "<ControlTemplate.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\">" +
            "<Setter TargetName=\"Body\" Property=\"Background\" Value=\"" + thumbHover + "\" />" +
            "</Trigger></ControlTemplate.Triggers>" +
            "</ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
            "<Track.IncreaseRepeatButton><RepeatButton Command=\"ScrollBar.PageDownCommand\" Opacity=\"0\" Focusable=\"False\" /></Track.IncreaseRepeatButton>" +
            "</Track></Grid></ControlTemplate>");

        ControlTemplate horizontal = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
            "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"ScrollBar\">" +
            "<Grid Height=\"10\" Background=\"Transparent\">" +
            "<Track Name=\"PART_Track\" IsDirectionReversed=\"False\">" +
            "<Track.DecreaseRepeatButton><RepeatButton Command=\"ScrollBar.PageLeftCommand\" Opacity=\"0\" Focusable=\"False\" /></Track.DecreaseRepeatButton>" +
            "<Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType=\"Thumb\">" +
            "<Border CornerRadius=\"4\" Margin=\"1,2\" MinWidth=\"24\" Background=\"" + thumbIdle + "\" Name=\"Body\" />" +
            "<ControlTemplate.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\">" +
            "<Setter TargetName=\"Body\" Property=\"Background\" Value=\"" + thumbHover + "\" />" +
            "</Trigger></ControlTemplate.Triggers>" +
            "</ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
            "<Track.IncreaseRepeatButton><RepeatButton Command=\"ScrollBar.PageRightCommand\" Opacity=\"0\" Focusable=\"False\" /></Track.IncreaseRepeatButton>" +
            "</Track></Grid></ControlTemplate>");

        var style = new Style(typeof(ScrollBar));
        var verticalTrigger = new Trigger { Property = ScrollBar.OrientationProperty, Value = Orientation.Vertical };
        verticalTrigger.Setters.Add(new Setter(ScrollBar.TemplateProperty, vertical));
        var horizontalTrigger = new Trigger { Property = ScrollBar.OrientationProperty, Value = Orientation.Horizontal };
        horizontalTrigger.Setters.Add(new Setter(ScrollBar.TemplateProperty, horizontal));
        style.Triggers.Add(verticalTrigger);
        style.Triggers.Add(horizontalTrigger);
        scope.Resources.Add(typeof(ScrollBar), style);
    }

    private TextEditor? detailScriptEditor;
    private TextBlock? detailScriptStatus;
    private DispatcherTimer? detailSaveTimer;
    private bool detailScriptDirty;

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

        var editor = new TextEditor
        {
            FontFamily = new FontFamily("JetBrains Mono, Cascadia Code, Consolas"),
            FontSize = 14,
            ShowLineNumbers = true,
            WordWrap = true,
            Background = BrushFor("SoftBrush"),
            Foreground = BrushFor("InkBrush"),
            LineNumbersForeground = BrushFor("MutedBrush"),
            SyntaxHighlighting = IskraHighlighting,
            Text = text,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        ApplyThinScrollBars(editor);
        content.Children.Add(new Border
        {
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Height = 380,
            Child = editor
        });

        var status = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            FontSize = 12,
            Foreground = BrushFor("MutedBrush")
        };
        detailScriptStatus = status;
        detailScriptEditor = editor;
        detailScriptDirty = false;
        editor.TextChanged += (_, _) =>
        {
            detailScriptDirty = true;
            if (detailSaveTimer is null)
            {
                detailSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                detailSaveTimer.Tick += (_, _) => FlushScriptSave();
            }
            detailSaveTimer.Stop();
            detailSaveTimer.Start();
        };
        content.Children.Add(status);
    }

    private void FlushScriptSave()
    {
        detailSaveTimer?.Stop();
        if (!detailScriptDirty || detailScriptEditor is null || activeProject is null || detailScene is null || detailItem is null)
        {
            return;
        }
        if (projectContentStore.TryWriteObjectScript(activeProject, detailScene, detailItem.Id, detailScriptEditor.Text, out var error))
        {
            detailScriptDirty = false;
            if (detailScriptStatus is not null)
            {
                detailScriptStatus.Text = "Сохранено " + DateTime.Now.ToString("HH:mm:ss");
                detailScriptStatus.Foreground = BrushFor("SuccessBrush");
            }
        }
        else if (detailScriptStatus is not null)
        {
            detailScriptStatus.Text = error ?? "Не удалось сохранить скрипт.";
            detailScriptStatus.Foreground = BrushFor("ErrorBrush");
        }
    }

    private void BuildImagesPage(StackPanel content)
    {
        if (activeProject is null || detailScene is null || detailItem is null)
        {
            return;
        }

        var directory = projectContentStore.GetObjectResourceDirectory(activeProject, detailScene, detailItem.Id, "sprites");
        var files = ProjectContentStore.ListResourceFiles(directory, ProjectContentStore.ImageExtensions);

        content.Children.Add(CreateDetailHeader("Образы", files.Count));
        if (files.Count == 0)
        {
            content.Children.Add(CreateEmptyState("Нажмите + чтобы добавить образ"));
            return;
        }

        var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        for (var index = 0; index < files.Count; index++)
        {
            wrap.Children.Add(CreateImageThumb(files[index], index == 0));
        }
        content.Children.Add(wrap);
    }

    private UIElement CreateDetailHeader(string title, int count)
    {
        return new TextBlock
        {
            Text = count == 0 ? title : $"{title} · {count}",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFor("InkBrush"),
            Margin = new Thickness(0, 0, 0, 14)
        };
    }

    private UIElement CreateEmptyState(string hint)
    {
        return new Border
        {
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 34, 22, 34),
            Child = new TextBlock
            {
                Text = hint,
                FontSize = 14,
                Foreground = BrushFor("MutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
    }

    private void ShowSpriteNameDialog(string title, string subtitle, string buttonLabel, Action<string> next)
    {
        var overlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0))
        };
        Panel.SetZIndex(overlay, 40);
        var card = new Border
        {
            Width = 400,
            MaxWidth = 440,
            Margin = new Thickness(24),
            Padding = new Thickness(24),
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var layout = new StackPanel();
        layout.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFor("InkBrush")
        });
        layout.Children.Add(new TextBlock
        {
            Text = subtitle,
            Margin = new Thickness(0, 6, 0, 14),
            FontSize = 13,
            Foreground = BrushFor("MutedBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        var nameBox = new TextBox
        {
            Style = (Style)FindResource("ProjectTextBox"),
            MinHeight = 44,
            MaxLength = 50,
            ToolTip = "Например, Герой"
        };
        layout.Children.Add(nameBox);
        var error = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 12,
            Foreground = BrushFor("ErrorBrush"),
            TextWrapping = TextWrapping.Wrap
        };
        layout.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        var cancelButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = "Отмена",
            Padding = new Thickness(14, 9, 14, 9)
        };
        var goButton = new Button
        {
            Style = (Style)FindResource("PrimaryButton"),
            Content = buttonLabel,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(18, 9, 18, 9)
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(goButton);
        layout.Children.Add(buttons);
        card.Child = layout;
        overlay.Children.Add(card);
        overlay.MouseDown += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, overlay))
            {
                WorkspaceContentHost.Children.Remove(overlay);
            }
        };
        cancelButton.Click += (_, _) => WorkspaceContentHost.Children.Remove(overlay);
        goButton.Click += (_, _) =>
        {
            var name = nameBox.Text.Trim();
            if (name.Length == 0)
            {
                error.Text = "Введи имя спрайта.";
                error.Visibility = Visibility.Visible;
                nameBox.Focus();
                return;
            }
            if (name.Length > 50 || name is "." or ".." || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error.Text = "Имя слишком длинное или содержит недопустимые символы.";
                error.Visibility = Visibility.Visible;
                nameBox.Focus();
                return;
            }
            WorkspaceContentHost.Children.Remove(overlay);
            next(name);
        };
        WorkspaceContentHost.Children.Add(overlay);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => nameBox.Focus()));
    }

    private void ImportImagesNamed(string name)
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

        var directory = projectContentStore.EnsureObjectResourceDirectory(activeProject, detailScene, detailItem.Id, "sprites");
        var counter = 0;
        foreach (var source in dialog.FileNames)
        {
            try
            {
                var extension = Path.GetExtension(SafeFileName(source));
                var fileName = dialog.FileNames.Length == 1
                    ? name + extension
                    : $"{name} ({++counter}){extension}";
                File.Copy(source, FreeFilePath(directory, fileName));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Write("Не удалось импортировать образ.", exception);
                MessageBox.Show(this, $"Не удалось импортировать файл {Path.GetFileName(source)}.", "Искра Студио", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        BuildObjectDetail();
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

        var directory = projectContentStore.EnsureObjectResourceDirectory(activeProject, detailScene, detailItem.Id, "sprites");
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
        var checkerGeometry = new GeometryGroup();
        checkerGeometry.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
        checkerGeometry.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));
        var checkerDrawing = new DrawingGroup();
        checkerDrawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(240, 240, 240)), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        checkerDrawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(204, 204, 204)), null, checkerGeometry));
        var imageBox = new Border
        {
            Height = 100,
            CornerRadius = new CornerRadius(6),
            Background = new DrawingBrush
            {
                Viewport = new Rect(0, 0, 16, 16),
                ViewportUnits = BrushMappingMode.Absolute,
                TileMode = TileMode.Tile,
                Drawing = checkerDrawing
            },
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
        thumb.Cursor = Cursors.Hand;
        thumb.MouseLeftButtonUp += (_, e) =>
        {
            if (FindButtonAncestor(e.OriginalSource as DependencyObject) is not null)
            {
                return;
            }
            if (detailScene is not null && detailItem is not null)
            {
                StartPainting(detailScene, detailItem, path);
            }
        };
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

        content.Children.Add(CreateDetailHeader("Звуки", files.Count));
        if (isRecording && recordingLabel is not null)
        {
            content.Children.Add(CreateRecordingBar());
        }
        if (files.Count == 0 && !isRecording)
        {
            content.Children.Add(CreateEmptyState("Нажмите + чтобы добавить звук"));
            return;
        }

        foreach (var path in files)
        {
            content.Children.Add(CreateSoundRow(path));
        }
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

        var directory = projectContentStore.EnsureObjectResourceDirectory(activeProject, detailScene, detailItem.Id, "sounds");
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
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(path),
            FontSize = 14,
            Foreground = BrushFor("InkBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var meta = new TextBlock
        {
            FontSize = 11,
            Foreground = BrushFor("MutedBrush"),
            Margin = new Thickness(0, 2, 0, 0)
        };
        info.Children.Add(meta);
        layout.Children.Add(info);
        row.Child = layout;
        ProbeSoundLength(path, meta);

        if (string.Equals(soundPreviewPath, path, StringComparison.OrdinalIgnoreCase))
        {
            soundPreviewButton = playButton;
            playButton.Content = "⏹";
        }
        return row;
    }

    private void ProbeSoundLength(string path, TextBlock meta)
    {
        try
        {
            meta.Text = FormatFileSize(new FileInfo(path).Length);
        }
        catch
        {
        }
        try
        {
            var probe = new MediaPlayer();
            probe.MediaOpened += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (probe.NaturalDuration.HasTimeSpan)
                {
                    var prefix = string.IsNullOrEmpty(meta.Text) ? string.Empty : meta.Text + " · ";
                    meta.Text = prefix + probe.NaturalDuration.TimeSpan.ToString(@"m\:ss");
                }
                probe.Close();
            });
            probe.MediaFailed += (_, _) => Dispatcher.BeginInvoke(probe.Close);
            probe.Open(new Uri(path, UriKind.Absolute));
        }
        catch
        {
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return bytes + " Б";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " КБ";
        return (bytes / (1024.0 * 1024)).ToString("0.#") + " МБ";
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
            soundPreviewPlayer.MediaEnded -= OnSoundPreviewEnded;
            soundPreviewPlayer.MediaEnded += OnSoundPreviewEnded;
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

    private void OnSoundPreviewEnded(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(StopSoundPreview);
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

        string? recordingPath = null;
        try
        {
            var directory = projectContentStore.EnsureObjectResourceDirectory(activeProject, detailScene, detailItem.Id, "sounds");
            recordingPath = FreeFilePath(directory, "Запись.wav");
            var path = recordingPath;
            recorder = new WaveInEvent { WaveFormat = new WaveFormat(44100, 1) };
            recorderWriter = new WaveFileWriter(path, recorder.WaveFormat);
            recorder.DataAvailable += (_, e) =>
            {
                try
                {
                    lock (recorderLock)
                    {
                        recorderWriter?.Write(e.Buffer, 0, e.BytesRecorded);
                    }
                }
                catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException or ObjectDisposedException)
                {
                    AppLog.Write("Не удалось записать кусок звука.", writeException);
                }
            };
            recorder.RecordingStopped += (_, _) =>
            {
                lock (recorderLock)
                {
                    recorderWriter?.Dispose();
                    recorderWriter = null;
                    recorder?.Dispose();
                    recorder = null;
                }
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
            lock (recorderLock)
            {
                try
                {
                    recorderWriter?.Dispose();
                }
                catch
                {
                }
                recorderWriter = null;
                try
                {
                    recorder?.Dispose();
                }
                catch
                {
                }
                recorder = null;
            }
            recordingTimer?.Stop();
            recordingTimer = null;
            isRecording = false;
            try
            {
                if (recordingPath is not null && File.Exists(recordingPath))
                {
                    File.Delete(recordingPath);
                }
            }
            catch (Exception deleteException) when (deleteException is IOException or UnauthorizedAccessException)
            {
                AppLog.Write("Не удалось удалить недописанный файл записи.", deleteException);
            }
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

    private Grid? detailSheetOverlay;

    private FrameworkElement CreateSheetIcon(string kind, double size = 34, Brush? stroke = null)
    {
        var data = kind switch
        {
            "pencil" => "M4.5,19.5 L6.2,13.8 L15.2,4.8 L19.2,8.8 L10.2,17.8 Z M13.8,6.2 L17.8,10.2",
            "import" => "M9,2.5 H15 L21,8.5 V21.5 H9 Z M15,2.5 V8.5 H21 M2,12 H10 M7.5,9.5 L10,12 L7.5,14.5",
            "file" => "M8,2.5 H15 L21,8.5 V21.5 H8 Z M15,2.5 V8.5 H21 M11,13 H18 M11,16 H18 M11,19 H16",
            "eye" => "M2,12 C2,12 6,5.5 12,5.5 C18,5.5 22,12 22,12 C22,12 18,18.5 12,18.5 C6,18.5 2,12 2,12 Z M12,9.5 a2.5,2.5 0 1 0 0.01,0 Z",
            "sound" => "M4,9.5 H8 L13.5,5 V19 L8,14.5 H4 Z M16,9 a4.5,4.5 0 0 1 0,6 M18.5,6.5 a8,8 0 0 1 0,11",
            "code" => "M8.5,7.5 L4,12 L8.5,16.5 M15.5,7.5 L20,12 L15.5,16.5 M13.2,6.5 L10.8,17.5",
            "brush" => "M4.5,19.5 L14,10 M14,10 L19.5,4.5",
            "eraser" => "M7,14 L12,9 L19,16 L14,21 Z M3,21 H10",
            "fillbucket" => "M7,11 H13 L14.5,20 H8.5 Z M13,11 L17,5 M17,5 c1,1.5 1.5,2.5 1.5,3.5 a1.5,1.5 0 1 1 -3,0 C15.5,7.5 16,6.5 17,5 Z",
            "pipette" => "M16,4.5 a3,3 0 1 0 0.01,0 M13.8,6.8 L6,14.6 L4,20 L9.4,18 L17.2,10.2",
            "shape" => "M5,7 H19 V17 H5 Z",
            "ellipse" => "M12,5 C16.5,5 20,8 20,12 C20,16 16.5,19 12,19 C7.5,19 4,16 4,12 C4,8 7.5,5 12,5 Z",
            "line" => "M5,19 L19,5",
            "handmove" => "M12,3 V21 M3,12 H21 M12,3 L9.5,5.5 M12,3 L14.5,5.5 M12,21 L9.5,18.5 M12,21 L14.5,18.5 M3,12 L5.5,9.5 M3,12 L5.5,14.5 M21,12 L18.5,9.5 M21,12 L18.5,14.5",
            "undo" => "M9,5 H4 V10 M4,5 C4,12 8.5,17.5 15,17.5",
            "redo" => "M15,5 H20 V10 M20,5 C20,12 15.5,17.5 9,17.5",
            "spray" => "M10,14 H14 V21 H10 Z M11,14 L9,5 M13,14 L15,5 M12,14 L12,4",
            "text" => "M5,20 L12,4 L19,20 M8,15 H16",
            _ => "M10,12 V6 a4,4 0 0 1 8,0 V12 a4,4 0 0 1 -8,0 Z M6,12 a8,8 0 0 0 16,0 M14,20 V23 M10,23 H18"
        };
        return new Viewbox
        {
            Width = size,
            Height = size,
            Child = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data),
                Stroke = stroke ?? BrushFor("InkBrush"),
                StrokeThickness = 1.8,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Fill = Brushes.Transparent
            }
        };
    }

    private void ShowDetailSheet(params (string Label, string Icon, Action Run)[] options)
    {
        if (detailSheetOverlay is null)
        {
            return;
        }

        var card = new Border
        {
            Width = 420,
            MaxWidth = 480,
            Margin = new Thickness(24),
            Padding = new Thickness(26),
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var list = new StackPanel();
        var optionsRow = new Grid();
        optionsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        optionsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < options.Length; index++)
        {
            var (label, icon, run) = options[index];
            var button = new Button
            {
                Style = (Style)FindResource("ToolButton"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 15,
                Padding = new Thickness(8, 18, 8, 18),
                Margin = new Thickness(index == 0 ? 0 : 5, 0, index == options.Length - 1 ? 0 : 5, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var iconBox = CreateSheetIcon(icon);
            iconBox.Margin = new Thickness(0, 0, 0, 10);
            cell.Children.Add(iconBox);
            cell.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
            button.Content = cell;
            button.Click += (_, _) =>
            {
                CloseDetailSheet();
                run();
            };
            Grid.SetColumn(button, index);
            optionsRow.Children.Add(button);
        }
        list.Children.Add(optionsRow);
        var cancelButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = "Отмена",
            FontSize = 14,
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        cancelButton.Click += (_, _) => CloseDetailSheet();
        list.Children.Add(cancelButton);
        card.Child = list;
        detailSheetOverlay.Children.Clear();
        detailSheetOverlay.Children.Add(card);
        detailSheetOverlay.Visibility = Visibility.Visible;
    }

    private void CloseDetailSheet()
    {
        if (detailSheetOverlay is not null)
        {
            detailSheetOverlay.Visibility = Visibility.Collapsed;
            detailSheetOverlay.Children.Clear();
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
