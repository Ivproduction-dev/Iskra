using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IskraStudio;
using Microsoft.Maui.Layouts;
using RoundRectangle = Microsoft.Maui.Controls.Shapes.RoundRectangle;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Iskra.Android;

public sealed class PaintDroidPage : ContentPage
{
    private const int MaxSide = 1024;
    private const int MaxTextRenderSide = 2048;
    private const double MinZoom = 0.05;
    private const double MaxZoom = 32;

    private readonly ProjectContentStore contentStore;
    private readonly IskraProject project;
    private readonly IskraScene scene;
    private readonly IskraObject item;
    private readonly PaintDocument document;
    private readonly SKBitmap displayBitmap;
    private readonly int width;
    private readonly int height;
    private readonly SKCanvasView canvasView = new() { EnableTouchEvents = true };
    private readonly Grid canvasHost = new();
    private readonly HorizontalStackLayout optionsHost = new() { Spacing = 10, Padding = new Thickness(12, 0) };
    private readonly ScrollView optionsScroll = new()
    {
        Orientation = ScrollOrientation.Horizontal,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
        VerticalScrollBarVisibility = ScrollBarVisibility.Never,
        BackgroundColor = MobileTheme.SurfacePanel,
        HeightRequest = 58
    };
    private readonly HorizontalStackLayout toolHost = new() { Spacing = 6, Padding = new Thickness(10, 0) };
    private readonly Dictionary<PaintTool, Border> toolCells = [];
    private readonly List<long> touchOrder = [];
    private readonly Dictionary<long, SKPoint> touches = [];
    private readonly Random random = new();
    private readonly Label zoomLabel = MobileUi.Text("100%", 11, true, Colors.White);
    private readonly Border statusBorder;
    private readonly Label statusLabel = MobileUi.Text(string.Empty, 12, true, Colors.White);
    private readonly Label titleLabel = MobileUi.Text(string.Empty, 14, true);
    private readonly Border colorSwatch;
    private readonly Button undoButton;
    private readonly Button redoButton;
    private readonly IDispatcherTimer sprayTimer;
    private readonly Grid textOverlay;
    private readonly Editor textEditor;
    private readonly Button textFontButton;
    private readonly Border textColorSwatch;
    private readonly Border outlineColorSwatch;
    private readonly Switch outlineSwitch;
    private readonly Slider outlineWidthSlider;
    private readonly Label outlineWidthValue;
    private readonly SKBitmap checkerBitmap;

    private string? currentPath;
    private string? newFileName;
    private string? loadError;
    private bool loadErrorShown;
    private bool draftChecked;
    private bool dirty;
    private bool skipDraft;
    private bool modalOpen;
    private bool closing;
    private bool isDisappearing;
    private Window? hookedWindow;

    private PaintTool tool = PaintTool.Brush;
    private PaintTool toolBeforePipette = PaintTool.Brush;
    private PaintShape shape = PaintShape.Rectangle;
    private bool shapeFilled;
    private SKColor color = SKColors.Black;
    private double size = 4;
    private double tolerance = 32;
    private double sprayDensity = 30;

    private double zoom = 1;
    private double panX;
    private double panY;
    private bool fitted;
    private bool autoFit = true;
    private SKSizeI lastViewSize;

    private DragMode dragMode;
    private long? activeTouchId;
    private SKPoint panTouchStart;
    private double panStartX;
    private double panStartY;
    private double pinchStartDistance;
    private double pinchStartZoom;
    private SKPoint pinchStartMid;
    private double pinchStartPanX;
    private double pinchStartPanY;
    private PaintPoint lastPaintPoint;
    private PaintPoint? pendingFill;
    private bool dirtyBeforeStroke;
    private bool strokeOpen;
    private SKPoint strokeStartScreen;
    private double strokeTravel;
    private bool saving;

    private bool shapeFrameActive;
    private bool shapeInitialized;
    private bool shapePreviewBaked;
    private bool shapePreviewEdited;
    private double frameX;
    private double frameY;
    private double frameWidth = 160;
    private double frameHeight = 160;
    private double frameRotation;
    private PaintPoint resizeOpposite;
    private double rotateStartAngle;
    private double rotateStartValue;
    private PaintPoint moveOffset;
    private bool shapeCreatedThisGesture;
    private bool shapeGestureActive;
    private bool shapeGestureDirty;
    private bool shapeGestureFrameActive;
    private bool shapeGestureInitialized;
    private bool shapeGesturePreviewBaked;
    private bool shapeGesturePreviewEdited;
    private double shapeGestureFrameX;
    private double shapeGestureFrameY;
    private double shapeGestureFrameWidth;
    private double shapeGestureFrameHeight;
    private double shapeGestureFrameRotation;

    private bool lineActive;
    private PaintPoint lineStart;
    private PaintPoint lineEnd;

    private string paintText = "Искра";
    private string fontFamily = "sans-serif";
    private string fontDisplayName = "Системный";
    private string? customFontFile;
    private double fontSize = 32;
    private double letterSpacing = 100;
    private double weirdness;
    private SKColor textColor = SKColors.Black;
    private SKColor outlineColor = SKColors.White;
    private bool outlineOn;
    private double outlineWidth = 3;
    private double textScaleX = 1;
    private double textScaleY = 1;
    private double textRotation;
    private double textX = 8;
    private double textY = 8;
    private bool textPlaced;
    private bool textPreviewBaked;
    private bool textPreviewEdited;
    private int[]? textCache;
    private int textCacheWidth;
    private int textCacheHeight;
    private SKBitmap? textPreviewBitmap;
    private SKTypeface? textTypeface;
    private bool textTypefaceOwned;
    private string? textTypefaceKey;
    private PaintPoint textResizeOpposite;
    private double textRotateStartAngle;
    private double textRotateStartValue;
    private PaintPoint textMoveOffset;
    private bool textGestureActive;
    private bool textGestureDirty;
    private bool textGesturePlaced;
    private bool textGesturePreviewBaked;
    private bool textGesturePreviewEdited;
    private double textGestureX;
    private double textGestureY;
    private double textGestureScaleX;
    private double textGestureScaleY;
    private double textGestureRotation;

    private readonly record struct DraftInfo(string Target, int Width, int Height, DateTimeOffset SavedAt);

    public PaintDroidPage(
        ProjectStore projectStore, IskraProject project, IskraScene scene, IskraObject item,
        string? path, string? newFileName)
    {
        contentStore = new ProjectContentStore(projectStore);
        this.project = project;
        this.scene = scene;
        this.item = item;
        currentPath = path;
        this.newFileName = newFileName;

        width = MaxSide;
        height = MaxSide;
        int[]? initialPixels = null;
        if (path is not null)
        {
            if (TryLoadImage(path, MaxSide, out var loadedWidth, out var loadedHeight, out var pixels, out var error))
            {
                width = loadedWidth;
                height = loadedHeight;
                initialPixels = pixels;
            }
            else
            {
                loadError = error;
            }
        }

        document = new PaintDocument(width, height);
        if (initialPixels is not null)
        {
            Array.Copy(initialPixels, document.Pixels, document.Pixels.Length);
        }

        displayBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        checkerBitmap = CreateCheckerBitmap();
        SyncBitmapNoInvalidate();

        BackgroundColor = MobileTheme.SurfacePrimary;
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        NavigationPage.SetHasNavigationBar(this, false);

        titleLabel.LineBreakMode = LineBreakMode.TailTruncation;
        titleLabel.MaxLines = 1;

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(48), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(44), new ColumnDefinition(44), new ColumnDefinition(48)),
            Padding = new Thickness(12, 6, 8, 6),
            HeightRequest = 56,
            BackgroundColor = MobileTheme.SurfacePrimary
        };
        header.Add(TopAction("←", "Назад", () => _ = RequestCloseAsync()), 0, 0);
        var titleStack = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(4, 0) };
        titleStack.Add(MobileUi.Text("Рисование", 10, true, MobileTheme.TextMuted));
        titleStack.Add(titleLabel);
        header.Add(titleStack, 1, 0);

        undoButton = TopAction("↶", "Отменить", Undo);
        redoButton = TopAction("↷", "Вернуть", Redo);
        header.Add(undoButton, 2, 0);
        header.Add(redoButton, 3, 0);
        var saveButton = new Button
        {
            Text = "✓",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = MobileTheme.Accent,
            CornerRadius = 10,
            WidthRequest = 42,
            HeightRequest = 40,
            Padding = 0,
            VerticalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetDescription(saveButton, "Сохранить рисунок");
        saveButton.Clicked += async (_, _) => await SaveAsync(closeAfterSave: true);
        header.Add(saveButton, 4, 0);

        optionsScroll.Content = optionsHost;

        var toolsScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            BackgroundColor = MobileTheme.SurfacePanel,
            HeightRequest = 74,
            Content = toolHost
        };

        colorSwatch = new Border
        {
            WidthRequest = 28,
            HeightRequest = 28,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Stroke = MobileTheme.Border,
            StrokeThickness = 1,
            BackgroundColor = ToMaui(color)
        };
        var colorCaption = MobileUi.Text("Цвет", 10, false, MobileTheme.TextMuted);
        colorCaption.HorizontalTextAlignment = TextAlignment.Center;
        var colorButton = new Border
        {
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children = { colorSwatch, colorCaption }
            },
            BackgroundColor = Colors.Transparent,
            StrokeThickness = 0,
            Padding = 0,
            WidthRequest = 52,
            HeightRequest = 60,
            Margin = new Thickness(0, 0, 4, 0)
        };
        var colorTap = new TapGestureRecognizer();
        colorTap.Tapped += async (_, _) => await PickColorAsync(color);
        colorButton.GestureRecognizers.Add(colorTap);
        SemanticProperties.SetDescription(colorButton, "Выбрать цвет");
        toolHost.Add(colorButton);

        AddToolCell(PaintTool.Brush, "✎", "Кисть");
        AddToolCell(PaintTool.Spray, "✷", "Спрей");
        AddToolCell(PaintTool.Eraser, "⌫", "Ластик");
        AddToolCell(PaintTool.Fill, "◧", "Заливка");
        AddToolCell(PaintTool.Pipette, "⌖", "Пипетка");
        AddToolCell(PaintTool.Shape, "▭", "Фигура");
        AddToolCell(PaintTool.Line, "╱", "Линия");
        AddToolCell(PaintTool.Text, "T", "Текст");
        AddToolCell(PaintTool.Hand, "✋", "Рука");

        statusBorder = new Border
        {
            Content = statusLabel,
            BackgroundColor = Color.FromArgb("#D91A1917"),
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10, 5),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(8),
            IsVisible = false,
            InputTransparent = true
        };
        BuildCanvasHost();

        var root = new Grid
        {
            RowDefinitions = new RowDefinitionCollection(
                new RowDefinition(56), new RowDefinition(58), new RowDefinition(GridLength.Star), new RowDefinition(74))
        };
        root.Add(header, 0, 0);
        root.Add(optionsScroll, 0, 1);
        root.Add(canvasHost, 0, 2);
        root.Add(toolsScroll, 0, 3);

        var textUi = BuildTextOverlay();
        textOverlay = textUi.Overlay;
        textEditor = textUi.Editor;
        textFontButton = textUi.FontButton;
        textColorSwatch = textUi.TextSwatch;
        outlineColorSwatch = textUi.OutlineSwatch;
        outlineSwitch = textUi.OutlineSwitch;
        outlineWidthSlider = textUi.OutlineWidth;
        outlineWidthValue = textUi.OutlineValue;
        root.Add(textOverlay, 0, 0);
        Grid.SetRowSpan(textOverlay, 4);
        Content = root;

        sprayTimer = Dispatcher.CreateTimer();
        sprayTimer.Interval = TimeSpan.FromMilliseconds(35);
        sprayTimer.Tick += (_, _) => SprayTick();

        canvasView.PaintSurface += OnPaintSurface;
        canvasView.Touch += OnTouch;
        SelectTool(PaintTool.Brush, force: true);
        UpdateChrome();
    }

    private static Button TopAction(string glyph, string description, Action action)
    {
        var button = new Button
        {
            Text = glyph,
            FontSize = 20,
            TextColor = MobileTheme.TextBody,
            BackgroundColor = Colors.Transparent,
            Padding = 0,
            WidthRequest = 40,
            HeightRequest = 40,
            VerticalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetDescription(button, description);
        button.Clicked += (_, _) => action();
        return button;
    }

    private void BuildCanvasHost()
    {
        canvasHost.BackgroundColor = Color.FromArgb("#242320");
        canvasHost.Add(canvasView);

        var zoomControls = new VerticalStackLayout
        {
            Spacing = 4,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(8)
        };
        zoomControls.Add(CanvasButton("+", "Приблизить", () => ZoomAtCenter(1.25)));
        zoomControls.Add(CanvasButton("−", "Уменьшить", () => ZoomAtCenter(0.8)));
        zoomControls.Add(CanvasButton("⛶", "Вписать", () =>
        {
            autoFit = true;
            fitted = false;
            canvasView.InvalidateSurface();
        }));
        zoomControls.Add(CanvasButton("1:1", "Масштаб 100%", () => ZoomTo(1)));
        canvasHost.Add(zoomControls);

        var zoomBadge = new Border
        {
            Content = zoomLabel,
            BackgroundColor = Color.FromArgb("#B31A1917"),
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = 7 },
            Padding = new Thickness(8, 4),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(8),
            InputTransparent = true
        };
        canvasHost.Add(zoomBadge);
        canvasHost.Add(statusBorder);
    }

    private static Button CanvasButton(string text, string description, Action action)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#CC1A1917"),
            BorderColor = Color.FromArgb("#66FFFFFF"),
            BorderWidth = 1,
            CornerRadius = 9,
            WidthRequest = 40,
            HeightRequest = 38,
            Padding = 0
        };
        SemanticProperties.SetDescription(button, description);
        button.Clicked += (_, _) => action();
        return button;
    }

    private void AddToolCell(PaintTool value, string glyph, string name)
    {
        var label = MobileUi.Text(glyph, 19, false, MobileTheme.TextPrimary);
        label.HorizontalTextAlignment = TextAlignment.Center;
        var caption = MobileUi.Text(name, 10, false, MobileTheme.TextMuted);
        caption.HorizontalTextAlignment = TextAlignment.Center;
        var content = new VerticalStackLayout
        {
            Spacing = 1,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { label, caption }
        };
        var cell = new Border
        {
            WidthRequest = 55,
            HeightRequest = 60,
            Content = content,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            StrokeThickness = 1,
            Stroke = Colors.Transparent,
            BackgroundColor = Colors.Transparent
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SelectTool(value);
        cell.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(cell, $"Инструмент {name}");
        toolCells[value] = cell;
        toolHost.Add(cell);
    }

    private void SelectTool(PaintTool value, bool force = false)
    {
        if (!force && tool == value)
        {
            return;
        }
        CommitEditedPreview();
        DiscardUntouchedPreview();
        if (value == PaintTool.Pipette && tool != PaintTool.Pipette)
        {
            toolBeforePipette = tool;
        }
        tool = value;
        stopSpray();
        dragMode = DragMode.None;
        lineActive = false;
        if (value == PaintTool.Shape && !shapeInitialized)
        {
            shapeFrameActive = true;
            shapeInitialized = true;
            frameX = width / 2.0;
            frameY = height / 2.0;
            var side = Math.Max(8, Math.Min(width, height) / 2.0);
            frameWidth = Math.Min(side, width);
            frameHeight = Math.Min(side, height);
        }
        if (value == PaintTool.Text)
        {
            EnsureTextCache();
            if (!textPlaced)
            {
                textPlaced = true;
                textX = Math.Min(8, Math.Max(0, width - textCacheWidth));
                textY = Math.Min(8, Math.Max(0, height - textCacheHeight));
            }
        }
        foreach (var pair in toolCells)
        {
            var selected = pair.Key == tool;
            pair.Value.BackgroundColor = selected ? MobileTheme.AccentSoft : Colors.Transparent;
            pair.Value.Stroke = selected ? MobileTheme.Accent : Colors.Transparent;
            pair.Value.StrokeThickness = selected ? 1.5 : 1;
        }
        RebuildOptions();
        UpdateChrome();
        canvasView.InvalidateSurface();
    }

    private void CommitEditedPreview()
    {
        if (tool == PaintTool.Shape && shapeFrameActive && !shapePreviewBaked && shapePreviewEdited)
        {
            StampShape();
        }
        else if (tool == PaintTool.Text && textPlaced && !textPreviewBaked && textPreviewEdited)
        {
            StampText();
        }
    }

    private void DiscardUntouchedPreview()
    {
        if (tool == PaintTool.Shape && shapeFrameActive && !shapePreviewBaked && !shapePreviewEdited)
        {
            shapeFrameActive = false;
            shapeInitialized = false;
        }
        else if (tool == PaintTool.Text && textPlaced && !textPreviewBaked && !textPreviewEdited)
        {
            textPlaced = false;
        }
    }

    private void CommitPreviewForSave()
    {
        if (tool == PaintTool.Shape && shapeFrameActive && !shapePreviewBaked)
        {
            StampShape();
        }
        else if (tool == PaintTool.Text && textPlaced && !textPreviewBaked)
        {
            StampText();
        }
    }

    private void RebuildOptions()
    {
        optionsHost.Clear();
        optionsHost.Add(MobileUi.Text(ToolName(tool), 12, true, MobileTheme.Accent));
        optionsHost.Add(Separator());

        switch (tool)
        {
            case PaintTool.Brush:
                optionsHost.Add(SizeSlider());
                optionsHost.Add(ColorOption());
                break;
            case PaintTool.Spray:
                optionsHost.Add(SizeSlider("Радиус"));
                optionsHost.Add(NumericSlider("Плотность", 5, 100, sprayDensity, value => sprayDensity = value, value => $"{value:0}"));
                optionsHost.Add(ColorOption());
                break;
            case PaintTool.Eraser:
                optionsHost.Add(SizeSlider());
                optionsHost.Add(MobileUi.Text("Стирает до прозрачности", 12, color: MobileTheme.TextMuted));
                break;
            case PaintTool.Fill:
                optionsHost.Add(NumericSlider("Допуск", 0, 200, tolerance, value => tolerance = value, value => $"{value:0}"));
                optionsHost.Add(ColorOption());
                break;
            case PaintTool.Pipette:
                optionsHost.Add(MobileUi.Text("Коснись холста, чтобы взять цвет", 12, color: MobileTheme.TextMuted));
                break;
            case PaintTool.Shape:
                AddShapeButtons();
                optionsHost.Add(Separator());
                var fillSwitch = new Switch
                {
                    IsToggled = shapeFilled,
                    OnColor = MobileTheme.Accent,
                    VerticalOptions = LayoutOptions.Center,
                    MinimumHeightRequest = 40
                };
                fillSwitch.Toggled += (_, e) =>
                {
                    shapeFilled = e.Value;
                    shapePreviewBaked = false;
                    MarkShapePreviewEdited();
                    canvasView.InvalidateSurface();
                };
                optionsHost.Add(fillSwitch);
                optionsHost.Add(MobileUi.Text("Заливка", 12, color: MobileTheme.TextBody));
                optionsHost.Add(NumericSlider("Размер", 1, 64, size, value =>
                {
                    size = value;
                    shapePreviewBaked = false;
                    MarkShapePreviewEdited();
                }, value => $"{value:0} px"));
                optionsHost.Add(OptionButton("Поставить", StampShape, primary: true));
                break;
            case PaintTool.Line:
                optionsHost.Add(SizeSlider());
                optionsHost.Add(MobileUi.Text("Проведи пальцем по холсту", 12, color: MobileTheme.TextMuted));
                optionsHost.Add(ColorOption());
                break;
            case PaintTool.Text:
                optionsHost.Add(OptionButton("Настройки текста…", OpenTextPanel));
                optionsHost.Add(OptionButton("Поставить текст", StampText, primary: true));
                break;
            case PaintTool.Hand:
                optionsHost.Add(MobileUi.Text("Двигай холст одним пальцем, масштаб — двумя", 12, color: MobileTheme.TextMuted));
                optionsHost.Add(OptionButton("Вписать", () =>
                {
                    autoFit = true;
                    fitted = false;
                    canvasView.InvalidateSurface();
                }));
                break;
        }

        optionsHost.Add(Separator());
        optionsHost.Add(OptionButton("Очистить", ClearCanvas));
        _ = optionsScroll.ScrollToAsync(0, 0, false);
    }

    private View SizeSlider(string title = "Размер") =>
        NumericSlider(title, 1, 64, size, value => size = value, value => $"{value:0} px");

    private View ColorOption()
    {
        var swatch = new Border
        {
            WidthRequest = 22,
            HeightRequest = 22,
            StrokeShape = new RoundRectangle { CornerRadius = 11 },
            Stroke = MobileTheme.Border,
            BackgroundColor = ToMaui(color)
        };
        var button = new Border
        {
            Content = new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center,
                Children = { swatch, MobileUi.Text("Цвет", 12, false, MobileTheme.TextBody) }
            },
            BackgroundColor = MobileTheme.SurfacePanel,
            Stroke = MobileTheme.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10, 0),
            HeightRequest = 40,
            VerticalOptions = LayoutOptions.Center
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await PickColorAsync(color);
        button.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(button, "Выбрать цвет");
        return button;
    }

    private static View Separator() => new BoxView
    {
        WidthRequest = 1,
        HeightRequest = 24,
        BackgroundColor = MobileTheme.Border,
        VerticalOptions = LayoutOptions.Center
    };

    private View NumericSlider(
        string title, double minimum, double maximum, double initial, Action<double> set, Func<double, string> format)
    {
        var valueLabel = MobileUi.Text(format(initial), 12, true, MobileTheme.TextPrimary);
        valueLabel.WidthRequest = 46;
        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = initial,
            WidthRequest = 116,
            MinimumHeightRequest = 40,
            MaximumTrackColor = MobileTheme.Border,
            MinimumTrackColor = MobileTheme.Accent,
            ThumbColor = MobileTheme.Accent,
            VerticalOptions = LayoutOptions.Center
        };
        slider.ValueChanged += (_, e) =>
        {
            set(e.NewValue);
            valueLabel.Text = format(e.NewValue);
        };
        return new HorizontalStackLayout
        {
            Spacing = 5,
            VerticalOptions = LayoutOptions.Center,
            Children = { MobileUi.Text(title, 12, false, MobileTheme.TextMuted), slider, valueLabel }
        };
    }

    private static Button OptionButton(string text, Action action, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 13,
            FontAttributes = primary ? FontAttributes.Bold : FontAttributes.None,
            TextColor = primary ? Colors.White : MobileTheme.TextBody,
            BackgroundColor = primary ? MobileTheme.Accent : MobileTheme.SurfacePanel,
            BorderColor = primary ? Colors.Transparent : MobileTheme.Border,
            BorderWidth = primary ? 0 : 1,
            CornerRadius = 8,
            Padding = new Thickness(12, 0),
            HeightRequest = 40,
            MinimumWidthRequest = 44,
            VerticalOptions = LayoutOptions.Center
        };
        button.Clicked += (_, _) => action();
        return button;
    }

    private void AddShapeButtons()
    {
        AddShapeButton(PaintShape.Rectangle, "▭", "Прямоугольник");
        AddShapeButton(PaintShape.Ellipse, "◯", "Круг");
        AddShapeButton(PaintShape.Triangle, "△", "Треугольник");
        AddShapeButton(PaintShape.Heart, "♥", "Сердце");
        AddShapeButton(PaintShape.Star, "★", "Звезда");
    }

    private void AddShapeButton(PaintShape value, string glyph, string name)
    {
        var button = new Button
        {
            Text = glyph,
            FontSize = 18,
            TextColor = shape == value ? Colors.White : MobileTheme.TextBody,
            BackgroundColor = shape == value ? MobileTheme.Accent : MobileTheme.SurfacePanel,
            BorderColor = shape == value ? Colors.Transparent : MobileTheme.Border,
            BorderWidth = shape == value ? 0 : 1,
            CornerRadius = 8,
            WidthRequest = 42,
            HeightRequest = 40,
            Padding = 0
        };
        SemanticProperties.SetDescription(button, name);
        button.Clicked += (_, _) =>
        {
            shape = value;
            shapePreviewBaked = false;
            MarkShapePreviewEdited();
            RebuildOptions();
            canvasView.InvalidateSurface();
        };
        optionsHost.Add(button);
    }

    private void UpdateChrome()
    {
        undoButton.IsEnabled = document.CanUndo;
        redoButton.IsEnabled = document.CanRedo;
        undoButton.Opacity = document.CanUndo ? 1 : 0.35;
        redoButton.Opacity = document.CanRedo ? 1 : 0.35;
        titleLabel.Text = (dirty ? "• " : string.Empty) + OutputName();
        colorSwatch.BackgroundColor = ToMaui(color);
        zoomLabel.Text = $"{zoom * 100:0}% · {ToolName(tool)}";
    }

    private void UpdateZoomLabel()
    {
        zoomLabel.Text = $"{zoom * 100:0}% · {ToolName(tool)}";
    }

    private static string ToolName(PaintTool value) => value switch
    {
        PaintTool.Brush => "Кисть",
        PaintTool.Spray => "Спрей",
        PaintTool.Eraser => "Ластик",
        PaintTool.Fill => "Заливка",
        PaintTool.Pipette => "Пипетка",
        PaintTool.Shape => "Фигура",
        PaintTool.Line => "Линия",
        PaintTool.Text => "Текст",
        PaintTool.Hand => "Рука",
        _ => "Рисование"
    };

    private void MarkDirty()
    {
        if (dirty)
        {
            return;
        }
        dirty = true;
        UpdateChrome();
    }

    private void MarkShapePreviewEdited()
    {
        if (!shapeFrameActive)
        {
            return;
        }
        shapePreviewEdited = true;
        MarkDirty();
    }

    private void MarkTextPreviewEdited()
    {
        if (!textPlaced)
        {
            return;
        }
        textPreviewEdited = true;
        MarkDirty();
    }

    private void PushUndo()
    {
        document.PushUndo();
        UpdateChrome();
    }

    private void Undo()
    {
        stopSpray();
        lineActive = false;
        if (!document.Undo())
        {
            return;
        }
        dirty = true;
        shapePreviewBaked = false;
        textPreviewBaked = false;
        shapePreviewEdited = false;
        textPreviewEdited = false;
        shapeFrameActive = false;
        textPlaced = false;
        SyncBitmap();
        UpdateChrome();
        ShowStatus("Отменено");
    }

    private void Redo()
    {
        stopSpray();
        lineActive = false;
        if (!document.Redo())
        {
            return;
        }
        dirty = true;
        shapePreviewBaked = false;
        textPreviewBaked = false;
        shapePreviewEdited = false;
        textPreviewEdited = false;
        shapeFrameActive = false;
        textPlaced = false;
        SyncBitmap();
        UpdateChrome();
        ShowStatus("Возвращено");
    }

    private void ClearCanvas()
    {
        _ = ConfirmClearCanvasAsync();
    }

    private async Task ConfirmClearCanvasAsync()
    {
        try
        {
            var confirmed = await DisplayAlertAsync("Очистить холст?", "Действие можно отменить.", "Очистить", "Отмена");
            if (!confirmed)
            {
                return;
            }
            PushUndo();
            PaintOperations.Clear(document.Pixels);
            shapePreviewBaked = false;
            textPreviewBaked = false;
            shapePreviewEdited = false;
            textPreviewEdited = false;
            shapeFrameActive = false;
            textPlaced = false;
            MarkDirty();
            SyncBitmap();
            ShowStatus("Холст очищен");
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось очистить холст.", exception);
            await DisplayAlertAsync("Не удалось очистить холст", "Попробуй ещё раз.", "Понятно");
        }
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(0x24, 0x23, 0x20));
        if (width < 1 || height < 1)
        {
            return;
        }

        var view = new SKSizeI(e.Info.Width, e.Info.Height);
        if (!fitted || (autoFit && view != lastViewSize))
        {
            FitToView(view);
            lastViewSize = view;
            fitted = true;
        }

        var destination = new SKRect(
            (float)panX, (float)panY,
            (float)(panX + width * zoom), (float)(panY + height * zoom));
        if (destination.Width < 1 || destination.Height < 1)
        {
            return;
        }

        using (var shader = SKShader.CreateBitmap(checkerBitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat))
        using (var checkerPaint = new SKPaint { Shader = shader })
        {
            canvas.DrawRect(destination, checkerPaint);
        }

        using (var imagePaint = new SKPaint { IsAntialias = true })
        {
            var sampling = new SKSamplingOptions(zoom >= 1 ? SKFilterMode.Nearest : SKFilterMode.Linear);
            canvas.DrawBitmap(displayBitmap, destination, sampling, imagePaint);
        }

        using (var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Math.Max(1, 1.2 * DeviceDisplay.MainDisplayInfo.Density),
            Color = new SKColor(0x80, 0x80, 0x80)
        })
        {
            canvas.DrawRect(destination, borderPaint);
        }

        canvas.Save();
        canvas.ClipRect(destination);
        DrawLivePreview(canvas);
        canvas.Restore();

        DrawFrames(canvas);
        DrawBrushCursor(canvas);
    }

    private void FitToView(SKSizeI viewSize)
    {
        if (viewSize.Width <= 2 || viewSize.Height <= 2)
        {
            return;
        }
        var margin = 18 * DeviceDisplay.MainDisplayInfo.Density;
        var candidate = Math.Min(
            (viewSize.Width - margin * 2) / width,
            (viewSize.Height - margin * 2) / height);
        zoom = Math.Clamp(candidate, MinZoom, MaxZoom);
        panX = (viewSize.Width - width * zoom) / 2;
        panY = (viewSize.Height - height * zoom) / 2;
        UpdateZoomLabel();
    }

    private void ZoomAtCenter(double factor)
    {
        var view = new SKPoint(canvasView.CanvasSize.Width / 2f, canvasView.CanvasSize.Height / 2f);
        ZoomAround(view, zoom * factor);
    }

    private void ZoomAround(SKPoint screenCenter, double targetZoom)
    {
        var image = ScreenToImage(screenCenter);
        zoom = Math.Clamp(targetZoom, MinZoom, MaxZoom);
        panX = screenCenter.X - image.X * zoom;
        panY = screenCenter.Y - image.Y * zoom;
        autoFit = false;
        UpdateZoomLabel();
        canvasView.InvalidateSurface();
    }

    private void ZoomTo(double targetZoom)
    {
        var view = new SKPoint(canvasView.CanvasSize.Width / 2f, canvasView.CanvasSize.Height / 2f);
        ZoomAround(view, targetZoom);
    }

    private PaintPoint ScreenToImage(SKPoint point) =>
        new((point.X - panX) / zoom, (point.Y - panY) / zoom);

    private SKPoint ImageToScreen(double x, double y) =>
        new((float)(x * zoom + panX), (float)(y * zoom + panY));

    private SKPoint ImageToScreen(PaintPoint point) => ImageToScreen(point.X, point.Y);

    private bool InsideCanvas(PaintPoint point) =>
        point.X >= 0 && point.Y >= 0 && point.X < width && point.Y < height;

    private void DrawLivePreview(SKCanvas canvas)
    {
        if (lineActive)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)Math.Max(1, size * zoom),
                StrokeCap = SKStrokeCap.Round,
                Color = color
            };
            var start = ImageToScreen(lineStart);
            var end = ImageToScreen(lineEnd);
            canvas.DrawLine(start, end, paint);
        }

        if (tool == PaintTool.Shape && shapeFrameActive && !shapePreviewBaked)
        {
            DrawShapePreview(canvas);
        }

        if (tool == PaintTool.Text && textPlaced && !textPreviewBaked)
        {
            EnsureTextCache();
            if (textPreviewBitmap is not null && textCacheWidth > 0 && textCacheHeight > 0)
            {
                var effectiveWidth = textCacheWidth * textScaleX;
                var effectiveHeight = textCacheHeight * textScaleY;
                var centerX = textX + effectiveWidth / 2;
                var centerY = textY + effectiveHeight / 2;
                var destination = SKRect.Create(
                    (float)(textX * zoom + panX), (float)(textY * zoom + panY),
                    (float)(effectiveWidth * zoom), (float)(effectiveHeight * zoom));
                canvas.Save();
                canvas.RotateDegrees((float)textRotation, (float)(centerX * zoom + panX), (float)(centerY * zoom + panY));
                using var paint = new SKPaint { IsAntialias = true };
                canvas.DrawBitmap(textPreviewBitmap, destination, new SKSamplingOptions(SKFilterMode.Linear), paint);
                canvas.Restore();
            }
        }
    }

    private void DrawShapePreview(SKCanvas canvas)
    {
        var center = ImageToScreen(frameX, frameY);
        var scaledWidth = (float)(frameWidth * zoom);
        var scaledHeight = (float)(frameHeight * zoom);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = shapeFilled ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
            StrokeWidth = (float)Math.Max(1, size * zoom),
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round,
            Color = color
        };

        if (shape is PaintShape.Rectangle or PaintShape.Ellipse)
        {
            canvas.Save();
            canvas.RotateDegrees((float)frameRotation, center.X, center.Y);
            var rect = new SKRect(
                center.X - scaledWidth / 2, center.Y - scaledHeight / 2,
                center.X + scaledWidth / 2, center.Y + scaledHeight / 2);
            if (shape == PaintShape.Rectangle)
            {
                canvas.DrawRect(rect, paint);
            }
            else
            {
                canvas.DrawOval(rect, paint);
            }
            canvas.Restore();
            return;
        }

        var points = PaintOperations.ShapeWorldPoints(shape, frameX, frameY, Math.Max(2, frameWidth), Math.Max(2, frameHeight), frameRotation);
        using var builder = new SKPathBuilder();
        for (var index = 0; index < points.Length; index++)
        {
            var point = ImageToScreen(points[index]);
            if (index == 0)
            {
                builder.MoveTo(point);
            }
            else
            {
                builder.LineTo(point);
            }
        }
        builder.Close();
        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
    }

    private void DrawFrames(SKCanvas canvas)
    {
        var density = DeviceDisplay.MainDisplayInfo.Density;
        if (tool == PaintTool.Shape && shapeFrameActive)
        {
            var corners = PaintOperations.FrameCorners(frameX, frameY, Math.Max(2, frameWidth), Math.Max(2, frameHeight), frameRotation);
            var screenCorners = corners.Select(ImageToScreen).ToArray();
            DrawFrame(canvas, screenCorners, ShapeRotateHandleScreen(), density, "Фигура");
        }

        if (tool == PaintTool.Text && textPlaced && textCacheWidth > 0)
        {
            var effectiveWidth = Math.Max(1, textCacheWidth * textScaleX);
            var effectiveHeight = Math.Max(1, textCacheHeight * textScaleY);
            var centerX = textX + effectiveWidth / 2;
            var centerY = textY + effectiveHeight / 2;
            var corners = PaintOperations.FrameCorners(centerX, centerY, effectiveWidth, effectiveHeight, textRotation);
            var screenCorners = corners.Select(ImageToScreen).ToArray();
            DrawFrame(canvas, screenCorners, TextRotateHandleScreen(), density, "Текст");
        }
    }

    private void DrawFrame(SKCanvas canvas, SKPoint[] corners, SKPoint rotateHandle, double density, string label)
    {
        using var builder = new SKPathBuilder();
        builder.MoveTo(corners[0]);
        for (var index = 1; index < corners.Length; index++)
        {
            builder.LineTo(corners[index]);
        }
        builder.Close();
        using var path = builder.Detach();

        using var effect = SKPathEffect.CreateDash([(float)(7 * density), (float)(5 * density)], 0);
        using var outline = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)(1.5 * density),
            Color = new SKColor(0xA9, 0x58, 0x3E),
            PathEffect = effect
        };
        canvas.DrawPath(path, outline);

        var topMiddle = new SKPoint((corners[0].X + corners[1].X) / 2, (corners[0].Y + corners[1].Y) / 2);
        using var line = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)(1.5 * density),
            Color = new SKColor(0xA9, 0x58, 0x3E)
        };
        canvas.DrawLine(topMiddle, rotateHandle, line);

        foreach (var corner in corners)
        {
            DrawHandle(canvas, corner, density, filled: true);
        }
        DrawHandle(canvas, rotateHandle, density, filled: false);
    }

    private static void DrawHandle(SKCanvas canvas, SKPoint point, double density, bool filled)
    {
        using var fill = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = filled ? SKColors.White : new SKColor(0xA9, 0x58, 0x3E)
        };
        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)(1.8 * density),
            Color = new SKColor(0xA9, 0x58, 0x3E)
        };
        var radius = (float)(6 * density);
        canvas.DrawCircle(point, radius, fill);
        canvas.DrawCircle(point, radius, stroke);
    }

    private SKPoint ShapeRotateHandleScreen()
    {
        var offset = (float)(42 * DeviceDisplay.MainDisplayInfo.Density / zoom);
        var radians = frameRotation * Math.PI / 180;
        var local = new PaintPoint(0, -frameHeight / 2 - offset);
        return ImageToScreen(
            frameX + local.X * Math.Cos(radians) - local.Y * Math.Sin(radians),
            frameY + local.X * Math.Sin(radians) + local.Y * Math.Cos(radians));
    }

    private SKPoint TextRotateHandleScreen()
    {
        var effectiveHeight = Math.Max(1, textCacheHeight * textScaleY);
        var centerY = textY + effectiveHeight / 2;
        var centerX = textX + Math.Max(1, textCacheWidth * textScaleX) / 2;
        var offset = (float)(42 * DeviceDisplay.MainDisplayInfo.Density / zoom);
        var radians = textRotation * Math.PI / 180;
        var local = new PaintPoint(0, -effectiveHeight / 2 - offset);
        return ImageToScreen(
            centerX + local.X * Math.Cos(radians) - local.Y * Math.Sin(radians),
            centerY + local.X * Math.Sin(radians) + local.Y * Math.Cos(radians));
    }

    private void DrawBrushCursor(SKCanvas canvas)
    {
        if (activeTouchId is null || tool is not (PaintTool.Brush or PaintTool.Eraser or PaintTool.Spray))
        {
            return;
        }
        var screen = ImageToScreen(lastPaintPoint);
        using var outer = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            Color = new SKColor(0xFF, 0xFF, 0xFF, 0xCC)
        };
        using var inner = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = new SKColor(0x00, 0x00, 0x00, 0xAA)
        };
        var radius = (float)Math.Max(2, size * zoom / 2);
        canvas.DrawCircle(screen, radius + 1, outer);
        canvas.DrawCircle(screen, radius, inner);
    }

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        e.Handled = true;
        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                touchOrder.Add(e.Id);
                touches[e.Id] = e.Location;
                if (touches.Count == 1)
                {
                    activeTouchId = e.Id;
                    BeginSingleTouch(e.Location);
                }
                else
                {
                    CancelSingleAction();
                    BeginPinch();
                }
                break;
            case SKTouchAction.Moved:
                touches[e.Id] = e.Location;
                if (touches.Count >= 2)
                {
                    UpdatePinch();
                }
                else if (activeTouchId == e.Id)
                {
                    MoveSingleTouch(e.Location);
                }
                break;
            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                touches.Remove(e.Id);
                touchOrder.Remove(e.Id);
                if (touches.Count == 0)
                {
                    if (activeTouchId == e.Id)
                    {
                        EndSingleTouch(e.Location, e.ActionType == SKTouchAction.Cancelled);
                    }
                    activeTouchId = null;
                }
                else if (touches.Count == 1)
                {
                    var remaining = touchOrder.FirstOrDefault(id => touches.ContainsKey(id), -1);
                    if (remaining >= 0)
                    {
                        activeTouchId = remaining;
                        panTouchStart = touches[remaining];
                        panStartX = panX;
                        panStartY = panY;
                        dragMode = DragMode.Pan;
                    }
                    pinchStartDistance = 0;
                    stopSpray();
                }
                break;
        }
    }

    private void BeginPinch()
    {
        if (touches.Count < 2)
        {
            return;
        }
        var ids = touchOrder.Where(touches.ContainsKey).Take(2).ToArray();
        if (ids.Length < 2)
        {
            return;
        }
        var first = touches[ids[0]];
        var second = touches[ids[1]];
        pinchStartDistance = Math.Max(1, Distance(first, second));
        pinchStartMid = new SKPoint((first.X + second.X) / 2, (first.Y + second.Y) / 2);
        pinchStartZoom = zoom;
        pinchStartPanX = panX;
        pinchStartPanY = panY;
        dragMode = DragMode.Pinch;
        autoFit = false;
    }

    private void UpdatePinch()
    {
        if (dragMode != DragMode.Pinch || pinchStartDistance <= 0 || touches.Count < 2)
        {
            return;
        }
        var ids = touchOrder.Where(touches.ContainsKey).Take(2).ToArray();
        if (ids.Length < 2)
        {
            return;
        }
        var first = touches[ids[0]];
        var second = touches[ids[1]];
        var midpoint = new SKPoint((first.X + second.X) / 2, (first.Y + second.Y) / 2);
        var distance = Math.Max(1, Distance(first, second));
        var factor = distance / pinchStartDistance;
        zoom = Math.Clamp(pinchStartZoom * factor, MinZoom, MaxZoom);
        var imageX = (pinchStartMid.X - pinchStartPanX) / pinchStartZoom;
        var imageY = (pinchStartMid.Y - pinchStartPanY) / pinchStartZoom;
        panX = midpoint.X - imageX * zoom;
        panY = midpoint.Y - imageY * zoom;
        UpdateZoomLabel();
        canvasView.InvalidateSurface();
    }

    private static double Distance(SKPoint a, SKPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void BeginSingleTouch(SKPoint screen)
    {
        var point = ScreenToImage(screen);
        lastPaintPoint = point;
        var density = DeviceDisplay.MainDisplayInfo.Density;

        if (tool == PaintTool.Hand)
        {
            dragMode = DragMode.Pan;
            panTouchStart = screen;
            panStartX = panX;
            panStartY = panY;
            return;
        }

        if (tool == PaintTool.Pipette)
        {
            dragMode = DragMode.Pipette;
            return;
        }

        if (tool == PaintTool.Shape)
        {
            BeginShapeTouch(screen, point, density);
            return;
        }

        if (tool == PaintTool.Text)
        {
            BeginTextTouch(screen, point, density);
            return;
        }

        if (tool == PaintTool.Line)
        {
            lineStart = point;
            lineEnd = point;
            lineActive = true;
            dragMode = DragMode.Line;
            canvasView.InvalidateSurface();
            return;
        }

        if (tool == PaintTool.Fill)
        {
            if (InsideCanvas(point))
            {
                pendingFill = point;
                dragMode = DragMode.Fill;
            }
            return;
        }

        if (tool is PaintTool.Brush or PaintTool.Spray or PaintTool.Eraser)
        {
            if (!InsideCanvas(point))
            {
                return;
            }
            dirtyBeforeStroke = dirty;
            strokeStartScreen = screen;
            strokeTravel = 0;
            PushUndo();
            dragMode = DragMode.Paint;
            strokeOpen = true;
            var x = (int)Math.Round(point.X);
            var y = (int)Math.Round(point.Y);
            if (tool == PaintTool.Spray)
            {
                PaintOperations.SprayAt(document.Pixels, width, height, x, y, size, sprayDensity, ToPixel(color), false, random);
                sprayTimer.Start();
            }
            else
            {
                PaintOperations.Stamp(document.Pixels, width, height, x, y, size, ToPixel(color), tool == PaintTool.Eraser);
            }
            MarkDirty();
            SyncBitmap();
        }
    }

    private void MoveSingleTouch(SKPoint screen)
    {
        if (dragMode == DragMode.Pan)
        {
            panX = panStartX + (screen.X - panTouchStart.X);
            panY = panStartY + (screen.Y - panTouchStart.Y);
            UpdateZoomLabel();
            canvasView.InvalidateSurface();
            return;
        }

        var point = ScreenToImage(screen);
        var previous = lastPaintPoint;
        lastPaintPoint = point;

        switch (dragMode)
        {
            case DragMode.Paint:
                strokeTravel = Math.Max(strokeTravel, Distance(strokeStartScreen, screen));
                if (tool is PaintTool.Brush or PaintTool.Spray or PaintTool.Eraser && InsideCanvas(point))
                {
                    var x = (int)Math.Round(point.X);
                    var y = (int)Math.Round(point.Y);
                    if (tool == PaintTool.Spray)
                    {
                        var targetX = Math.Clamp(x, 0, width - 1);
                        var targetY = Math.Clamp(y, 0, height - 1);
                        var fromX = Math.Clamp((int)Math.Round(previous.X), 0, width - 1);
                        var fromY = Math.Clamp((int)Math.Round(previous.Y), 0, height - 1);
                        var steps = Math.Max(Math.Abs(targetX - fromX), Math.Abs(targetY - fromY));
                        var radius = Math.Max(1.0, size / 2.0);
                        var stride = Math.Max(1, (int)Math.Ceiling(radius * 0.4));
                        var interpolations = Math.Max(1, steps / stride);
                        for (var step = 1; step <= interpolations; step++)
                        {
                            var t = (double)step / interpolations;
                            var ix = (int)Math.Round(fromX + (targetX - fromX) * t);
                            var iy = (int)Math.Round(fromY + (targetY - fromY) * t);
                            var count = Math.Max(2, (int)Math.Round(sprayDensity / Math.Max(1, interpolations)));
                            PaintOperations.SprayAt(
                                document.Pixels, width, height, ix, iy, size, count,
                                ToPixel(color), false, random);
                        }
                    }
                    else
                    {
                        PaintOperations.StrokeTo(document.Pixels, width, height, previous,
                            x, y, size, ToPixel(color), tool == PaintTool.Eraser);
                    }
                    SyncBitmap();
                }
                break;
            case DragMode.Fill:
                if (InsideCanvas(point))
                {
                    pendingFill = point;
                }
                break;
            case DragMode.Pipette:
                break;
            case DragMode.Line:
                lineEnd = point;
                canvasView.InvalidateSurface();
                break;
            case DragMode.ShapeMove:
                shapePreviewBaked = false;
                MarkShapePreviewEdited();
                frameX = point.X + moveOffset.X;
                frameY = point.Y + moveOffset.Y;
                canvasView.InvalidateSurface();
                break;
            case DragMode.ShapeResize:
                shapePreviewBaked = false;
                MarkShapePreviewEdited();
                ResizeShape(point);
                break;
            case DragMode.ShapeRotate:
                shapePreviewBaked = false;
                MarkShapePreviewEdited();
                RotateShape(point);
                break;
            case DragMode.TextMove:
                textPreviewBaked = false;
                MarkTextPreviewEdited();
                textX = point.X + textMoveOffset.X;
                textY = point.Y + textMoveOffset.Y;
                canvasView.InvalidateSurface();
                break;
            case DragMode.TextScale:
                textPreviewBaked = false;
                MarkTextPreviewEdited();
                ResizeText(point);
                break;
            case DragMode.TextRotate:
                textPreviewBaked = false;
                MarkTextPreviewEdited();
                RotateText(point);
                break;
        }
    }

    private void EndSingleTouch(SKPoint screen, bool cancelled)
    {
        var point = ScreenToImage(screen);
        switch (dragMode)
        {
            case DragMode.Paint:
                stopSpray();
                strokeOpen = false;
                if (cancelled && strokeTravel <= 12 * DeviceDisplay.MainDisplayInfo.Density &&
                    tool is (PaintTool.Brush or PaintTool.Spray or PaintTool.Eraser) &&
                    document.CancelUndo())
                {
                    dirty = dirtyBeforeStroke;
                    SyncBitmap();
                    UpdateChrome();
                }
                break;
            case DragMode.Fill:
                if (!cancelled && pendingFill is { } fill && InsideCanvas(fill))
                {
                    var x = (int)fill.X;
                    var y = (int)fill.Y;
                    if (document.Pixels[y * width + x] != ToPixel(color))
                    {
                        PushUndo();
                        PaintOperations.FloodFill(document.Pixels, width, height, x, y, ToPixel(color), tolerance);
                        MarkDirty();
                        SyncBitmap();
                    }
                }
                pendingFill = null;
                break;
            case DragMode.Pipette:
                if (!cancelled)
                {
                    if (InsideCanvas(point))
                    {
                        var index = (int)point.Y * width + (int)point.X;
                        var picked = FromPixel(document.Pixels[index]);
                        color = picked;
                        UpdateChrome();
                        ShowStatus($"Цвет {picked.ToHex()}");
                    }
                    SelectTool(toolBeforePipette);
                }
                break;
            case DragMode.Line:
                if (!cancelled && lineActive)
                {
                    lineEnd = point;
                    var startScreen = ImageToScreen(lineStart);
                    var endScreen = ImageToScreen(lineEnd);
                    if (Distance(startScreen, endScreen) > 2 * DeviceDisplay.MainDisplayInfo.Density)
                    {
                        PushUndo();
                        PaintOperations.DrawShape(
                            document.Pixels, width, height, PaintShape.Line,
                            (int)Math.Round(lineStart.X), (int)Math.Round(lineStart.Y),
                            (int)Math.Round(lineEnd.X), (int)Math.Round(lineEnd.Y),
                            size, ToPixel(color), false);
                        MarkDirty();
                        SyncBitmap();
                    }
                }
                lineActive = false;
                break;
            case DragMode.ShapeResize:
                if (shapeCreatedThisGesture && frameWidth < 8 && frameHeight < 8)
                {
                    frameX = point.X;
                    frameY = point.Y;
                    frameWidth = Math.Min(96, width);
                    frameHeight = Math.Min(96, height);
                }
                shapeCreatedThisGesture = false;
                break;
            case DragMode.ShapeMove:
            case DragMode.ShapeRotate:
            case DragMode.TextMove:
            case DragMode.TextScale:
            case DragMode.TextRotate:
                break;
        }
        shapeGestureActive = false;
        textGestureActive = false;
        dragMode = DragMode.None;
        canvasView.InvalidateSurface();
    }

    private void CancelSingleAction()
    {
        stopSpray();
        lineActive = false;
        var restoredPreview = RestorePreviewGesture();
        if (strokeOpen)
        {
            strokeOpen = false;
            if (strokeTravel <= 12 * DeviceDisplay.MainDisplayInfo.Density &&
                tool is (PaintTool.Brush or PaintTool.Spray or PaintTool.Eraser) &&
                document.CancelUndo())
            {
                dirty = dirtyBeforeStroke;
                SyncBitmap();
                UpdateChrome();
            }
        }
        pendingFill = null;
        strokeTravel = 0;
        dragMode = DragMode.None;
        if (restoredPreview)
        {
            UpdateChrome();
            canvasView.InvalidateSurface();
        }
    }

    private void ResetTouches()
    {
        touches.Clear();
        touchOrder.Clear();
        activeTouchId = null;
        dragMode = DragMode.None;
        strokeOpen = false;
        strokeTravel = 0;
        pendingFill = null;
        lineActive = false;
        shapeGestureActive = false;
        textGestureActive = false;
    }

    private void stopSpray()
    {
        if (sprayTimer?.IsRunning == true)
        {
            sprayTimer.Stop();
        }
    }

    private void SprayTick()
    {
        if (tool != PaintTool.Spray || dragMode != DragMode.Paint || !InsideCanvas(lastPaintPoint))
        {
            stopSpray();
            return;
        }
        PaintOperations.SprayAt(
            document.Pixels, width, height,
            (int)Math.Round(lastPaintPoint.X), (int)Math.Round(lastPaintPoint.Y),
            size, sprayDensity, ToPixel(color), false, random);
        SyncBitmap();
    }

    private void CaptureShapeGesture()
    {
        shapeGestureActive = true;
        shapeGestureDirty = dirty;
        shapeGestureFrameActive = shapeFrameActive;
        shapeGestureInitialized = shapeInitialized;
        shapeGesturePreviewBaked = shapePreviewBaked;
        shapeGesturePreviewEdited = shapePreviewEdited;
        shapeGestureFrameX = frameX;
        shapeGestureFrameY = frameY;
        shapeGestureFrameWidth = frameWidth;
        shapeGestureFrameHeight = frameHeight;
        shapeGestureFrameRotation = frameRotation;
    }

    private void CaptureTextGesture()
    {
        textGestureActive = true;
        textGestureDirty = dirty;
        textGesturePlaced = textPlaced;
        textGesturePreviewBaked = textPreviewBaked;
        textGesturePreviewEdited = textPreviewEdited;
        textGestureX = textX;
        textGestureY = textY;
        textGestureScaleX = textScaleX;
        textGestureScaleY = textScaleY;
        textGestureRotation = textRotation;
    }

    private bool RestorePreviewGesture()
    {
        var restored = shapeGestureActive || textGestureActive;
        var restoreDirty = dirty;
        if (shapeGestureActive)
        {
            shapeFrameActive = shapeGestureFrameActive;
            shapeInitialized = shapeGestureInitialized;
            shapePreviewBaked = shapeGesturePreviewBaked;
            shapePreviewEdited = shapeGesturePreviewEdited;
            frameX = shapeGestureFrameX;
            frameY = shapeGestureFrameY;
            frameWidth = shapeGestureFrameWidth;
            frameHeight = shapeGestureFrameHeight;
            frameRotation = shapeGestureFrameRotation;
            shapeCreatedThisGesture = false;
            shapeGestureActive = false;
            restoreDirty = shapeGestureDirty;
        }
        if (textGestureActive)
        {
            textPlaced = textGesturePlaced;
            textPreviewBaked = textGesturePreviewBaked;
            textPreviewEdited = textGesturePreviewEdited;
            textX = textGestureX;
            textY = textGestureY;
            textScaleX = textGestureScaleX;
            textScaleY = textGestureScaleY;
            textRotation = textGestureRotation;
            textGestureActive = false;
            restoreDirty = textGestureDirty;
        }
        if (restored)
        {
            dirty = restoreDirty;
        }
        return restored;
    }

    private void BeginShapeTouch(SKPoint screen, PaintPoint point, double density)
    {
        CaptureShapeGesture();
        if (shapeFrameActive)
        {
            var rotateHandle = ShapeRotateHandleScreen();
            if (Distance(screen, rotateHandle) <= 28 * density)
            {
                dragMode = DragMode.ShapeRotate;
                rotateStartAngle = Math.Atan2(point.Y - frameY, point.X - frameX) * 180 / Math.PI;
                rotateStartValue = frameRotation;
                return;
            }

            var corners = PaintOperations.FrameCorners(frameX, frameY, Math.Max(2, frameWidth), Math.Max(2, frameHeight), frameRotation);
            for (var index = 0; index < corners.Length; index++)
            {
                if (Distance(screen, ImageToScreen(corners[index])) <= 26 * density)
                {
                    dragMode = DragMode.ShapeResize;
                    resizeOpposite = corners[(index + 2) % 4];
                    shapeCreatedThisGesture = false;
                    return;
                }
            }

            if (PointInFrame(point, frameX, frameY, Math.Max(2, frameWidth), Math.Max(2, frameHeight), frameRotation))
            {
                dragMode = DragMode.ShapeMove;
                moveOffset = new PaintPoint(frameX - point.X, frameY - point.Y);
                return;
            }
        }

        if (!InsideCanvas(point))
        {
            return;
        }
        shapeFrameActive = true;
        shapeInitialized = true;
        shapePreviewBaked = false;
        frameX = point.X;
        frameY = point.Y;
        frameWidth = 4;
        frameHeight = 4;
        frameRotation = 0;
        resizeOpposite = new PaintPoint(point.X - 2, point.Y - 2);
        dragMode = DragMode.ShapeResize;
        shapeCreatedThisGesture = true;
        MarkShapePreviewEdited();
        canvasView.InvalidateSurface();
    }

    private void ResizeShape(PaintPoint pointer)
    {
        var dx = pointer.X - resizeOpposite.X;
        var dy = pointer.Y - resizeOpposite.Y;
        var radians = -frameRotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var localX = dx * cos - dy * sin;
        var localY = dx * sin + dy * cos;
        frameWidth = Math.Max(2, Math.Abs(localX));
        frameHeight = Math.Max(2, Math.Abs(localY));
        var forward = frameRotation * Math.PI / 180;
        frameX = resizeOpposite.X + localX / 2 * Math.Cos(forward) - localY / 2 * Math.Sin(forward);
        frameY = resizeOpposite.Y + localX / 2 * Math.Sin(forward) + localY / 2 * Math.Cos(forward);
        canvasView.InvalidateSurface();
    }

    private void RotateShape(PaintPoint pointer)
    {
        var angle = Math.Atan2(pointer.Y - frameY, pointer.X - frameX) * 180 / Math.PI;
        frameRotation = rotateStartValue + angle - rotateStartAngle;
        canvasView.InvalidateSurface();
    }

    private void BeginTextTouch(SKPoint screen, PaintPoint point, double density)
    {
        CaptureTextGesture();
        EnsureTextCache();
        if (!textPlaced)
        {
            if (!InsideCanvas(point))
            {
                return;
            }
            textPlaced = true;
            textPreviewBaked = false;
            textX = point.X;
            textY = point.Y;
            textMoveOffset = new PaintPoint(0, 0);
            dragMode = DragMode.TextMove;
            MarkTextPreviewEdited();
            return;
        }

        if (Distance(screen, TextRotateHandleScreen()) <= 28 * density)
        {
            var rotateCenter = TextCenter();
            dragMode = DragMode.TextRotate;
            textRotateStartAngle = Math.Atan2(point.Y - rotateCenter.Y, point.X - rotateCenter.X) * 180 / Math.PI;
            textRotateStartValue = textRotation;
            return;
        }

        var effectiveWidth = Math.Max(1, textCacheWidth * textScaleX);
        var effectiveHeight = Math.Max(1, textCacheHeight * textScaleY);
        var center = TextCenter();
        var corners = PaintOperations.FrameCorners(center.X, center.Y, effectiveWidth, effectiveHeight, textRotation);
        for (var index = 0; index < corners.Length; index++)
        {
            if (Distance(screen, ImageToScreen(corners[index])) <= 26 * density)
            {
                dragMode = DragMode.TextScale;
                textResizeOpposite = corners[(index + 2) % 4];
                return;
            }
        }

        if (PointInFrame(point, center.X, center.Y, effectiveWidth, effectiveHeight, textRotation))
        {
            dragMode = DragMode.TextMove;
            textMoveOffset = new PaintPoint(textX - point.X, textY - point.Y);
            return;
        }

        if (!InsideCanvas(point))
        {
            return;
        }
        textX = point.X;
        textY = point.Y;
        textPlaced = true;
        textPreviewBaked = false;
        dragMode = DragMode.TextMove;
        textMoveOffset = new PaintPoint(0, 0);
        MarkTextPreviewEdited();
        canvasView.InvalidateSurface();
    }

    private PaintPoint TextCenter()
    {
        var effectiveWidth = Math.Max(1, textCacheWidth * textScaleX);
        var effectiveHeight = Math.Max(1, textCacheHeight * textScaleY);
        return new PaintPoint(textX + effectiveWidth / 2, textY + effectiveHeight / 2);
    }

    private void ResizeText(PaintPoint pointer)
    {
        if (textCacheWidth < 1 || textCacheHeight < 1)
        {
            return;
        }
        var dx = pointer.X - textResizeOpposite.X;
        var dy = pointer.Y - textResizeOpposite.Y;
        var radians = -textRotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var localX = dx * cos - dy * sin;
        var localY = dx * sin + dy * cos;
        textScaleX = Math.Clamp(Math.Abs(localX) / textCacheWidth, 0.05, 40);
        textScaleY = Math.Clamp(Math.Abs(localY) / textCacheHeight, 0.05, 40);
        var effectiveWidth = textCacheWidth * textScaleX;
        var effectiveHeight = textCacheHeight * textScaleY;
        var forward = textRotation * Math.PI / 180;
        var centerX = textResizeOpposite.X + localX / 2 * Math.Cos(forward) - localY / 2 * Math.Sin(forward);
        var centerY = textResizeOpposite.Y + localX / 2 * Math.Sin(forward) + localY / 2 * Math.Cos(forward);
        textX = centerX - effectiveWidth / 2;
        textY = centerY - effectiveHeight / 2;
        canvasView.InvalidateSurface();
    }

    private void RotateText(PaintPoint pointer)
    {
        var center = TextCenter();
        var angle = Math.Atan2(pointer.Y - center.Y, pointer.X - center.X) * 180 / Math.PI;
        textRotation = textRotateStartValue + angle - textRotateStartAngle;
        canvasView.InvalidateSurface();
    }

    private static bool PointInFrame(PaintPoint point, double centerX, double centerY, double width, double height, double rotation)
    {
        var radians = -rotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var dx = point.X - centerX;
        var dy = point.Y - centerY;
        var localX = dx * cos - dy * sin;
        var localY = dx * sin + dy * cos;
        return Math.Abs(localX) <= width / 2 && Math.Abs(localY) <= height / 2;
    }

    private void StampShape()
    {
        if (!shapeFrameActive || shapePreviewBaked)
        {
            return;
        }
        PushUndo();
        PaintOperations.RasterizeRotatedShape(
            document.Pixels, width, height, shape,
            frameX, frameY, Math.Max(2, frameWidth), Math.Max(2, frameHeight),
            frameRotation, size, ToPixel(color), shapeFilled);
        shapePreviewBaked = true;
        shapePreviewEdited = false;
        MarkDirty();
        SyncBitmap();
        ShowStatus("Фигура поставлена");
    }

    private void StampText()
    {
        if (!textPlaced || textPreviewBaked)
        {
            return;
        }
        EnsureTextCache();
        if (textCache is null)
        {
            return;
        }
        PushUndo();
        PaintOperations.BlitText(
            document.Pixels, width, height,
            textCache, textCacheWidth, textCacheHeight,
            (int)Math.Round(textX), (int)Math.Round(textY),
            textScaleX, textScaleY, textRotation);
        textPreviewBaked = true;
        textPreviewEdited = false;
        MarkDirty();
        SyncBitmap();
        ShowStatus("Текст поставлен");
    }

    private void EnsureTextCache()
    {
        if (textCache is not null)
        {
            return;
        }
        BuildTextCache();
    }

    private void BuildTextCache()
    {
        try
        {
            BuildTextCacheCore();
        }
        catch (Exception exception)
        {
            textCache = null;
            textPreviewBitmap?.Dispose();
            textPreviewBitmap = null;
            textCacheWidth = 1;
            textCacheHeight = 1;
            MobileLog.Write("Не удалось отрисовать текст.", exception);
            ShowStatus("Не удалось отрисовать текст");
        }
    }

    private void BuildTextCacheCore()
    {
        textCache = null;
        textPreviewBitmap?.Dispose();
        textPreviewBitmap = null;
        textCacheWidth = 1;
        textCacheHeight = 1;

        var typeface = ResolveTypeface();
        using var font = new SKFont(typeface, (float)Math.Max(1, fontSize))
        {
            Subpixel = true,
            Edging = SKFontEdging.Antialias
        };
        var metrics = font.Metrics;
        var lines = (paintText ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        var runs = new List<(string Text, double X, double Y)>();
        var lineRandom = new Random(1);
        double y = 0;
        double maxWidth = 0;
        var lineAdvance = Math.Max(fontSize * 1.2, (metrics.Descent - metrics.Ascent) * 1.05);
        var jitterAmount = weirdness / 100.0 * fontSize * 0.35;
        foreach (var line in lines)
        {
            double x = 0;
            foreach (var character in line)
            {
                var text = character.ToString();
                var jitter = (lineRandom.NextDouble() * 2 - 1) * jitterAmount;
                runs.Add((text, x, y + jitter));
                var characterWidth = font.MeasureText(text);
                x += characterWidth * (letterSpacing / 100.0);
            }
            maxWidth = Math.Max(maxWidth, x);
            y += lineAdvance;
        }

        var padding = Math.Max(2, outlineOn ? outlineWidth + 2 : 2);
        var cacheWidth = (int)Math.Min(MaxTextRenderSide, Math.Max(1, Math.Ceiling(maxWidth + padding * 2)));
        var cacheHeight = (int)Math.Min(MaxTextRenderSide, Math.Max(1, Math.Ceiling(y + padding * 2)));
        textCacheWidth = cacheWidth;
        textCacheHeight = cacheHeight;

        using var bitmap = new SKBitmap(new SKImageInfo(cacheWidth, cacheHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var fill = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = textColor
            };
            using var stroke = outlineOn ? new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)Math.Max(0.5, outlineWidth),
                StrokeJoin = SKStrokeJoin.Round,
                Color = outlineColor
            } : null;
            var baseline = (float)(padding - metrics.Ascent);
            foreach (var run in runs)
            {
                var originX = (float)(padding + run.X);
                var originY = (float)(baseline + run.Y);
                if (stroke is not null)
                {
                    canvas.DrawText(run.Text, originX, originY, SKTextAlign.Left, font, stroke);
                }
                canvas.DrawText(run.Text, originX, originY, SKTextAlign.Left, font, fill);
            }
        }

        var pixels = new int[cacheWidth * cacheHeight];
        var colors = bitmap.Pixels;
        for (var index = 0; index < pixels.Length; index++)
        {
            var pixel = colors[index];
            pixels[index] = (pixel.Alpha << 24) | (pixel.Red << 16) | (pixel.Green << 8) | pixel.Blue;
        }
        textCache = pixels;

        var preview = new SKBitmap(new SKImageInfo(cacheWidth, cacheHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        var span = preview.GetPixelSpan();
        MemoryMarshal.AsBytes(pixels.AsSpan()).CopyTo(span);
        textPreviewBitmap = preview;
        canvasView.InvalidateSurface();
    }

    private void InvalidateTextCache()
    {
        textPreviewBaked = false;
        MarkTextPreviewEdited();
        textCache = null;
        textPreviewBitmap?.Dispose();
        textPreviewBitmap = null;
        textCacheWidth = 1;
        textCacheHeight = 1;
        canvasView.InvalidateSurface();
    }

    private SKTypeface ResolveTypeface()
    {
        var key = customFontFile ?? fontFamily;
        if (textTypeface is not null && string.Equals(textTypefaceKey, key, StringComparison.Ordinal))
        {
            return textTypeface;
        }
        if (textTypeface is not null && textTypefaceOwned) textTypeface.Dispose();
        textTypeface = null;
        textTypefaceOwned = false;
        textTypefaceKey = key;
        if (string.IsNullOrEmpty(customFontFile))
        {
            var saved = contentStore.GetObjectSetting(project, scene, item, "Шрифт");
            if (!string.IsNullOrWhiteSpace(saved))
            {
                var directory = contentStore.GetObjectResourceDirectory(project, scene, item.Id, "fonts");
                var candidate = Path.Combine(directory, saved);
                if (File.Exists(candidate)) customFontFile = candidate;
            }
        }
        if (!string.IsNullOrEmpty(customFontFile) && File.Exists(customFontFile))
        {
            try
            {
                textTypeface = SKTypeface.FromFile(customFontFile);
                textTypefaceOwned = textTypeface is not null;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                MobileLog.Write("Не удалось загрузить шрифт рисунка.", exception);
            }
        }
        textTypeface ??= SKTypeface.FromFamilyName(fontFamily);
        textTypefaceOwned = textTypeface is not null && !ReferenceEquals(textTypeface, SKTypeface.Default);
        textTypeface ??= SKTypeface.Default;
        return textTypeface;
    }

    private async Task PickColorAsync(SKColor initial)
    {
        if (modalOpen)
        {
            return;
        }
        modalOpen = true;
        try
        {
            var page = new PaintColorPage(initial, picked =>
            {
                color = picked;
                if (tool == PaintTool.Shape)
                {
                    shapePreviewBaked = false;
                    MarkShapePreviewEdited();
                }
                UpdateChrome();
                RebuildOptions();
                canvasView.InvalidateSurface();
            }, () => modalOpen = false);
            await Navigation.PushModalAsync(new NavigationPage(page));
        }
        catch (Exception exception)
        {
            modalOpen = false;
            MobileLog.Write("Не удалось открыть выбор цвета.", exception);
            await DisplayAlertAsync("Не удалось открыть выбор цвета", "Попробуй ещё раз.", "Понятно");
        }
    }

    private async Task PickTextColorAsync(bool outline)
    {
        if (modalOpen)
        {
            return;
        }
        modalOpen = true;
        try
        {
            var page = new PaintColorPage(outline ? outlineColor : textColor, picked =>
            {
                if (outline)
                {
                    outlineColor = picked;
                    outlineColorSwatch.BackgroundColor = ToMaui(picked);
                }
                else
                {
                    textColor = picked;
                    textColorSwatch.BackgroundColor = ToMaui(picked);
                }
                InvalidateTextCache();
            }, () => modalOpen = false);
            await Navigation.PushModalAsync(new NavigationPage(page));
        }
        catch (Exception exception)
        {
            modalOpen = false;
            MobileLog.Write("Не удалось открыть выбор цвета текста.", exception);
            await DisplayAlertAsync("Не удалось открыть выбор цвета", "Попробуй ещё раз.", "Понятно");
        }
    }

    private async Task ChooseFontAsync()
    {
        var options = new List<string> { "Системный", "С засечками", "Моноширинный" };
        var directory = contentStore.EnsureObjectResourceDirectory(project, scene, item.Id, "fonts");
        var files = new List<string>();
        try
        {
            files.AddRange(Directory.EnumerateFiles(directory)
                .Where(file => Path.GetExtension(file).ToLowerInvariant() is ".ttf" or ".otf" or ".ttc")
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MobileLog.Write("Не удалось прочитать список шрифтов.", exception);
        }
        options.AddRange(files.Select(file => "Шрифт: " + Path.GetFileNameWithoutExtension(file)));
        options.Add("Добавить шрифт…");

        var action = await DisplayActionSheetAsync("Шрифт", "Отмена", null, options.ToArray());
        if (action is null or "Отмена")
        {
            return;
        }
        if (action == "Добавить шрифт…")
        {
            await ImportFontAsync(directory);
            return;
        }
        if (action == "Системный")
        {
            SetFont("sans-serif", "Системный", null);
        }
        else if (action == "С засечками")
        {
            SetFont("serif", "С засечками", null);
        }
        else if (action == "Моноширинный")
        {
            SetFont("monospace", "Моноширинный", null);
        }
        else if (action.StartsWith("Шрифт: ", StringComparison.Ordinal))
        {
            var name = action["Шрифт: ".Length..];
            var file = files.FirstOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.CurrentCultureIgnoreCase));
            if (file is not null)
            {
                SetFont("sans-serif", name, file);
            }
        }
    }

    private async Task ImportFontAsync(string directory)
    {
        FileResult? selected;
        try
        {
            selected = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Выбери шрифт" });
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось открыть выбор шрифта.", exception);
            await DisplayAlertAsync("Не удалось открыть файл", "Попробуй выбрать шрифт ещё раз.", "Понятно");
            return;
        }
        if (selected is null)
        {
            return;
        }
        var extension = Path.GetExtension(selected.FileName).ToLowerInvariant();
        if (extension is not (".ttf" or ".otf" or ".ttc"))
        {
            await DisplayAlertAsync("Неподдерживаемый формат", "Поддерживаются TTF, OTF и TTC.", "Понятно");
            return;
        }
        string? temporary = null;
        try
        {
            var destination = AvailablePath(directory, Path.GetFileName(selected.FileName));
            temporary = destination + ".tmp";
            await using (var source = await selected.OpenReadAsync())
            await using (var target = File.Create(temporary))
            {
                await source.CopyToAsync(target);
            }
            using (var typeface = SKTypeface.FromFile(temporary))
            {
                if (typeface is null)
                {
                    throw new InvalidDataException("Шрифт не удалось прочитать.");
                }
            }
            File.Move(temporary, destination);
            temporary = null;
            SetFont("sans-serif", Path.GetFileNameWithoutExtension(destination), destination);
        }
        catch (Exception exception)
        {
            if (temporary is not null)
            {
                TryDeleteFile(temporary);
            }
            MobileLog.Write("Не удалось добавить шрифт.", exception);
            await DisplayAlertAsync("Не удалось добавить шрифт", "Проверь формат файла и доступное место.", "Понятно");
        }
    }

    private void SetFont(string family, string displayName, string? file)
    {
        fontFamily = family;
        fontDisplayName = displayName;
        customFontFile = file;
        if (textTypeface is not null && textTypefaceOwned) textTypeface.Dispose();
        textTypeface = null;
        textTypefaceOwned = false;
        textTypefaceKey = null;
        textFontButton.Text = displayName;
        _ = contentStore.SetObjectSetting(project, scene, item, "Шрифт", string.IsNullOrEmpty(file) ? string.Empty : Path.GetFileName(file));
        InvalidateTextCache();
    }

    private static string AvailablePath(string directory, string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        var candidate = Path.Combine(directory, safeName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }
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

    private async Task SaveAsync(bool closeAfterSave)
    {
        if (saving)
        {
            return;
        }
        saving = true;
        try
        {
            await SaveCoreAsync(closeAfterSave);
        }
        finally
        {
            saving = false;
        }
    }

    private async Task SaveCoreAsync(bool closeAfterSave)
    {
        string? path = currentPath;
        try
        {
            if (path is null)
            {
                var directory = contentStore.EnsureObjectResourceDirectory(project, scene, item.Id, "sprites");
                path = FreeFilePath(directory, OutputName());
            }
            CommitPreviewForSave();
            if ((tool == PaintTool.Shape && shapeFrameActive && !shapePreviewBaked) ||
                (tool == PaintTool.Text && textPlaced && !textPreviewBaked))
            {
                MobileLog.Write("Не удалось зафиксировать текст или фигуру перед сохранением.");
                await DisplayAlertAsync("Не удалось сохранить рисунок", "Не удалось подготовить текст или фигуру. Попробуй ещё раз.", "Понятно");
                return;
            }
            if (Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp"))
            {
                path = FreeFilePath(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".png");
                currentPath = path;
            }
            SyncBitmapNoInvalidate();
            var bytes = EncodeImage(BuildSavePixels(), path);
            var temporary = path + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, path, overwrite: true);
            }
            catch
            {
                TryDeleteFile(temporary);
                throw;
            }
            dirty = false;
            DeleteDraft();
            currentPath = path;
            newFileName = null;
            UpdateChrome();
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось сохранить рисунок.", exception);
            await DisplayAlertAsync("Не удалось сохранить рисунок", "Проверь доступное место и попробуй ещё раз.", "Понятно");
            return;
        }

        if (closeAfterSave)
        {
            skipDraft = true;
            try
            {
                await Navigation.PopAsync();
            }
            catch (Exception exception)
            {
                skipDraft = false;
                MobileLog.Write("Рисунок сохранён, но вернуться к образам не удалось.", exception);
                await DisplayAlertAsync("Рисунок сохранён", "Не удалось вернуться к образам. Используй кнопку «Назад».", "Понятно");
            }
        }
        else
        {
            ShowStatus("Сохранено");
        }
    }

    private int[] BuildSavePixels()
    {
        if (tool == PaintTool.Shape && shapeFrameActive && !shapePreviewBaked)
        {
            var composite = (int[])document.Pixels.Clone();
            PaintOperations.RasterizeRotatedShape(
                composite, width, height, shape,
                frameX, frameY, Math.Max(2, frameWidth), Math.Max(2, frameHeight),
                frameRotation, size, ToPixel(color), shapeFilled);
            return composite;
        }
        if (tool == PaintTool.Text && textPlaced && !textPreviewBaked)
        {
            EnsureTextCache();
            if (textCache is not null)
            {
                var composite = (int[])document.Pixels.Clone();
                PaintOperations.BlitText(
                    composite, width, height,
                    textCache, textCacheWidth, textCacheHeight,
                    (int)Math.Round(textX), (int)Math.Round(textY),
                    textScaleX, textScaleY, textRotation);
                return composite;
            }
        }
        return document.Pixels;
    }

    private byte[] EncodeImage(int[] pixels, string path)
    {
        var format = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png
        };
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        MemoryMarshal.AsBytes(pixels.AsSpan()).CopyTo(bitmap.GetPixelSpan());
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, format == SKEncodedImageFormat.Png ? 100 : 92);
        return data.ToArray();
    }

    private byte[] EncodePng(int[] pixels)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        MemoryMarshal.AsBytes(pixels.AsSpan()).CopyTo(bitmap.GetPixelSpan());
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
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
            MobileLog.Write("Не удалось удалить временный файл рисунка.", exception);
        }
    }

    private string OutputName()
    {
        if (currentPath is not null)
        {
            return Path.GetFileName(currentPath);
        }
        var name = string.IsNullOrWhiteSpace(newFileName) ? "Рисунок" : Path.GetFileName(newFileName.Trim());
        name = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Рисунок";
        }
        return name + ".png";
    }

    private static string FreeFilePath(string directory, string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        var candidate = Path.Combine(directory, safeName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }
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

    private async Task RequestCloseAsync()
    {
        if (closing || saving)
        {
            return;
        }
        closing = true;
        var discarded = false;
        try
        {
            if (textOverlay.IsVisible)
            {
                CloseTextPanel();
                return;
            }
            if (shapePreviewEdited || textPreviewEdited)
            {
                MarkDirty();
            }
            if (!dirty)
            {
                skipDraft = true;
                await Navigation.PopAsync();
                return;
            }
            var action = await DisplayActionSheetAsync(
                "Сохранить изменения?", "Отмена", "Не сохранять", "Сохранить");
            if (action == "Сохранить")
            {
                await SaveAsync(closeAfterSave: true);
            }
            else if (action == "Не сохранять")
            {
                skipDraft = true;
                discarded = true;
                await Navigation.PopAsync();
                DeleteDraft();
            }
        }
        catch (Exception exception)
        {
            if (discarded)
            {
                skipDraft = false;
            }
            MobileLog.Write("Не удалось закрыть рисунок.", exception);
            await DisplayAlertAsync("Не удалось закрыть рисунок", "Попробуй ещё раз.", "Понятно");
        }
        finally
        {
            closing = false;
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = RequestCloseAsync();
        return true;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        modalOpen = false;
        isDisappearing = false;
        if (hookedWindow is null && Window is not null)
        {
            hookedWindow = Window;
            hookedWindow.Deactivated += OnWindowDeactivated;
        }
        if (loadError is not null && !loadErrorShown)
        {
            loadErrorShown = true;
            _ = ShowLoadErrorAsync();
            return;
        }
        if (!draftChecked)
        {
            draftChecked = true;
            _ = CheckDraftAsync();
        }
    }

    protected override void OnDisappearing()
    {
        if (hookedWindow is not null)
        {
            hookedWindow.Deactivated -= OnWindowDeactivated;
            hookedWindow = null;
        }
        isDisappearing = true;
        statusBorder.IsVisible = false;
        stopSpray();
        ResetTouches();
        if (dirty && !skipDraft)
        {
            SaveDraft();
        }
        base.OnDisappearing();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        stopSpray();
        ResetTouches();
        if (dirty && !skipDraft)
        {
            SaveDraft();
        }
    }

    private async Task ShowLoadErrorAsync()
    {
        await DisplayAlertAsync("Не удалось открыть рисунок", loadError ?? "Файл повреждён или слишком большой.", "Понятно");
        skipDraft = true;
        await Navigation.PopAsync();
    }

    private async Task CheckDraftAsync()
    {
        var info = TryReadDraftInfo();
        if (info is null)
        {
            return;
        }
        if (!string.Equals(info.Value.Target, DraftTarget(), StringComparison.Ordinal) ||
            info.Value.Width != width || info.Value.Height != height)
        {
            DeleteDraft();
            return;
        }
        var draftPath = DraftPath() + ".png";
        if (!File.Exists(draftPath))
        {
            DeleteDraft();
            return;
        }
        try
        {
            if (currentPath is not null && File.Exists(currentPath))
            {
                var targetWrite = File.GetLastWriteTimeUtc(currentPath);
                var draftWrite = File.GetLastWriteTimeUtc(draftPath);
                if (targetWrite >= info.Value.SavedAt.UtcDateTime && targetWrite >= draftWrite)
                {
                    DeleteDraft();
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MobileLog.Write("Не удалось проверить актуальность черновика рисунка.", exception);
        }
        var restore = await DisplayAlertAsync(
            "Есть несохранённый рисунок",
            "Восстановить последние изменения?",
            "Восстановить", "Удалить");
        if (!restore)
        {
            DeleteDraft();
            return;
        }
        try
        {
            if (!TryLoadImage(draftPath, MaxSide, out var loadedWidth, out var loadedHeight, out var pixels, out var error) ||
                loadedWidth != width || loadedHeight != height)
            {
                MobileLog.Write($"Не удалось восстановить черновик рисунка: {error}");
                DeleteDraft();
                await DisplayAlertAsync("Не удалось восстановить черновик", "Файл черновика повреждён.", "Понятно");
                return;
            }
            Array.Copy(pixels, document.Pixels, document.Pixels.Length);
            document.ResetHistory();
            dirty = true;
            SyncBitmap();
            UpdateChrome();
            ShowStatus("Черновик восстановлен");
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось восстановить черновик рисунка.", exception);
            await DisplayAlertAsync("Не удалось восстановить черновик", "Попробуй открыть рисунок ещё раз.", "Понятно");
        }
    }

    private bool TryLoadImage(string path, int maxSide, out int imageWidth, out int imageHeight, out int[] pixels, out string? error)
    {
        imageWidth = 0;
        imageHeight = 0;
        pixels = [];
        error = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
            {
                error = "Файл не является изображением.";
                return false;
            }
            var info = codec.Info;
            if (info.Width < 1 || info.Height < 1 || info.Width > maxSide || info.Height > maxSide)
            {
                error = $"Картинка слишком большая для рисовалки. Максимум {maxSide} × {maxSide}.";
                return false;
            }
            stream.Position = 0;
            using var decoded = SKBitmap.Decode(stream);
            if (decoded is null)
            {
                error = "Не удалось прочитать изображение.";
                return false;
            }
            imageWidth = info.Width;
            imageHeight = info.Height;
            using var converted = new SKBitmap(new SKImageInfo(imageWidth, imageHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(converted))
            {
                canvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint { IsAntialias = false };
                canvas.DrawBitmap(decoded, new SKRect(0, 0, imageWidth, imageHeight), new SKSamplingOptions(SKFilterMode.Nearest), paint);
            }
            var colors = converted.Pixels;
            pixels = new int[imageWidth * imageHeight];
            for (var index = 0; index < pixels.Length; index++)
            {
                var pixel = colors[index];
                pixels[index] = (pixel.Alpha << 24) | (pixel.Red << 16) | (pixel.Green << 8) | pixel.Blue;
            }
            return true;
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось открыть изображение.", exception);
            error = "Не удалось открыть изображение.";
            return false;
        }
    }

    private void SyncBitmap()
    {
        SyncBitmapNoInvalidate();
        canvasView.InvalidateSurface();
    }

    private void SyncBitmapNoInvalidate()
    {
        var span = displayBitmap.GetPixelSpan();
        MemoryMarshal.AsBytes(document.Pixels.AsSpan()).CopyTo(span);
    }

    private static int ToPixel(SKColor value) =>
        (value.Alpha << 24) | (value.Red << 16) | (value.Green << 8) | value.Blue;

    private static SKColor FromPixel(int value) =>
        new((byte)((value >> 16) & 0xFF), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF), (byte)((value >> 24) & 0xFF));

    private static Microsoft.Maui.Graphics.Color ToMaui(SKColor color) =>
        Microsoft.Maui.Graphics.Color.FromRgba(color.Red, color.Green, color.Blue, color.Alpha);

    private static SKBitmap CreateCheckerBitmap()
    {
        var bitmap = new SKBitmap(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0xEB, 0xE7, 0xDF));
        using var paint = new SKPaint { Color = new SKColor(0xC9, 0xC4, 0xBB) };
        canvas.DrawRect(0, 0, 8, 8, paint);
        canvas.DrawRect(8, 8, 8, 8, paint);
        return bitmap;
    }

    private void CloseTextPanel()
    {
        textOverlay.IsVisible = false;
        textEditor.Unfocus();
    }

    private (Grid Overlay, Editor Editor, Button FontButton, Border TextSwatch, Border OutlineSwatch,
        Switch OutlineSwitch, Slider OutlineWidth, Label OutlineValue) BuildTextOverlay()
    {
        var overlay = new Grid { IsVisible = false };
        var backdrop = new BoxView { BackgroundColor = Color.FromArgb("#99000000") };
        var backdropTap = new TapGestureRecognizer();
        backdropTap.Tapped += (_, _) => CloseTextPanel();
        backdrop.GestureRecognizers.Add(backdropTap);
        overlay.Add(backdrop);

        var displayHeight = DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density;
        var panel = new Border
        {
            MaximumHeightRequest = Math.Min(560, Math.Max(320, displayHeight * 0.78)),
            BackgroundColor = MobileTheme.SurfacePanel,
            Stroke = MobileTheme.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18, 18, 0, 0) },
            VerticalOptions = LayoutOptions.End,
            Padding = new Thickness(16, 12, 16, 16)
        };

        var content = new VerticalStackLayout { Spacing = 10 };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(44)) };
        heading.Add(MobileUi.Text("Настройки текста", 18, true), 0, 0);
        heading.Add(MobileUi.IconButton("✕", "Закрыть настройки текста", (_, _) => CloseTextPanel()), 1, 0);
        content.Add(heading);

        content.Add(MobileUi.Text("Текст", 12, true, MobileTheme.TextMuted));
        var editor = new Editor
        {
            Text = paintText,
            FontSize = 16,
            MaxLength = 2048,
            AutoSize = EditorAutoSizeOption.Disabled,
            HeightRequest = 92,
            TextColor = MobileTheme.TextPrimary,
            BackgroundColor = MobileTheme.SurfaceSoft,
            Placeholder = "Введите текст",
            PlaceholderColor = MobileTheme.TextMuted
        };
        editor.TextChanged += (_, e) =>
        {
            paintText = e.NewTextValue ?? string.Empty;
            InvalidateTextCache();
        };
        content.Add(new Border
        {
            Content = editor,
            Padding = new Thickness(8),
            BackgroundColor = MobileTheme.SurfaceSoft,
            Stroke = MobileTheme.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 }
        });

        var fontRow = new Grid { ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(180)) };
        fontRow.Add(MobileUi.Text("Шрифт", 13, color: MobileTheme.TextBody), 0, 0);
        var fontButton = new Button
        {
            Text = fontDisplayName,
            FontSize = 12,
            TextColor = MobileTheme.TextBody,
            BackgroundColor = MobileTheme.SurfaceSoft,
            BorderColor = MobileTheme.Border,
            BorderWidth = 1,
            CornerRadius = 8,
            Padding = new Thickness(8, 0),
            HeightRequest = 40
        };
        fontButton.Clicked += async (_, _) => await ChooseFontAsync();
        fontRow.Add(fontButton, 1, 0);
        content.Add(fontRow);

        content.Add(NumericSlider("Кегль", 8, 256, fontSize, value =>
        {
            fontSize = value;
            InvalidateTextCache();
        }, value => $"{value:0}"));
        content.Add(NumericSlider("Интервал", -100, 200, letterSpacing, value =>
        {
            letterSpacing = value;
            InvalidateTextCache();
        }, value => $"{value:0}%"));
        content.Add(NumericSlider("Странность", 0, 100, weirdness, value =>
        {
            weirdness = value;
            InvalidateTextCache();
        }, value => $"{value:0}"));

        var colors = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlignItems.Center,
            AlignContent = FlexAlignContent.Start
        };
        var textSwatch = new Border
        {
            WidthRequest = 26,
            HeightRequest = 26,
            StrokeShape = new RoundRectangle { CornerRadius = 13 },
            Stroke = MobileTheme.Border,
            StrokeThickness = 1,
            BackgroundColor = ToMaui(textColor)
        };
        var textColorButton = new Border
        {
            Content = new HorizontalStackLayout
            {
                Spacing = 7,
                Children = { textSwatch, MobileUi.Text("Цвет текста", 13, color: MobileTheme.TextBody) }
            },
            BackgroundColor = MobileTheme.SurfaceSoft,
            Stroke = MobileTheme.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Margin = new Thickness(0, 0, 14, 6),
            Padding = new Thickness(10, 0),
            HeightRequest = 42
        };
        var textColorTap = new TapGestureRecognizer();
        textColorTap.Tapped += async (_, _) => await PickTextColorAsync(outline: false);
        textColorButton.GestureRecognizers.Add(textColorTap);
        colors.Add(textColorButton);

        var outlineSwatch = new Border
        {
            WidthRequest = 26,
            HeightRequest = 26,
            StrokeShape = new RoundRectangle { CornerRadius = 13 },
            Stroke = MobileTheme.Border,
            StrokeThickness = 1,
            BackgroundColor = ToMaui(outlineColor),
            Opacity = outlineOn ? 1 : 0.4
        };
        var outlineColorButton = new Border
        {
            Content = new HorizontalStackLayout
            {
                Spacing = 7,
                Children = { outlineSwatch, MobileUi.Text("Обводка", 13, color: MobileTheme.TextBody) }
            },
            BackgroundColor = MobileTheme.SurfaceSoft,
            Stroke = MobileTheme.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(10, 0),
            HeightRequest = 42,
            IsEnabled = outlineOn,
            Opacity = outlineOn ? 1 : 0.5
        };
        var outlineColorTap = new TapGestureRecognizer();
        outlineColorTap.Tapped += async (_, _) =>
        {
            if (outlineOn)
            {
                await PickTextColorAsync(outline: true);
            }
        };
        outlineColorButton.GestureRecognizers.Add(outlineColorTap);
        colors.Add(outlineColorButton);
        content.Add(colors);

        var outlineRow = new Grid { ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)) };
        var outlineSwitch = new Switch
        {
            IsToggled = outlineOn,
            OnColor = MobileTheme.Accent,
            MinimumHeightRequest = 42
        };
        outlineRow.Add(new HorizontalStackLayout
        {
            Spacing = 6,
            Children = { outlineSwitch, MobileUi.Text("Обводка текста", 13, color: MobileTheme.TextBody) }
        }, 0, 0);
        content.Add(outlineRow);

        var outlineValueLabel = MobileUi.Text($"{outlineWidth:0}", 12, true, MobileTheme.TextPrimary);
        outlineValueLabel.WidthRequest = 46;
        var outlineSlider = new Slider
        {
            Minimum = 1,
            Maximum = 24,
            Value = outlineWidth,
            IsEnabled = outlineOn,
            MinimumHeightRequest = 40,
            MaximumTrackColor = MobileTheme.Border,
            MinimumTrackColor = MobileTheme.Accent,
            ThumbColor = MobileTheme.Accent
        };
        var outlineSliderRow = new HorizontalStackLayout
        {
            Spacing = 6,
            Children =
            {
                MobileUi.Text("Толщина", 12, false, MobileTheme.TextMuted),
                outlineSlider,
                outlineValueLabel
            }
        };
        content.Add(outlineSliderRow);

        outlineSwitch.Toggled += (_, e) =>
        {
            outlineOn = e.Value;
            outlineSwatch.Opacity = outlineOn ? 1 : 0.4;
            outlineColorButton.IsEnabled = outlineOn;
            outlineColorButton.Opacity = outlineOn ? 1 : 0.5;
            outlineSlider.IsEnabled = outlineOn;
            InvalidateTextCache();
        };
        outlineSlider.ValueChanged += (_, e) =>
        {
            outlineWidth = e.NewValue;
            outlineValueLabel.Text = $"{outlineWidth:0}";
            if (outlineOn)
            {
                InvalidateTextCache();
            }
        };

        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)),
            ColumnSpacing = 10
        };
        actions.Add(MobileUi.Action("Готово", (_, _) => CloseTextPanel(), secondary: true), 0, 0);
        actions.Add(MobileUi.Action("Поставить текст", (_, _) =>
        {
            StampText();
            CloseTextPanel();
        }), 1, 0);
        content.Add(actions);

        panel.Content = new ScrollView { Content = content };
        overlay.Add(panel);
        return (overlay, editor, fontButton, textSwatch, outlineSwatch, outlineSwitch, outlineSlider, outlineValueLabel);
    }

    private void OpenTextPanel()
    {
        if (!textPlaced)
        {
            EnsureTextCache();
            textPlaced = true;
            textPreviewBaked = false;
            textX = Math.Min(8, Math.Max(0, width - textCacheWidth));
            textY = Math.Min(8, Math.Max(0, height - textCacheHeight));
            canvasView.InvalidateSurface();
        }
        if (textEditor.Text != paintText)
        {
            textEditor.Text = paintText;
        }
        textFontButton.Text = fontDisplayName;
        textColorSwatch.BackgroundColor = ToMaui(textColor);
        outlineColorSwatch.BackgroundColor = ToMaui(outlineColor);
        outlineSwitch.IsToggled = outlineOn;
        outlineWidthSlider.IsEnabled = outlineOn;
        outlineWidthValue.Text = $"{outlineWidth:0}";
        textOverlay.IsVisible = true;
    }

    private DateTimeOffset? lastStatusShown;

    private async void ShowStatus(string message)
    {
        if (isDisappearing)
        {
            return;
        }
        var shown = DateTimeOffset.Now;
        lastStatusShown = shown;
        statusLabel.Text = message;
        statusBorder.IsVisible = true;
        await Task.Delay(1800);
        if (lastStatusShown == shown && !isDisappearing)
        {
            statusBorder.IsVisible = false;
        }
    }

    private string DraftTarget() => currentPath ?? ("new:" + (newFileName ?? string.Empty));

    private string DraftPath()
    {
        var identity = item.Id.ToString("N") + "|" + DraftTarget();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return Path.Combine(Path.Combine(FileSystem.CacheDirectory, "PaintDroidDrafts"), hash);
    }

    private DraftInfo? TryReadDraftInfo()
    {
        try
        {
            var path = DraftPath() + ".json";
            if (!File.Exists(path))
            {
                return null;
            }
            return JsonSerializer.Deserialize<DraftInfo>(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось прочитать черновик рисунка.", exception);
            return null;
        }
    }

    private void SaveDraft()
    {
        var basePath = DraftPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
            SyncBitmapNoInvalidate();
            var bytes = EncodePng(BuildSavePixels());
            var temporary = basePath + ".png.tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, basePath + ".png", overwrite: true);
            var info = new DraftInfo(DraftTarget(), width, height, DateTimeOffset.Now);
            var jsonTemp = basePath + ".json.tmp";
            File.WriteAllText(jsonTemp, JsonSerializer.Serialize(info));
            File.Move(jsonTemp, basePath + ".json", overwrite: true);
        }
        catch (Exception exception)
        {
            TryDeleteFile(basePath + ".png.tmp");
            TryDeleteFile(basePath + ".json.tmp");
            MobileLog.Write("Не удалось сохранить черновик рисунка.", exception);
        }
    }

    private void DeleteDraft()
    {
        try
        {
            var basePath = DraftPath();
            TryDeleteFile(basePath + ".png");
            TryDeleteFile(basePath + ".json");
            TryDeleteFile(basePath + ".png.tmp");
            TryDeleteFile(basePath + ".json.tmp");
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось удалить черновик рисунка.", exception);
        }
    }

    private enum DragMode
    {
        None,
        Paint,
        Pan,
        Pinch,
        Fill,
        Pipette,
        Line,
        ShapeMove,
        ShapeResize,
        ShapeRotate,
        TextMove,
        TextScale,
        TextRotate
    }
}
