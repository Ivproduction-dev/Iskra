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

    private static readonly ObjectDetailTab[] DetailTabOrder =
    [
        ObjectDetailTab.Scripts,
        ObjectDetailTab.Images,
        ObjectDetailTab.Sounds
    ];

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
        WorkspaceBackButton.Content = CreateIconContent("arrowleft", scene.Name);
        WorkspaceMenuButton.Visibility = Visibility.Collapsed;
        WorkspaceConfirmSelectionButton.Visibility = Visibility.Collapsed;
        WorkspaceCancelSelectionButton.Visibility = Visibility.Collapsed;
        CreateProjectButton.Visibility = detailTab == ObjectDetailTab.Scripts ? Visibility.Collapsed : Visibility.Visible;
        RunProjectButton.Visibility = Visibility.Visible;
        UpdateFloatingCreateButton();
        RefreshProjectDiagnostics();
        BuildObjectDetail();
    }

    private void StopDetailMedia()
    {
        var scriptSaved = FlushScriptSave();
        detailScriptEditor = null;
        detailScriptStatus = null;
        detailScriptDirty = false;
        StopSoundPreview();
        StopRecording();
        if (!scriptSaved)
        {
            ShowRuntimeToast("Не удалось сохранить скрипт объекта");
        }
    }

    private void AddToCurrentDetail()
    {
        if (detailScene is null || detailItem is null)
        {
            return;
        }

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
            return;
        }

        if (detailTab == ObjectDetailTab.Sounds)
        {
            ShowDetailSheet(("Выбрать из файла", "file", ImportSounds), ("Записать с микрофона", "mic", StartRecording));
        }
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
        AddDetailTab(tabGrid, "Скрипты", "code", ObjectDetailTab.Scripts, 0);
        AddDetailTab(tabGrid, "Образы", "eye", ObjectDetailTab.Images, 1);
        AddDetailTab(tabGrid, "Звуки", "sound", ObjectDetailTab.Sounds, 2);
        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 6)
        };
        for (var dotIndex = 0; dotIndex < DetailTabOrder.Length; dotIndex++)
        {
            dots.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 6,
                Height = 6,
                Margin = new Thickness(3, 0, 3, 0),
                Fill = BrushFor(DetailTabOrder[dotIndex] == detailTab ? "AccentBrush" : "HairlineBrush")
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
        CreateProjectButton.Visibility = detailTab == ObjectDetailTab.Scripts ? Visibility.Collapsed : Visibility.Visible;
        UpdateFloatingCreateButton();
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
        var index = Array.IndexOf(DetailTabOrder, detailTab) + direction;
        if (index < 0 || index >= DetailTabOrder.Length)
        {
            return;
        }
        SwitchDetailTab(DetailTabOrder[index], direction);
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
        cellContent.Children.Add(CreateSheetIcon(icon, 26));
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
              <Color name="String" foreground="#2F7947" />
              <Color name="Comment" foreground="#6C6A64" fontStyle="italic" />
              <Color name="Operator" foreground="#386B88" fontWeight="bold" />
              <Color name="Resource" foreground="#9270A8" />
              <RuleSet>
                <Rule color="Comment">^\s*[/]{2}.*$</Rule>
                <Rule color="String">&quot;([^&quot;]|\\.)*&quot;</Rule>
                <Rule color="Keyword">(?i)\bкак\.только\b</Rule>
                <Rule color="Resource">[\p{L}_][\p{L}\p{N}_-]*(?:\.[\p{L}\p{N}_-]+)+</Rule>
                <Rule color="Keyword">(?i)\b(при|запуске|нажатии|отпускании|наведении|задать|присвоить|изменить|печать|показать|спрятать|скрыть|поместить|подвинуть|воспроизвести|зациклить|остановить|звук|громкость|ждать|проиграть|спросить|каждые|каждый|кадр|клавиша|клавиши|право|лево|верх|вниз|пробел|ввод|отмена|забой|запустить|сцену|продолжить|сменить|ориентацию|горизонтальная|вертикальная|иначеесли|иначеесле|иначеэсли|если|есле|эсли|иначе|конец|то|и|или|не|нет|секунд\w*|минут\w*|час\w*|изображение|прозрачность|размер|яркость|когда|перед|назад|истина|ложь|true|false|свойство|коснулся)\b</Rule>
                <Rule color="Keyword">(?i)\b(вечно|пока)\b</Rule>
                <Rule color="Number">[+-]?(?:\d+(?:\.\d*)?|\.\d+)</Rule>
                <Rule color="Operator">\.\.|=</Rule>
                <Rule color="Keyword">(?i)\b(прервать)\b</Rule>
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
    private static readonly (string Label, string Command, string? Placeholder)[] ScriptCommandSuggestions =
    [
        ("При. запуске", "При. запуске", null),
        ("При. нажатии", "При. нажатии", null),
        ("При. отпускании", "При. отпускании", null),
        ("При. наведении", "При. наведении", null),
        ("При. нажатии клавиши.пробел", "При. нажатии клавиши.пробел", "пробел"),
        ("Каждые.2.секунды", "Каждые.2.секунды", "2"),
        ("Каждый кадр", "Каждый кадр", null),
        ("Когда. условие", "Когда. ", "условие"),
        ("Как.Только условие", "Как.Только ", "условие"),
        ("Задать имя = значение", "Задать имя = ", "имя"),
        ("Присвоить имя = значение", "Присвоить имя = ", "имя"),
        ("Задать изображение.", "Задать изображение. ", null),
        ("Присвоить изображение.", "Присвоить изображение. ", null),
        ("Задать прозрачность. 100", "Задать прозрачность. ", "100"),
        ("Изменить прозрачность. 10", "Изменить прозрачность. ", "10"),
        ("Задать размер. 100", "Задать размер. ", "100"),
        ("Изменить размер. 10", "Изменить размер. ", "10"),
        ("Задать яркость. 100", "Задать яркость. ", "100"),
        ("Изменить яркость. 10", "Изменить яркость. ", "10"),
        ("Изменить имя число", "Изменить имя ", "имя"),
        ("Задать Свойство.х = 0", "Задать Свойство.х = 0", "0"),
        ("Изменить Свойство.х 5", "Изменить Свойство.х 5", "5"),
        ("Подвинуть. 10 -5", "Подвинуть. 10 -5", "10 -5"),
        ("Изменить изображение. перед", "Изменить изображение. перед", null),
        ("Изменить изображение. назад", "Изменить изображение. назад", null),
        ("Изменить изображение. объект.перед", "Изменить изображение. объект.перед", null),
        ("Изменить изображение. объект.назад", "Изменить изображение. объект.назад", null),
        ("Поместить X Y", "Поместить X Y", "X Y"),
        ("Ждать 1 секунду", "Ждать 1 секунду", "1 секунду"),
        ("Если условие то", "Если условие то", "условие"),
        ("Есле условие то", "Есле условие то", "условие"),
        ("Эсли условие то", "Эсли условие то", "условие"),
        ("Иначе если условие то", "Иначе если условие то", "условие"),
        ("Иначеесли условие то", "Иначеесли условие то", "условие"),
        ("Иначеесле условие то", "Иначеесле условие то", "условие"),
        ("Иначеэсли условие то", "Иначеэсли условие то", "условие"),
        ("Пока условие то", "Пока условие то", "условие"),
        ("Вечно", "Вечно", null),
        ("Прервать", "Прервать", null),
        ("Иначе", "Иначе", null),
        ("Конец", "Конец", null),
        ("Печать значение", "Печать ", null),
        ("Спрятать (свой объект)", "Спрятать", null),
        ("Скрыть (свой объект)", "Скрыть", null),
        ("Показать (свой объект)", "Показать", null),
        ("Спрятать.объект", "Спрятать.объект", "объект"),
        ("Скрыть.объект", "Скрыть.объект", "объект"),
        ("Показать.объект", "Показать.объект", "объект"),
        ("Воспроизвести звук.", "Воспроизвести звук. ", null),
        ("Воспроизвести звук ждать.", "Воспроизвести звук ждать. ", null),
        ("Зациклить звук.файл.mp3", "Зациклить звук.файл.mp3", "файл.mp3"),
        ("Остановить звук.файл.mp3", "Остановить звук.файл.mp3", "файл.mp3"),
        ("Задать громкость. 50", "Задать громкость. 50", "50"),
        ("Задать громкость.файл.mp3 50", "Задать громкость.файл.mp3 50", "50"),
        ("Проиграть изображение. 8", "Проиграть изображение. 8", "8"),
        ("Проиграть изображение.объект 8", "Проиграть изображение.объект 8", "объект"),
        ("Остановить изображение.", "Остановить изображение.", null),
        ("Запустить.сцену.название", "Запустить.сцену.название", "название"),
        ("Продолжить.сцену.название", "Продолжить.сцену.название", "название"),
        ("Сменить.ориентацию", "Сменить.ориентацию", null),
        ("Сменить.ориентацию.вертикальная", "Сменить.ориентацию.вертикальная", null),
        ("Сменить.ориентацию.горизонтальная", "Сменить.ориентацию.горизонтальная", null),
        ("Спросить. \"вопрос?\" имя", "Спросить. ", null)
    ];

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
            Foreground = BrushFor("ErrorBrush"),
            Visibility = Visibility.Collapsed
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
        AttachScriptCommandSuggestions(editor);
        content.Children.Add(status);
    }

    private void AttachScriptCommandSuggestions(TextEditor editor)
    {
        var popupRows = new StackPanel();
        var popup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            PlacementTarget = editor.TextArea.TextView,
            StaysOpen = true,
            Focusable = false,
            Child = new Border
            {
                Background = BrushFor("PanelBrush"),
                BorderBrush = BrushFor("HairlineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                Child = popupRows
            }
        };
        var selectedIndex = 0;
        var replacementOffset = 0;
        var replacementLength = 0;
        var visibleSuggestions = Array.Empty<(string Label, string Command, string? Placeholder)>();

        void SelectSuggestion(int index)
        {
            if (visibleSuggestions.Length == 0)
            {
                return;
            }

            selectedIndex = (index + visibleSuggestions.Length) % visibleSuggestions.Length;
            for (var rowIndex = 0; rowIndex < popupRows.Children.Count; rowIndex++)
            {
                if (popupRows.Children[rowIndex] is Border row)
                {
                    row.Background = BrushFor(rowIndex == selectedIndex ? "SelectedBrush" : "PanelBrush");
                }
            }
        }

        void AcceptSuggestion()
        {
            if (!popup.IsOpen || visibleSuggestions.Length == 0)
            {
                return;
            }

            var suggestion = visibleSuggestions[selectedIndex];
            var command = suggestion.Command;
            popup.IsOpen = false;
            editor.Document.Replace(replacementOffset, replacementLength, command);
            if (suggestion.Placeholder is { } placeholder)
            {
                var placeholderIndex = command.IndexOf(placeholder, StringComparison.Ordinal);
                if (placeholderIndex >= 0)
                {
                    editor.Select(replacementOffset + placeholderIndex, placeholder.Length);
                }
                else
                {
                    editor.CaretOffset = replacementOffset + command.Length;
                }
            }
            else
            {
                editor.CaretOffset = replacementOffset + command.Length;
            }
            editor.TextArea.Focus();
        }

        void RefreshSuggestions()
        {
            if (!editor.IsKeyboardFocusWithin)
            {
                popup.IsOpen = false;
                return;
            }

            var caret = editor.TextArea.Caret;
            var line = editor.Document.GetLineByNumber(caret.Line);
            var caretColumn = Math.Clamp(caret.Column - 1, 0, line.Length);
            var textBeforeCaret = editor.Document.GetText(line.Offset, caretColumn);
            var indentationLength = textBeforeCaret.Length - textBeforeCaret.TrimStart().Length;
            var prefix = textBeforeCaret[indentationLength..];
            visibleSuggestions = prefix.Length == 0
                ? Array.Empty<(string Label, string Command, string? Placeholder)>()
                : ScriptCommandSuggestions
                    .Where(suggestion => suggestion.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                        suggestion.Command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            if (visibleSuggestions.Length == 0)
            {
                popup.IsOpen = false;
                popupRows.Children.Clear();
                return;
            }

            replacementOffset = line.Offset + indentationLength;
            replacementLength = prefix.Length;
            selectedIndex = 0;
            popupRows.Children.Clear();
            foreach (var suggestion in visibleSuggestions)
            {
                var row = new Border
                {
                    Padding = new Thickness(10, 7, 10, 7),
                    CornerRadius = new CornerRadius(5),
                    Background = BrushFor("PanelBrush"),
                    Child = new TextBlock
                    {
                        Text = suggestion.Label,
                        FontSize = 13,
                        Foreground = BrushFor("InkBrush")
                    }
                };
                row.MouseEnter += (_, _) => SelectSuggestion(popupRows.Children.IndexOf(row));
                row.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    SelectSuggestion(popupRows.Children.IndexOf(row));
                    AcceptSuggestion();
                };
                popupRows.Children.Add(row);
            }

            SelectSuggestion(0);
            var caretPoint = editor.TextArea.TextView.GetVisualPosition(caret.Position, ICSharpCode.AvalonEdit.Rendering.VisualYPosition.LineBottom);
            popup.HorizontalOffset = caretPoint.X;
            popup.VerticalOffset = caretPoint.Y + 3;
            popup.IsOpen = true;
        }

        editor.TextChanged += (_, _) => RefreshSuggestions();
        editor.TextArea.Caret.PositionChanged += (_, _) => RefreshSuggestions();
        editor.TextArea.PreviewKeyDown += (_, e) =>
        {
            if (!popup.IsOpen)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Down:
                    SelectSuggestion(selectedIndex + 1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    SelectSuggestion(selectedIndex - 1);
                    e.Handled = true;
                    break;
                case Key.Tab:
                    AcceptSuggestion();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    popup.IsOpen = false;
                    e.Handled = true;
                    break;
            }
        };
        editor.LostKeyboardFocus += (_, _) => popup.IsOpen = false;
        editor.Unloaded += (_, _) => popup.IsOpen = false;
    }

    private bool FlushScriptSave()
    {
        detailSaveTimer?.Stop();
        if (!detailScriptDirty || detailScriptEditor is null || activeProject is null || detailScene is null || detailItem is null)
        {
            return true;
        }
        if (projectContentStore.TryWriteObjectScript(activeProject, detailScene, detailItem.Id, detailScriptEditor.Text, out var error))
        {
            detailScriptDirty = false;
            if (detailScriptStatus is not null)
            {
                detailScriptStatus.Text = string.Empty;
                detailScriptStatus.Visibility = Visibility.Collapsed;
            }
            RefreshProjectDiagnostics();
            return true;
        }
        if (detailScriptStatus is not null)
        {
            detailScriptStatus.Text = error ?? "Не удалось сохранить скрипт.";
            detailScriptStatus.Visibility = Visibility.Visible;
        }
        return false;
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

    private void ShowSpriteNameDialog(string title, string subtitle, string buttonLabel, Action<string> next, string initialName = "")
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
            Text = initialName,
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
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            nameBox.Focus();
            nameBox.SelectAll();
        }));
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
        imageBox.Cursor = Cursors.Hand;
        imageBox.MouseLeftButtonUp += (_, _) =>
        {
            if (detailScene is not null && detailItem is not null)
            {
                StartPainting(detailScene, detailItem, path);
            }
        };
        var imageFrame = new Grid { Height = 100 };
        imageFrame.Children.Add(imageBox);
        var actionsButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Content = CreateSheetIcon("dots", 16),
            Background = BrushFor("PanelBrush"),
            Opacity = 0.94,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            ToolTip = "Действия с образом"
        };
        AutomationProperties.SetName(actionsButton, $"Действия с образом {Path.GetFileName(path)}");
        imageFrame.Children.Add(actionsButton);
        layout.Children.Add(imageFrame);
        var nameRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 8, 0, 0) };
        var name = new TextBlock
        {
            Text = (isPreview ? "Превью · " : string.Empty) + Path.GetFileName(path),
            FontSize = 11,
            Foreground = BrushFor(isPreview ? "AccentBrush" : "MutedBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "Дважды щёлкни, чтобы скопировать имя файла"
        };
        name.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2)
            {
                return;
            }

            e.Handled = true;
            CopyImageName(path);
        };
        nameRow.Children.Add(name);
        var actionsPopup = new Popup
        {
            Placement = PlacementMode.Custom,
            PlacementTarget = actionsButton,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.None
        };
        var popupMotion = new TranslateTransform();
        var popupCard = new Border
        {
            Width = 198,
            Padding = new Thickness(6),
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Opacity = 0,
            RenderTransform = popupMotion
        };
        popupCard.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 4,
            Opacity = 0.2
        };
        var popupItems = new StackPanel();
        popupItems.Children.Add(CreateImageActionItem("Скопировать образ", actionsPopup, () => CopyImage(path)));
        popupItems.Children.Add(CreateImageActionItem("Скопировать имя", actionsPopup, () => CopyImageName(path)));
        popupItems.Children.Add(CreateImageActionItem("Переименовать", actionsPopup, () => RenameImage(path)));
        popupItems.Children.Add(new Border
        {
            Height = 1,
            Background = BrushFor("HairlineSoftBrush"),
            Margin = new Thickness(6, 4, 6, 4)
        });
        popupItems.Children.Add(CreateImageActionItem("Удалить", actionsPopup, () => DeleteImage(path), BrushFor("ErrorBrush")));
        popupCard.Child = popupItems;
        actionsPopup.Child = popupCard;

        var opensAbove = false;
        actionsPopup.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        {
            var origin = actionsButton.PointToScreen(new Point(0, 0));
            var transform = PresentationSource.FromVisual(actionsButton)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            var originX = origin.X / transform.M11;
            var originY = origin.Y / transform.M22;
            var workArea = SystemParameters.WorkArea;
            var roomBelow = workArea.Bottom - originY - targetSize.Height;
            var roomAbove = originY - workArea.Top;
            opensAbove = roomBelow < popupSize.Height && roomAbove > roomBelow;
            var x = Math.Clamp(targetSize.Width - popupSize.Width,
                workArea.Left - originX,
                workArea.Right - originX - popupSize.Width);
            var y = opensAbove ? -popupSize.Height : targetSize.Height;
            return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None)];
        };
        actionsPopup.Opened += (_, _) =>
        {
            popupCard.BeginAnimation(UIElement.OpacityProperty, null);
            popupMotion.BeginAnimation(TranslateTransform.YProperty, null);
            popupCard.Opacity = 0;
            var initialOffset = opensAbove ? 8 : -8;
            popupMotion.Y = initialOffset;
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            popupCard.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = easing });
            popupMotion.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(initialOffset, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = easing });
        };
        actionsButton.Click += (_, _) =>
        {
            if (!actionsPopup.IsOpen)
            {
                actionsPopup.IsOpen = true;
            }
        };
        layout.Children.Add(nameRow);
        thumb.Child = layout;
        return thumb;
    }

    private Button CreateImageActionItem(string label, Popup popup, Action action, Brush? foreground = null)
    {
        var button = new Button
        {
            Style = (Style)FindResource("AppMenuItemButton"),
            Content = label,
            Foreground = foreground ?? BrushFor("InkBrush"),
            MinWidth = 184,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        button.Click += (_, _) =>
        {
            popup.IsOpen = false;
            action();
        };
        return button;
    }

    private void CopyImageName(string path)
    {
        try
        {
            Clipboard.SetText(Path.GetFileName(path));
            ShowRuntimeToast("Имя файла скопировано");
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            AppLog.Write("Не удалось скопировать имя файла образа.", exception);
            ShowRuntimeToast("Не удалось скопировать имя файла");
        }
    }

    private void CopyImage(string path) => CopyResourceFile(path, "образ");

    private void CopySound(string path) => CopyResourceFile(path, "звук");

    private void CopyResourceFile(string path, string resourceLabel)
    {
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            var destination = FreeFilePath(directory, SafeFileName(path));
            File.Copy(path, destination);
            BuildObjectDetail();
            ShowRuntimeToast($"{char.ToUpperInvariant(resourceLabel[0]) + resourceLabel[1..]} скопирован");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"Не удалось скопировать {resourceLabel}.", exception);
            ShowRuntimeToast($"Не удалось скопировать {resourceLabel}");
        }
    }

    private void RenameImage(string path)
    {
        if (activeProject is null || detailScene is null || detailItem is null)
        {
            return;
        }

        var extension = Path.GetExtension(path);
        ShowSpriteNameDialog("Переименовать образ", $"Формат {extension} сохранится.", "Переименовать", name =>
        {
            var currentName = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(currentName, name, StringComparison.Ordinal))
            {
                return;
            }

            var destination = Path.Combine(Path.GetDirectoryName(path)!, name + extension);
            if (File.Exists(destination))
            {
                ShowRuntimeToast("Образ с таким именем уже существует");
                return;
            }

            try
            {
                File.Move(path, destination);
                BuildObjectDetail();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Write("Не удалось переименовать образ.", exception);
                ShowRuntimeToast("Не удалось переименовать образ");
            }
        }, Path.GetFileNameWithoutExtension(path));
    }

    private void DeleteImage(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Не удалось удалить образ.", exception);
            ShowRuntimeToast("Не удалось удалить образ");
        }
        BuildObjectDetail();
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
            Content = CreateSheetIcon("play", 16),
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = "Слушать",
            Margin = new Thickness(0, 0, 10, 0)
        };
        playButton.Click += (_, _) => ToggleSoundPreview(path, playButton);
        AutomationProperties.SetName(playButton, $"Слушать {Path.GetFileName(path)}");
        DockPanel.SetDock(playButton, Dock.Left);
        layout.Children.Add(playButton);
        var actionsButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = CreateSheetIcon("dots", 16),
            Padding = new Thickness(8, 4, 8, 4),
            ToolTip = "Действия со звуком"
        };
        AutomationProperties.SetName(actionsButton, $"Действия со звуком {Path.GetFileName(path)}");
        DockPanel.SetDock(actionsButton, Dock.Right);
        layout.Children.Add(actionsButton);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Cursor = Cursors.Hand;
        info.ToolTip = "Дважды щёлкни по имени, чтобы скопировать имя файла";
        info.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2)
            {
                return;
            }

            e.Handled = true;
            CopySoundName(path);
        };
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
        var actionsPopup = new Popup
        {
            Placement = PlacementMode.Custom,
            PlacementTarget = actionsButton,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.None
        };
        var popupMotion = new TranslateTransform();
        var popupCard = new Border
        {
            Width = 198,
            Padding = new Thickness(6),
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Opacity = 0,
            RenderTransform = popupMotion
        };
        popupCard.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 4,
            Opacity = 0.2
        };
        var popupItems = new StackPanel();
        popupItems.Children.Add(CreateImageActionItem("Скопировать звук", actionsPopup, () => CopySound(path)));
        popupItems.Children.Add(CreateImageActionItem("Скопировать имя", actionsPopup, () => CopySoundName(path)));
        popupItems.Children.Add(new Border
        {
            Height = 1,
            Background = BrushFor("HairlineSoftBrush"),
            Margin = new Thickness(6, 4, 6, 4)
        });
        popupItems.Children.Add(CreateImageActionItem("Удалить", actionsPopup, () => DeleteSound(path), BrushFor("ErrorBrush")));
        popupCard.Child = popupItems;
        actionsPopup.Child = popupCard;

        var opensAbove = false;
        actionsPopup.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        {
            var origin = actionsButton.PointToScreen(new Point(0, 0));
            var transform = PresentationSource.FromVisual(actionsButton)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            var originX = origin.X / transform.M11;
            var originY = origin.Y / transform.M22;
            var workArea = SystemParameters.WorkArea;
            var roomBelow = workArea.Bottom - originY - targetSize.Height;
            var roomAbove = originY - workArea.Top;
            opensAbove = roomBelow < popupSize.Height && roomAbove > roomBelow;
            var x = Math.Clamp(targetSize.Width - popupSize.Width,
                workArea.Left - originX,
                workArea.Right - originX - popupSize.Width);
            var y = opensAbove ? -popupSize.Height : targetSize.Height;
            return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None)];
        };
        actionsPopup.Opened += (_, _) =>
        {
            popupCard.BeginAnimation(UIElement.OpacityProperty, null);
            popupMotion.BeginAnimation(TranslateTransform.YProperty, null);
            popupCard.Opacity = 0;
            var initialOffset = opensAbove ? 8 : -8;
            popupMotion.Y = initialOffset;
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            popupCard.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = easing });
            popupMotion.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(initialOffset, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = easing });
        };
        actionsButton.Click += (_, _) =>
        {
            if (!actionsPopup.IsOpen)
            {
                actionsPopup.IsOpen = true;
            }
        };
        ProbeSoundLength(path, meta);

        if (string.Equals(soundPreviewPath, path, StringComparison.OrdinalIgnoreCase))
        {
            soundPreviewButton = playButton;
            playButton.Content = CreateSheetIcon("stop", 14);
        }
        return row;
    }

    private void CopySoundName(string path)
    {
        try
        {
            Clipboard.SetText(Path.GetFileName(path));
            ShowRuntimeToast("Имя файла скопировано");
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            AppLog.Write("Не удалось скопировать имя звукового файла.", exception);
            ShowRuntimeToast("Не удалось скопировать имя файла");
        }
    }

    private void DeleteSound(string path)
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
            ShowRuntimeToast("Не удалось удалить звук");
        }
        BuildObjectDetail();
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
            button.Content = CreateSheetIcon("play", 16);
            return;
        }

        StopSoundPreview();
        try
        {
            soundPreviewPlayer.MediaEnded -= OnSoundPreviewEnded;
            soundPreviewPlayer.MediaEnded += OnSoundPreviewEnded;
            soundPreviewPlayer.MediaFailed -= OnSoundPreviewFailed;
            soundPreviewPlayer.MediaFailed += OnSoundPreviewFailed;
            soundPreviewPlayer.Open(new Uri(path, UriKind.Absolute));
            soundPreviewPlayer.Play();
            soundPreviewPath = path;
            soundPreviewButton = button;
            button.Content = CreateSheetIcon("stop", 14);
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

    private void OnSoundPreviewFailed(object? sender, ExceptionEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            StopSoundPreview();
            ShowRuntimeToast("Не удалось проиграть файл");
        });
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
            soundPreviewButton.Content = CreateSheetIcon("play", 16);
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
                    if (showingDetail)
                    {
                        BuildObjectDetail();
                    }
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NAudio.MmException)
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

    private FrameworkElement CreateSheetIcon(string kind, double size = 34)
    {
        var key = "Icon." + char.ToUpperInvariant(kind[0]) + kind[1..];
        return new Image
        {
            Source = TryFindResource(key) as System.Windows.Media.ImageSource,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform
        };
    }

    private StackPanel CreateIconContent(string kind, string text, double size = 14)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(CreateSheetIcon(kind, size));
        row.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
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
