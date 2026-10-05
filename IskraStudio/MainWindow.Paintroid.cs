using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IskraStudio;

public partial class MainWindow
{
    private enum PaintTool
    {
        Brush,
        Spray,
        Eraser,
        Fill,
        Pipette,
        Shape,
        Line,
        Text,
        Hand
    }

    private enum PaintShape
    {
        Rectangle,
        Ellipse,
        Line
    }

    private const int MaxPaintSide = 1024;
    private const int MaxPaintUndo = 25;

    private static readonly Color[] PaintPalettePresets =
    [
        Color.FromRgb(0x00, 0x74, 0xCD), Color.FromRgb(0x00, 0xB4, 0xF1),
        Color.FromRgb(0x07, 0x87, 0x07), Color.FromRgb(0x8E, 0xC4, 0x30),
        Color.FromRgb(0x61, 0x29, 0x0E), Color.FromRgb(0xA8, 0x48, 0x18),
        Color.FromRgb(0xDE, 0xAE, 0x66), Color.FromRgb(0xF0, 0xE4, 0xA8),
        Color.FromRgb(0x7D, 0x13, 0x79), Color.FromRgb(0xA7, 0x43, 0xD1),
        Color.FromRgb(0xCA, 0x01, 0x86), Color.FromRgb(0xEB, 0x90, 0xD3),
        Color.FromRgb(0xC5, 0x06, 0x0E), Color.FromRgb(0xEB, 0x46, 0x18),
        Color.FromRgb(0xF9, 0x92, 0x1C), Color.FromRgb(0xF3, 0xD6, 0x05),
        Colors.Black,                   Color.FromRgb(0xA3, 0xA3, 0xA3),
        Colors.White,                   Colors.Transparent
    ];

    private bool showingPainting;
    private IskraScene? paintingScene;
    private IskraObject? paintingItem;
    private string? paintingPath;
    private bool paintingDirty;

    private int paintWidth;
    private int paintHeight;
    private int[] paintPixels = [];
    private WriteableBitmap? paintBitmap;
    private readonly List<int[]> paintUndoStack = [];
    private readonly List<int[]> paintRedoStack = [];

    private PaintTool paintTool = PaintTool.Brush;
    private PaintShape paintShape = PaintShape.Rectangle;
    private bool paintShapeFilled;

    private double shapeFrameX;
    private double shapeFrameY;
    private double shapeFrameWidth = 32;
    private double shapeFrameHeight = 32;
    private double shapeFrameRotation;
    private bool shapeFrameActive;
    private PaintFrameAction paintFrameAction = PaintFrameAction.None;
    private int paintFrameHandle = -1;
    private Point shapeFrameGrabCell;
    private Point shapeFrameOpp;
    private Point shapeFrameCenter0;
    private double shapeFrameGrabWidth;
    private double shapeFrameGrabHeight;
    private double shapeFrameGrabRotation;

    private enum PaintFrameAction
    {
        None,
        Move,
        Resize,
        Rotate
    }
    private Color paintColor = Colors.Black;
    private double paintSize = 4;
    private double paintTolerance = 32;
    private double paintSprayDensity = 30;
    private readonly Random paintRandom = new();

    private string paintText = "Искра";
    private string paintFontName = "Segoe UI";
    private string? paintFontFile;
    private double paintFontSize = 32;
    private double paintSpacing = 100;
    private double paintWeirdness;
    private Color paintTextColor = Colors.Black;
    private Color paintOutlineColor = Colors.White;
    private bool paintOutlineOn;
    private double paintOutlineWidth = 3;
    private bool paintFillApplied;
    private bool paintShapeStamped;
    private int paintTextSeed = 1;
    private double paintTextX = 8;
    private double paintTextY = 8;
    private double paintTextRotation;
    private double paintTextGrabRotation;
    private bool paintTextPlaced;
    private int paintTextCacheWidth;
    private int paintTextCacheHeight;
    private double paintTextScaleX = 1;
    private double paintTextScaleY = 1;
    private double paintTextGrabScaleX = 1;
    private double paintTextGrabScaleY = 1;

    private double PaintTextEffW() => Math.Max(1, paintTextCacheWidth * paintTextScaleX);
    private double PaintTextEffH() => Math.Max(1, paintTextCacheHeight * paintTextScaleY);
    private TextBox? paintTextInputBox;
    private Button? paintFontButton;
    private Button? paintTextSwatch;
    private Button? paintOutlineSwatch;
    private StackPanel? paintOutlineWidthSlider;
    private Button CreatePaintSwatch(string label, Color color, Action<Color> pick)
    {
        var swatch = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Padding = new Thickness(4),
            ToolTip = label,
            VerticalAlignment = VerticalAlignment.Center
        };
        swatch.Content = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(color),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1)
        };
        swatch.Click += (_, _) => ShowPaintColorDialog(color, picked =>
        {
            pick(picked);
        }, showPipette: false);
        AutomationProperties.SetName(swatch, label);
        return swatch;
    }

    private void RefreshPaintTextSwatches()
    {
        if (paintTextSwatch is not null)
        {
            paintTextSwatch.Content = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(paintTextColor),
                BorderBrush = BrushFor("HairlineBrush"),
                BorderThickness = new Thickness(1)
            };
        }
        if (paintOutlineSwatch is not null)
        {
            paintOutlineSwatch.Content = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(paintOutlineColor),
                BorderBrush = BrushFor("HairlineBrush"),
                BorderThickness = new Thickness(1)
            };
        }
    }

    private void RefreshPaintOutlineControls()
    {
        var enabled = paintOutlineOn;
        if (paintOutlineWidthSlider is not null)
        {
            paintOutlineWidthSlider.IsEnabled = enabled;
            paintOutlineWidthSlider.Opacity = enabled ? 1 : 0.4;
        }
        if (paintOutlineSwatch is not null)
        {
            paintOutlineSwatch.IsEnabled = enabled;
            paintOutlineSwatch.Opacity = enabled ? 1 : 0.4;
        }
    }

    private void RefreshPaintFontButton()
    {
        if (paintFontButton is not null)
        {
            paintFontButton.Content = new TextBlock
            {
                Text = string.IsNullOrEmpty(paintFontFile) ? paintFontName : Path.GetFileNameWithoutExtension(paintFontFile),
                FontSize = 12,
                Foreground = BrushFor("BodyBrush")
            };
        }
    }

    private void ShowPaintFontDialog()
    {
        var overlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0))
        };
        Panel.SetZIndex(overlay, 40);
        var card = new Border
        {
            Width = 420,
            MaxWidth = 460,
            MaxHeight = 560,
            Margin = new Thickness(24),
            Padding = new Thickness(22),
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
            Text = "Шрифт",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFor("InkBrush")
        });
        var addButton = new Button
        {
            Style = (Style)FindResource("SecondaryButton"),
            Content = "Добавить шрифт…",
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0)
        };
        layout.Children.Add(addButton);
        var listScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 380,
            Margin = new Thickness(0, 12, 0, 0)
        };
        ApplyThinScrollBars(listScroll);
        var list = new StackPanel();
        listScroll.Content = list;
        layout.Children.Add(listScroll);
        var closeButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = "Закрыть",
            Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        layout.Children.Add(closeButton);
        card.Child = layout;
        overlay.Children.Add(card);
        overlay.MouseDown += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, overlay))
            {
                WorkspaceContentHost.Children.Remove(overlay);
            }
        };
        closeButton.Click += (_, _) => WorkspaceContentHost.Children.Remove(overlay);
        addButton.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Шрифты|*.ttf;*.otf",
                Title = "Выбери файл шрифта"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }
            var directory = PaintFontsDirectory();
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(directory);
                var destination = FreeFilePath(directory, SafeFileName(dialog.FileName));
                File.Copy(dialog.FileName, destination);
                RebuildPaintFontList(list, overlay);
                var family = GetCustomFontFamilyName(destination);
                if (family is not null)
                {
                    paintFontFile = destination;
                    paintFontName = family;
                    paintTextSeed = paintRandom.Next();
                    RefreshPaintFontButton();
                    InvalidatePaintText();
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Write("Не удалось добавить шрифт.", exception);
                MessageBox.Show(this, "Не удалось добавить шрифт.", "Искра Студио", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        RebuildPaintFontList(list, overlay);
        WorkspaceContentHost.Children.Add(overlay);
    }

    private void RebuildPaintFontList(StackPanel list, Grid overlay)
    {
        list.Children.Clear();
        AddPaintFontCard(list, overlay, "Segoe UI", null);
        AddPaintFontCard(list, overlay, "Georgia", null);
        var directory = PaintFontsDirectory();
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension is not (".ttf" or ".otf"))
                {
                    continue;
                }
                var family = GetCustomFontFamilyName(file);
                AddPaintFontCard(list, overlay, family ?? Path.GetFileNameWithoutExtension(file), file, family is not null);
            }
        }
    }

    private void AddPaintFontCard(StackPanel list, Grid overlay, string displayName, string? file, bool custom = false)
    {
        if (!custom)
        {
            custom = !string.IsNullOrEmpty(file);
        }
        var selected = custom
            ? string.Equals(paintFontFile, file, StringComparison.OrdinalIgnoreCase)
            : string.IsNullOrEmpty(paintFontFile) && string.Equals(paintFontName, displayName, StringComparison.OrdinalIgnoreCase);
        var card = new Border
        {
            Background = BrushFor(selected ? "SelectedBrush" : "PanelBrush"),
            BorderBrush = BrushFor(selected ? "AccentBrush" : "HairlineBrush"),
            BorderThickness = new Thickness(selected ? 1.5 : 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = Cursors.Hand
        };
        var layout = new DockPanel { LastChildFill = true };
        FontFamily previewFamily;
        try
        {
            previewFamily = custom && file is not null && File.Exists(file)
                ? ResolvePaintFontFamilyForFile(file)
                : new FontFamily(displayName);
        }
        catch
        {
            previewFamily = new FontFamily("Segoe UI");
        }
        layout.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "АаБбЫы 123", FontFamily = previewFamily, FontSize = 20, Foreground = BrushFor("InkBrush") },
                new TextBlock { Text = displayName, FontSize = 12, Foreground = BrushFor("MutedBrush"), Margin = new Thickness(0, 2, 0, 0) }
            }
        });
        if (custom && file is not null)
        {
            var captured = file;
            var capturedName = displayName;
            var menuButton = new Button
            {
                Style = (Style)FindResource("ToolButton"),
                Content = "⋮",
                FontSize = 18,
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Действия со шрифтом"
            };
            menuButton.Click += (_, _) => ShowPaintFontDeleteConfirm(overlay, captured, capturedName);
            DockPanel.SetDock(menuButton, Dock.Right);
            layout.Children.Add(menuButton);
        }
        card.Child = layout;
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (custom && file is not null)
            {
                paintFontFile = file;
                paintFontName = displayName;
            }
            else
            {
                paintFontFile = null;
                paintFontName = displayName;
            }
            paintTextSeed = paintRandom.Next();
            RefreshPaintFontButton();
            InvalidatePaintText();
            WorkspaceContentHost.Children.Remove(overlay);
        };
        list.Children.Add(card);
    }

    private FontFamily ResolvePaintFontFamilyForFile(string file)
    {
        var familyName = GetCustomFontFamilyName(file) ?? throw new InvalidDataException("Не шрифт.");
        var directory = Path.GetDirectoryName(file)!.Replace('\\', '/');
        return new FontFamily(new Uri("file:///" + directory + "/"), "./#" + familyName);
    }

    private void ShowPaintFontDeleteConfirm(Grid fontOverlay, string file, string displayName)
    {
        var overlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0))
        };
        Panel.SetZIndex(overlay, 50);
        var card = new Border
        {
            Width = 380,
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
            Text = "Удалить шрифт?",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFor("InkBrush")
        });
        layout.Children.Add(new TextBlock
        {
            Text = $"«{displayName}» будет удалён из проекта.",
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 13,
            Foreground = BrushFor("BodyBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancelButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = "Отмена",
            Padding = new Thickness(14, 8, 14, 8)
        };
        var deleteButton = new Button
        {
            Style = (Style)FindResource("PrimaryButton"),
            Content = "Удалить",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(18, 8, 18, 8)
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(deleteButton);
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
        deleteButton.Click += (_, _) =>
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Write("Не удалось удалить шрифт.", exception);
            }
            if (string.Equals(paintFontFile, file, StringComparison.OrdinalIgnoreCase))
            {
                paintFontFile = null;
                paintFontName = "Segoe UI";
                paintTextSeed = paintRandom.Next();
                RefreshPaintFontButton();
                InvalidatePaintText();
            }
            WorkspaceContentHost.Children.Remove(overlay);
            WorkspaceContentHost.Children.Remove(fontOverlay);
            ShowPaintFontDialog();
        };
        WorkspaceContentHost.Children.Add(overlay);
    }

    private string PaintFontsDirectory()
    {
        if (activeProject is null || paintingScene is null || paintingItem is null)
        {
            return string.Empty;
        }
        return projectContentStore.GetObjectResourceDirectory(activeProject, paintingScene, paintingItem.Id, "fonts");
    }

    private static string? GetCustomFontFamilyName(string path)
    {
        try
        {
            var glyph = new GlyphTypeface(new Uri(path, UriKind.Absolute));
            if (glyph.FamilyNames.TryGetValue(new System.Globalization.CultureInfo("ru-RU"), out var ru))
            {
                return ru;
            }
            if (glyph.FamilyNames.TryGetValue(new System.Globalization.CultureInfo("en-US"), out var en))
            {
                return en;
            }
            foreach (var name in glyph.FamilyNames.Values)
            {
                return name;
            }
        }
        catch
        {
        }
        return null;
    }

    private FontFamily ResolvePaintFontFamily()
    {
        if (!string.IsNullOrEmpty(paintFontFile) && File.Exists(paintFontFile))
        {
            try
            {
                var familyName = GetCustomFontFamilyName(paintFontFile);
                if (familyName is not null)
                {
                    var directory = Path.GetDirectoryName(paintFontFile)!.Replace('\\', '/');
                    return new FontFamily(new Uri("file:///" + directory + "/"), "./#" + familyName);
                }
            }
            catch
            {
            }
        }
        try
        {
            return new FontFamily(paintFontName);
        }
        catch
        {
            return new FontFamily("Segoe UI");
        }
    }

    private (int[] Pixels, int Width, int Height) RenderPaintText()
    {
        var typeface = new Typeface(ResolvePaintFontFamily(), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var culture = System.Globalization.CultureInfo.GetCultureInfo("ru-ru");
        var fill = new SolidColorBrush(paintTextColor);
        fill.Freeze();
        Pen? pen = null;
        if (paintOutlineOn)
        {
            pen = new Pen(new SolidColorBrush(paintOutlineColor), paintOutlineWidth);
            pen.LineJoin = PenLineJoin.Round;
            pen.Freeze();
        }
        var lines = (paintText ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        var runs = new List<(FormattedText Text, double X, double Y)>();
        var random = new Random(paintTextSeed);
        double y = 0;
        double maxWidth = 0;
        double minX = 0;
        foreach (var line in lines)
        {
            double x = 0;
            double lineHeight = paintFontSize * 1.2;
            foreach (var ch in line)
            {
                FormattedText formatted;
                try
                {
                    formatted = new FormattedText(ch.ToString(), culture, FlowDirection.LeftToRight, typeface, paintFontSize, fill, 1);
                }
                catch
                {
                    continue;
                }
                var jitter = (random.NextDouble() * 2 - 1) * (paintWeirdness / 100.0) * (paintFontSize * 0.35);
                runs.Add((formatted, x, y + jitter));
                minX = Math.Min(minX, x);
                x += formatted.WidthIncludingTrailingWhitespace * (paintSpacing / 100.0);
                lineHeight = Math.Max(lineHeight, formatted.Height);
            }
            maxWidth = Math.Max(maxWidth, x);
            y += lineHeight;
        }
        var pad = paintOutlineWidth + 2;
        var width = Math.Max(1, (int)Math.Ceiling(maxWidth - minX + pad * 2));
        var height = Math.Max(1, (int)Math.Ceiling(y + pad * 2));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            foreach (var (formatted, rx, ry) in runs)
            {
                var origin = new Point(rx - minX + pad, ry + pad);
                if (pen is not null)
                {
                    try
                    {
                        context.DrawGeometry(null, pen, formatted.BuildGeometry(origin));
                    }
                    catch
                    {
                    }
                }
                context.DrawText(formatted, origin);
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        var pixels = new int[width * height];
        bitmap.CopyPixels(pixels, width * 4, 0);
        for (var i = 0; i < pixels.Length; i++)
        {
            var pixel = pixels[i];
            var alpha = (pixel >> 24) & 0xFF;
            if (alpha == 0 || alpha == 255)
            {
                continue;
            }
            var r = (((pixel >> 16) & 0xFF) * 255 + alpha / 2) / alpha;
            var g = (((pixel >> 8) & 0xFF) * 255 + alpha / 2) / alpha;
            var b = ((pixel & 0xFF) * 255 + alpha / 2) / alpha;
            pixels[i] = (alpha << 24) | (Math.Min(r, 255) << 16) | (Math.Min(g, 255) << 8) | Math.Min(b, 255);
        }
        paintTextCacheWidth = width;
        paintTextCacheHeight = height;
        return (pixels, width, height);
    }

    private static void BlitPaintText(int[] destination, int destinationWidth, int destinationHeight, int[] source, int sourceWidth, int sourceHeight, int left, int top)
    {
        for (var y = 0; y < sourceHeight; y++)
        {
            var destinationY = top + y;
            if (destinationY < 0 || destinationY >= destinationHeight)
            {
                continue;
            }
            for (var x = 0; x < sourceWidth; x++)
            {
                var destinationX = left + x;
                if (destinationX < 0 || destinationX >= destinationWidth)
                {
                    continue;
                }
                var pixel = source[y * sourceWidth + x];
                var alpha = (pixel >> 24) & 0xFF;
                if (alpha == 0)
                {
                    continue;
                }
                var index = destinationY * destinationWidth + destinationX;
                destination[index] = BlendPixel(destination[index],
                    Color.FromArgb((byte)alpha, (byte)((pixel >> 16) & 0xFF), (byte)((pixel >> 8) & 0xFF), (byte)(pixel & 0xFF)));
            }
        }
    }

    private static void BlitPaintTextScaled(int[] destination, int destinationWidth, int destinationHeight,
        int[] source, int sourceWidth, int sourceHeight, int left, int top, double scaleX, double scaleY)
    {
        var destW = Math.Max(1, (int)Math.Round(sourceWidth * scaleX));
        var destH = Math.Max(1, (int)Math.Round(sourceHeight * scaleY));
        for (var y = 0; y < destH; y++)
        {
            var destinationY = top + y;
            if (destinationY < 0 || destinationY >= destinationHeight)
            {
                continue;
            }
            var sourceY = Math.Min(sourceHeight - 1, (int)(y / scaleY));
            for (var x = 0; x < destW; x++)
            {
                var destinationX = left + x;
                if (destinationX < 0 || destinationX >= destinationWidth)
                {
                    continue;
                }
                var sourceX = Math.Min(sourceWidth - 1, (int)(x / scaleX));
                var pixel = source[sourceY * sourceWidth + sourceX];
                var alpha = (pixel >> 24) & 0xFF;
                if (alpha == 0)
                {
                    continue;
                }
                var index = destinationY * destinationWidth + destinationX;
                destination[index] = BlendPixel(destination[index],
                    Color.FromArgb((byte)alpha, (byte)((pixel >> 16) & 0xFF), (byte)((pixel >> 8) & 0xFF), (byte)(pixel & 0xFF)));
            }
        }
    }

    private static void BlitPaintTextRotated(int[] destination, int destinationWidth, int destinationHeight,
        int[] source, int sourceWidth, int sourceHeight, double cx, double cy, double angleDeg, double scaleX, double scaleY)
    {
        var radians = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var hw = sourceWidth * scaleX / 2.0;
        var hh = sourceHeight * scaleY / 2.0;
        double[] corners = [
            cx + (-hw) * cos - (-hh) * sin, cy + (-hw) * sin + (-hh) * cos,
            cx + ( hw) * cos - (-hh) * sin, cy + ( hw) * sin + (-hh) * cos,
            cx + ( hw) * cos - ( hh) * sin, cy + ( hw) * sin + ( hh) * cos,
            cx + (-hw) * cos - ( hh) * sin, cy + (-hw) * sin + ( hh) * cos,
        ];
        var minDX = (int)Math.Floor(Math.Min(Math.Min(corners[0], corners[2]), Math.Min(corners[4], corners[6])));
        var maxDX = (int)Math.Ceiling(Math.Max(Math.Max(corners[0], corners[2]), Math.Max(corners[4], corners[6])));
        var minDY = (int)Math.Floor(Math.Min(Math.Min(corners[1], corners[3]), Math.Min(corners[5], corners[7])));
        var maxDY = (int)Math.Ceiling(Math.Max(Math.Max(corners[1], corners[3]), Math.Max(corners[5], corners[7])));
        for (var dy = Math.Max(0, minDY); dy <= Math.Min(destinationHeight - 1, maxDY); dy++)
        {
            for (var dx = Math.Max(0, minDX); dx <= Math.Min(destinationWidth - 1, maxDX); dx++)
            {
                var lx = dx - cx;
                var ly = dy - cy;
                var sx = (lx * cos + ly * sin) / scaleX + sourceWidth / 2.0;
                var sy = (-lx * sin + ly * cos) / scaleY + sourceHeight / 2.0;
                var si = (int)Math.Floor(sx);
                var sj = (int)Math.Floor(sy);
                if (si < 0 || si >= sourceWidth || sj < 0 || sj >= sourceHeight)
                {
                    continue;
                }
                var pixel = source[sj * sourceWidth + si];
                var alpha = (pixel >> 24) & 0xFF;
                if (alpha == 0)
                {
                    continue;
                }
                var index = dy * destinationWidth + dx;
                destination[index] = BlendPixel(destination[index],
                    Color.FromArgb((byte)alpha, (byte)((pixel >> 16) & 0xFF), (byte)((pixel >> 8) & 0xFF), (byte)(pixel & 0xFF)));
            }
        }
    }

    private void BlitPaintTextAt(int[] destination, (int[] Pixels, int Width, int Height) rendered, int left, int top)
    {
        if (Math.Abs(paintTextRotation) < 0.001)
        {
            BlitPaintTextScaled(destination, paintWidth, paintHeight, rendered.Pixels, rendered.Width, rendered.Height,
                left, top, paintTextScaleX, paintTextScaleY);
        }
        else
        {
            var cx = left + rendered.Width * paintTextScaleX / 2.0;
            var cy = top + rendered.Height * paintTextScaleY / 2.0;
            BlitPaintTextRotated(destination, paintWidth, paintHeight, rendered.Pixels, rendered.Width, rendered.Height,
                cx, cy, paintTextRotation, paintTextScaleX, paintTextScaleY);
        }
    }

    private void DrawPaintTextPreview()
    {
        if (paintTool != PaintTool.Text)
        {
            return;
        }
        if (paintTextBase is null)
        {
            paintTextBase = (int[])paintPixels.Clone();
        }
        var rendered = RenderPaintText();
        var preview = (int[])paintTextBase.Clone();
        BlitPaintTextAt(preview, rendered,
            (int)Math.Round(paintTextX), (int)Math.Round(paintTextY));
        paintPixels = preview;
        UploadPaintPixels();
        UpdatePaintFrame();
    }

    private void InvalidatePaintText()
    {
        if (paintTool == PaintTool.Text)
        {
            DrawPaintTextPreview();
        }
    }

    private void CommitPaintTextPreview()
    {
        if (paintTextBase is null)
        {
            return;
        }
        paintUndoStack.Add((int[])paintTextBase.Clone());
        if (paintUndoStack.Count > MaxPaintUndo)
        {
            paintUndoStack.RemoveAt(0);
        }
        paintRedoStack.Clear();
        paintTextBase = null;
        UpdatePaintChrome();
    }

    private void StampPaintText()
    {
        if (paintTool != PaintTool.Text)
        {
            return;
        }
        if (paintTextBase is null)
        {
            paintTextBase = (int[])paintPixels.Clone();
        }
        var rendered = RenderPaintText();
        var baked = (int[])paintTextBase.Clone();
        BlitPaintTextAt(baked, rendered,
            (int)Math.Round(paintTextX), (int)Math.Round(paintTextY));
        CommitPaintTextPreview();
        paintPixels = baked;
        paintingDirty = true;
        UploadPaintPixels();
        UpdatePaintFrame();
    }
    private DispatcherTimer? paintSprayTimer;

    private double paintZoom = 1;
    private Point paintPan;
    private bool paintStroking;
    private bool paintStylusDown;
    private bool paintStylusEraser;
    private TouchDevice? paintTouch;
    private Point paintLastCell;
    private int[]? paintShapeBase;
    private PaintTool paintToolBeforePipette = PaintTool.Brush;
    private bool paintPanning;
    private Point paintPanStart;
    private Point paintPanOrigin;
    private bool paintRmbPanning;
    private bool paintSpacePanning;

    private Image? paintImage;
    private FrameworkElement? paintViewport;
    private bool paintFitted;
    private System.Windows.Shapes.Ellipse? paintBrushCursorOuter;
    private System.Windows.Shapes.Ellipse? paintBrushCursorInner;
    private Point paintHoverCell = new(-100, -100);
    private TextBlock? paintZoomLabel;
    private Canvas? paintFrameLayer;
    private System.Windows.Shapes.Polygon? paintFrameBorder;
    private System.Windows.Shapes.Polygon? paintShapePreviewPolygon;
    private System.Windows.Shapes.Path? paintShapePreviewPath;
    private readonly List<System.Windows.Shapes.Rectangle> paintFrameCorners = [];
    private readonly List<System.Windows.Shapes.Rectangle> paintFrameEdges = [];
    private System.Windows.Shapes.Line? paintFrameRotateLine;
    private System.Windows.Shapes.Ellipse? paintFrameRotateHandle;
    private TextBlock? paintShapeSizeLabel;
    private Button? paintUndoButton;
    private Button? paintRedoButton;
    private StackPanel? paintOptionsPanel;
    private StackPanel? paintToolsPanel;
    private Border? paintColorSwatch;
    private Border? paintRailColorSwatch;
    private TextBlock? paintStatusText;
    private Image? paintPreviewImage;
    private readonly List<Border> quickPaletteSwatches = [];
    private StackPanel? paintShapeRow;
    private readonly List<System.Windows.Shapes.Rectangle> paintTextHandles = [];
    private readonly List<System.Windows.Shapes.Rectangle> paintTextEdgeHandles = [];
    private Border? paintTextFloatCard;
    private int[]? paintTextBase;
    private Slider? paintFontSizeSlider;

    private string? paintingNewFileName;

    private void StartPainting(IskraScene scene, IskraObject item, string? path, int width = 0, int height = 0, string? newFileName = null)
    {
        if (activeProject is null)
        {
            return;
        }

        try
        {
            if (path is not null)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var memory = new MemoryStream();
                stream.CopyTo(memory);
                memory.Position = 0;
                var decoder = BitmapDecoder.Create(memory, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                paintWidth = converted.PixelWidth;
                paintHeight = converted.PixelHeight;
                if (paintWidth < 1 || paintHeight < 1 || paintWidth > MaxPaintSide || paintHeight > MaxPaintSide)
                {
                    MessageBox.Show(this, "Картинка слишком большая для рисовалки.", "Искра Студио", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                paintPixels = new int[paintWidth * paintHeight];
                converted.CopyPixels(paintPixels, paintWidth * 4, 0);
            }
            else
            {
                paintWidth = width;
                paintHeight = height;
                paintPixels = new int[paintWidth * paintHeight];
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            AppLog.Write("Не удалось открыть картинку для рисования.", exception);
            MessageBox.Show(this, "Не удалось открыть картинку.", "Искра Студио", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        paintingScene = scene;
        paintingItem = item;
        paintingPath = path;
        paintingNewFileName = newFileName;
        paintingDirty = false;
        paintUndoStack.Clear();
        paintRedoStack.Clear();
        paintTool = PaintTool.Brush;
        shapeFrameActive = false;
        paintTextPlaced = false;
        paintTextScaleX = 1;
        paintTextScaleY = 1;
        paintTextBase = null;
        paintTextFloatCard = null;
        paintFrameAction = PaintFrameAction.None;
        paintZoom = 1;
        paintFitted = false;
        paintPan = new Point(0, 0);
        paintBitmap = new WriteableBitmap(paintWidth, paintHeight, 96, 96, PixelFormats.Bgra32, null);
        UploadPaintPixels();

        showingPainting = true;
        WorkspaceTitle.Text = "Рисовалка";
        WorkspaceBackButton.Content = "←  " + item.Name;
        WorkspaceMenuButton.Visibility = Visibility.Collapsed;
        WorkspaceConfirmSelectionButton.Visibility = Visibility.Collapsed;
        WorkspaceCancelSelectionButton.Visibility = Visibility.Collapsed;
        CreateProjectButton.Visibility = Visibility.Collapsed;
        UpdateFloatingCreateButton();
        PreviewKeyDown -= PaintScreen_KeyDown;
        PreviewKeyDown += PaintScreen_KeyDown;
        PreviewKeyUp -= PaintScreen_KeyUp;
        PreviewKeyUp += PaintScreen_KeyUp;
        BuildPaintScreen();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(FitPaintToView));
    }

    private void UploadPaintPixels()
    {
        paintBitmap?.WritePixels(new Int32Rect(0, 0, paintWidth, paintHeight), paintPixels, paintWidth * 4, 0);
    }

    private void BuildPaintScreen()
    {
        WorkspaceContentHost.Children.Clear();
        if (paintBitmap is null)
        {
            return;
        }
        quickPaletteSwatches.Clear();

        var layout = new DockPanel { LastChildFill = true };

        var topContainer = new StackPanel();

        var toolbar = new Border
        {
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 6, 12, 6)
        };
        var toolbarRow = new DockPanel { LastChildFill = false };

        var leftToolbar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        paintUndoButton = CreatePaintTopButton("undo", "Отменить (Ctrl+Z)", _ => UndoPaint());
        paintRedoButton = CreatePaintTopButton("redo", "Вернуть (Ctrl+Y)", _ => RedoPaint());
        leftToolbar.Children.Add(paintUndoButton);
        leftToolbar.Children.Add(paintRedoButton);
        leftToolbar.Children.Add(CreateVerticalSeparator());

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        titleStack.Children.Add(new TextBlock
        {
            Text = "Рисование",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFor("InkBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        if (paintingItem is not null)
        {
            titleStack.Children.Add(new TextBlock
            {
                Text = "  •  " + paintingItem.Name,
                FontSize = 13,
                Foreground = BrushFor("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        leftToolbar.Children.Add(titleStack);

        var rightToolbar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var zoomOut = CreatePaintTopButton(null, "−", _ => ZoomPaintToCenter(paintZoom / 1.25));
        zoomOut.ToolTip = "Уменьшить (Ctrl+−)";
        paintZoomLabel = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = BrushFor("BodyBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
            MinWidth = 44,
            TextAlignment = TextAlignment.Center
        };
        var zoomIn = CreatePaintTopButton(null, "+", _ => ZoomPaintToCenter(paintZoom * 1.25));
        zoomIn.ToolTip = "Увеличить (Ctrl++)";
        var fitButton = CreatePaintTopTextButton("⛶ Вписать", "Вписать холст в окно (Ctrl+0)", _ => FitPaintToView());
        var zoom100Button = CreatePaintTopTextButton("1:1", "Масштаб 100%", _ => ZoomPaintToCenter(1.0));

        rightToolbar.Children.Add(zoomOut);
        rightToolbar.Children.Add(paintZoomLabel);
        rightToolbar.Children.Add(zoomIn);
        rightToolbar.Children.Add(fitButton);
        rightToolbar.Children.Add(zoom100Button);
        rightToolbar.Children.Add(CreateVerticalSeparator());

        var saveButton = new Button
        {
            Style = (Style)FindResource("PrimaryButton"),
            Content = "✓  Сохранить",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 5, 14, 5),
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Сохранить рисунок (Ctrl+S)"
        };
        saveButton.Click += (_, _) => SavePainting();
        AutomationProperties.SetName(saveButton, "Сохранить рисунок");
        rightToolbar.Children.Add(saveButton);

        DockPanel.SetDock(leftToolbar, Dock.Left);
        DockPanel.SetDock(rightToolbar, Dock.Right);
        toolbarRow.Children.Add(leftToolbar);
        toolbarRow.Children.Add(rightToolbar);
        toolbar.Child = toolbarRow;
        topContainer.Children.Add(toolbar);

        paintOptionsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        RebuildPaintOptions();
        var optionsBar = new Border
        {
            Background = BrushFor("SoftBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 5, 14, 5),
            Child = paintOptionsPanel
        };
        topContainer.Children.Add(optionsBar);

        DockPanel.SetDock(topContainer, Dock.Top);
        layout.Children.Add(topContainer);

        paintToolsPanel = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        RebuildPaintTools();

        var railContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        railContent.Children.Add(paintToolsPanel);
        railContent.Children.Add(new Border
        {
            Width = 32,
            Height = 1,
            Background = BrushFor("HairlineBrush"),
            Margin = new Thickness(0, 6, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        paintRailColorSwatch = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(paintColor),
            BorderBrush = BrushFor("AccentBrush"),
            BorderThickness = new Thickness(2.5),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var railSwatchBtn = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Padding = new Thickness(0),
            Content = paintRailColorSwatch,
            ToolTip = "Выбор цвета (кликните для палитры)"
        };
        railSwatchBtn.Click += (_, _) => ShowPaintColorDialog();
        railContent.Children.Add(railSwatchBtn);

        var leftRail = new Border
        {
            Width = 56,
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(4, 6, 4, 8),
            Child = railContent
        };
        DockPanel.SetDock(leftRail, Dock.Left);
        layout.Children.Add(leftRail);

        var sidePanel = BuildPaintRightSidebar();
        DockPanel.SetDock(sidePanel, Dock.Right);
        layout.Children.Add(sidePanel);

        var viewport = BuildPaintViewport();
        layout.Children.Add(viewport);

        WorkspaceContentHost.Children.Add(layout);
        UpdatePaintChrome();
        UpdateQuickPaletteHighlights();
    }

    private FrameworkElement BuildPaintRightSidebar()
    {
        var sideBorder = new Border
        {
            Width = 210,
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1, 0, 0, 0)
        };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12, 12, 12, 16)
        };
        var content = new StackPanel();

        content.Children.Add(new TextBlock
        {
            Text = "ОБРАЗ",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = BrushFor("MutedBrush"),
            Margin = new Thickness(2, 0, 0, 8)
        });

        var previewBox = new Border
        {
            Height = 110,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
            Background = CreateCheckerboardBrush(8)
        };
        paintPreviewImage = new Image
        {
            Source = paintBitmap,
            Stretch = Stretch.Uniform
        };
        previewBox.Child = paintPreviewImage;
        content.Children.Add(previewBox);

        var infoStack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (paintingPath is not null)
        {
            infoStack.Children.Add(new TextBlock
            {
                Text = System.IO.Path.GetFileName(paintingPath),
                FontSize = 11,
                Foreground = BrushFor("BodyBrush"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3)
            });
        }
        var sizeBadge = new Border
        {
            Background = BrushFor("SoftBrush"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"{paintWidth} × {paintHeight} px",
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = BrushFor("MutedBrush")
            }
        };
        infoStack.Children.Add(sizeBadge);
        content.Children.Add(infoStack);

        var actionsRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        actionsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        actionsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fitBtn = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = "Вписать",
            FontSize = 11,
            Padding = new Thickness(0, 4, 0, 4),
            ToolTip = "Вписать рисунок в холст (Ctrl+0)"
        };
        fitBtn.Click += (_, _) => FitPaintToView();
        Grid.SetColumn(fitBtn, 0);
        actionsRow.Children.Add(fitBtn);

        var clearBtn = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = "Очистить",
            FontSize = 11,
            Padding = new Thickness(0, 4, 0, 4),
            ToolTip = "Очистить холст (с возможностью отмены)"
        };
        clearBtn.Click += (_, _) => ClearCanvasWithUndo();
        Grid.SetColumn(clearBtn, 2);
        actionsRow.Children.Add(clearBtn);

        content.Children.Add(actionsRow);

        content.Children.Add(new TextBlock
        {
            Text = "БЫСТРАЯ ПАЛИТРА",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = BrushFor("MutedBrush"),
            Margin = new Thickness(2, 0, 0, 8)
        });

        var paletteGrid = new System.Windows.Controls.Primitives.UniformGrid
        {
            Columns = 4,
            Margin = new Thickness(0, 0, 0, 8)
        };
        foreach (var presetColor in PaintPalettePresets)
        {
            var swatchBorder = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(6),
                BorderBrush = BrushFor("HairlineBrush"),
                BorderThickness = new Thickness(1),
                Tag = presetColor
            };
            if (presetColor == Colors.Transparent)
            {
                swatchBorder.Background = CreateCheckerboardBrush(6);
                swatchBorder.Child = new System.Windows.Shapes.Line
                {
                    X1 = 4, Y1 = 4, X2 = 28, Y2 = 28,
                    Stroke = Brushes.Red,
                    StrokeThickness = 2
                };
            }
            else
            {
                swatchBorder.Background = new SolidColorBrush(presetColor);
            }
            var btn = new Button
            {
                Style = (Style)FindResource("ToolButton"),
                Padding = new Thickness(0),
                Margin = new Thickness(2),
                Content = swatchBorder,
                ToolTip = presetColor == Colors.Transparent ? "Прозрачный" : $"#{presetColor.R:X2}{presetColor.G:X2}{presetColor.B:X2}"
            };
            btn.Click += (_, _) => SetPaintColor(presetColor);
            quickPaletteSwatches.Add(swatchBorder);
            paletteGrid.Children.Add(btn);
        }
        content.Children.Add(paletteGrid);

        var fullPaletteBtn = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = "🎨  Настроить цвет...",
            FontSize = 12,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 16),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ToolTip = "Открыть полную палитру (HSV / RGB)"
        };
        fullPaletteBtn.Click += (_, _) => ShowPaintColorDialog();
        content.Children.Add(fullPaletteBtn);

        content.Children.Add(new TextBlock
        {
            Text = "ГОРЯЧИЕ КЛАВИШИ",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = BrushFor("MutedBrush"),
            Margin = new Thickness(2, 0, 0, 6)
        });

        var shortcuts = new (string Key, string Desc)[]
        {
            ("B / E", "Кисть / Ластик"),
            ("G / I", "Заливка / Пипетка"),
            ("U / L", "Фигура / Линия"),
            ("[ / ]", "Размер ±1"),
            ("C", "Палитра"),
            ("Пробел", "Рука (тянуть)"),
            ("Колесо", "Масштаб"),
            ("Ctrl+Z", "Отменить"),
            ("Ctrl+Y", "Вернуть"),
            ("Ctrl+S", "Сохранить")
        };
        foreach (var (k, d) in shortcuts)
        {
            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2) };
            var kBlock = new TextBlock
            {
                Text = k,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFor("InkBrush"),
                Width = 56
            };
            DockPanel.SetDock(kBlock, Dock.Left);
            row.Children.Add(kBlock);
            row.Children.Add(new TextBlock
            {
                Text = d,
                FontSize = 11,
                Foreground = BrushFor("MutedBrush")
            });
            content.Children.Add(row);
        }

        sideBorder.Child = scroll;
        scroll.Content = content;
        return sideBorder;
    }

    private FrameworkElement BuildPaintViewport()
    {
        var viewport = new Grid { ClipToBounds = true, Background = BrushFor("CanvasBrush") };
        var checker = new Border
        {
            Width = paintWidth,
            Height = paintHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = CreateCheckerboardBrush(8),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1)
        };
        paintImage = new Image
        {
            Source = paintBitmap,
            Width = paintWidth,
            Height = paintHeight,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        RenderOptions.SetBitmapScalingMode(paintImage, BitmapScalingMode.NearestNeighbor);
        paintBrushCursorOuter = new System.Windows.Shapes.Ellipse
        {
            Stroke = BrushFor("InkBrush"),
            StrokeThickness = 1,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        paintBrushCursorInner = new System.Windows.Shapes.Ellipse
        {
            Stroke = new SolidColorBrush(paintColor),
            StrokeThickness = 2,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        var stage = new Canvas
        {
            Width = paintWidth,
            Height = paintHeight,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        stage.Children.Add(checker);
        stage.Children.Add(paintImage);
        stage.Children.Add(paintBrushCursorOuter);
        stage.Children.Add(paintBrushCursorInner);
        ApplyPaintTransform(stage);
        Panel.SetZIndex(stage, 0);
        viewport.Children.Add(stage);
        paintViewport = viewport;
        paintFrameLayer = new Canvas { IsHitTestVisible = false };
        Panel.SetZIndex(paintFrameLayer, 100);

        paintShapePreviewPolygon = new System.Windows.Shapes.Polygon
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        paintShapePreviewPath = new System.Windows.Shapes.Path
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        paintFrameLayer.Children.Add(paintShapePreviewPolygon);
        paintFrameLayer.Children.Add(paintShapePreviewPath);

        paintFrameBorder = new System.Windows.Shapes.Polygon
        {
            Stroke = BrushFor("AccentBrush"),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        paintFrameLayer.Children.Add(paintFrameBorder);
        paintFrameCorners.Clear();
        paintFrameEdges.Clear();
        paintTextHandles.Clear();
        for (var corner = 0; corner < 4; corner++)
        {
            var handle = new System.Windows.Shapes.Rectangle
            {
                Width = 14,
                Height = 14,
                Fill = BrushFor("PanelBrush"),
                Stroke = BrushFor("AccentBrush"),
                StrokeThickness = 2.5,
                Visibility = Visibility.Collapsed
            };
            paintFrameCorners.Add(handle);
            paintFrameLayer.Children.Add(handle);
        }
        for (var edge = 0; edge < 4; edge++)
        {
            var edgeHandle = new System.Windows.Shapes.Rectangle
            {
                Width = 12,
                Height = 12,
                Fill = BrushFor("PanelBrush"),
                Stroke = BrushFor("AccentBrush"),
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed
            };
            paintFrameEdges.Add(edgeHandle);
            paintFrameLayer.Children.Add(edgeHandle);
        }
        paintFrameRotateLine = new System.Windows.Shapes.Line
        {
            Stroke = BrushFor("AccentBrush"),
            StrokeThickness = 2,
            Visibility = Visibility.Collapsed
        };
        paintFrameRotateHandle = new System.Windows.Shapes.Ellipse
        {
            Width = 22,
            Height = 22,
            Fill = BrushFor("PanelBrush"),
            Stroke = BrushFor("AccentBrush"),
            StrokeThickness = 2.5,
            Visibility = Visibility.Collapsed
        };
        paintFrameLayer.Children.Add(paintFrameRotateLine);
        paintFrameLayer.Children.Add(paintFrameRotateHandle);
        paintTextHandles.Clear();
        paintTextEdgeHandles.Clear();
        for (var th = 0; th < 4; th++)
        {
            var textHandle = new System.Windows.Shapes.Rectangle
            {
                Width = 14,
                Height = 14,
                Fill = BrushFor("PanelBrush"),
                Stroke = new SolidColorBrush(Colors.MediumPurple),
                StrokeThickness = 2.5,
                Visibility = Visibility.Collapsed
            };
            paintTextHandles.Add(textHandle);
            paintFrameLayer.Children.Add(textHandle);
        }
        for (var te = 0; te < 4; te++)
        {
            var edgeHandle = new System.Windows.Shapes.Rectangle
            {
                Width = 12,
                Height = 12,
                Fill = BrushFor("PanelBrush"),
                Stroke = new SolidColorBrush(Colors.MediumPurple),
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed
            };
            paintTextEdgeHandles.Add(edgeHandle);
            paintFrameLayer.Children.Add(edgeHandle);
        }
        viewport.Children.Add(paintFrameLayer);

        var statusPill = new Border
        {
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(12, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false
        };
        Panel.SetZIndex(statusPill, 200);
        paintStatusText = new TextBlock
        {
            Text = $"{paintWidth} × {paintHeight} px",
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = BrushFor("MutedBrush")
        };
        statusPill.Child = paintStatusText;
        viewport.Children.Add(statusPill);

        viewport.SizeChanged += (_, _) =>
        {
            if (!paintFitted && viewport.ActualWidth > 0 && viewport.ActualHeight > 0)
            {
                paintFitted = true;
                FitPaintToView();
            }
        };
        viewport.MouseLeftButtonDown += PaintViewport_MouseDown;
        viewport.MouseMove += PaintViewport_MouseMove;
        viewport.MouseLeftButtonUp += PaintViewport_MouseUp;
        viewport.StylusDown += (_, e) =>
        {
            paintStylusDown = true;
            paintStylusEraser = e.StylusDevice.Inverted;
            PaintPointerDown(e.GetPosition(paintViewport));
            paintViewport?.CaptureStylus();
            e.Handled = true;
        };
        viewport.StylusMove += (_, e) =>
        {
            var position = e.GetPosition(paintViewport);
            var hover = PaintCellAt(PaintImagePos(position));
            paintHoverCell = hover;
            UpdatePaintCursor();
            UpdatePaintStatus();
            if (paintStylusDown)
            {
                PaintPointerMove(position, true);
            }
        };
        viewport.StylusUp += (_, e) =>
        {
            paintStylusDown = false;
            paintStylusEraser = false;
            PaintPointerUp(e.GetPosition(paintViewport));
            paintViewport?.ReleaseStylusCapture();
        };
        viewport.TouchDown += (_, e) =>
        {
            if (paintTouch is null)
            {
                paintTouch = e.TouchDevice;
                PaintPointerDown(e.GetTouchPoint(paintViewport).Position);
                paintViewport?.CaptureTouch(e.TouchDevice);
                e.Handled = true;
            }
        };
        viewport.TouchMove += (_, e) =>
        {
            if (e.TouchDevice == paintTouch)
            {
                PaintPointerMove(e.GetTouchPoint(paintViewport).Position, true);
            }
        };
        viewport.TouchUp += (_, e) =>
        {
            if (e.TouchDevice == paintTouch)
            {
                paintTouch = null;
                PaintPointerUp(e.GetTouchPoint(paintViewport).Position);
                paintViewport?.ReleaseTouchCapture(e.TouchDevice);
            }
        };
        viewport.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton is MouseButton.Right or MouseButton.Middle)
            {
                paintRmbPanning = true;
                paintPanStart = e.GetPosition(paintViewport);
                paintPanOrigin = paintPan;
                paintViewport?.CaptureMouse();
                e.Handled = true;
            }
        };
        viewport.PreviewMouseUp += (_, e) =>
        {
            if (e.ChangedButton is MouseButton.Right or MouseButton.Middle)
            {
                paintRmbPanning = false;
                paintViewport?.ReleaseMouseCapture();
                e.Handled = true;
            }
        };
        viewport.MouseLeave += (_, _) =>
        {
            paintHoverCell = new Point(-100, -100);
            UpdatePaintCursor();
            UpdatePaintStatus();
        };
        viewport.MouseWheel += (_, e) =>
        {
            ZoomPaint(e.Delta > 0 ? paintZoom * 1.15 : paintZoom / 1.15, e.GetPosition(viewport));
            e.Handled = true;
        };
        return viewport;
    }

    private Button CreatePaintTopButton(string? icon, string label, Action<Button> run)
    {
        var button = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(1, 0, 1, 0),
            ToolTip = label
        };
        if (icon is not null)
        {
            button.Content = CreateSheetIcon(icon, 18, BrushFor("BodyBrush"));
        }
        else
        {
            button.Content = new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = BrushFor("BodyBrush") };
        }
        button.Click += (_, _) => run(button);
        AutomationProperties.SetName(button, label);
        return button;
    }

    private Button CreatePaintTopTextButton(string text, string tooltip, Action<Button> run)
    {
        var button = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(2, 0, 2, 0),
            ToolTip = tooltip,
            Content = new TextBlock { Text = text, FontSize = 12, Foreground = BrushFor("BodyBrush") }
        };
        button.Click += (_, _) => run(button);
        AutomationProperties.SetName(button, tooltip);
        return button;
    }

    private Border CreateVerticalSeparator() => new()
    {
        Width = 1,
        Height = 20,
        Background = BrushFor("HairlineBrush"),
        Margin = new Thickness(8, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static DrawingBrush CreateCheckerboardBrush(int cellSize = 8)
    {
        var checkerGeometry = new GeometryGroup();
        checkerGeometry.Children.Add(new RectangleGeometry(new Rect(0, 0, cellSize, cellSize)));
        checkerGeometry.Children.Add(new RectangleGeometry(new Rect(cellSize, cellSize, cellSize, cellSize)));
        checkerGeometry.Freeze();
        var c1 = new SolidColorBrush(Color.FromRgb(240, 240, 240));
        c1.Freeze();
        var c2 = new SolidColorBrush(Color.FromRgb(204, 204, 204));
        c2.Freeze();
        var checkerDrawing = new DrawingGroup();
        checkerDrawing.Children.Add(new GeometryDrawing(c1, null, new RectangleGeometry(new Rect(0, 0, cellSize * 2, cellSize * 2))));
        checkerDrawing.Children.Add(new GeometryDrawing(c2, null, checkerGeometry));
        checkerDrawing.Freeze();
        var brush = new DrawingBrush
        {
            Viewport = new Rect(0, 0, cellSize * 2, cellSize * 2),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile,
            Drawing = checkerDrawing
        };
        brush.Freeze();
        return brush;
    }

    private void ClearCanvasWithUndo()
    {
        var result = MessageBox.Show(this, "Очистить холст? Действие можно отменить комбинацией Ctrl+Z.", "Очистить холст", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }
        PushPaintUndo();
        Array.Clear(paintPixels, 0, paintPixels.Length);
        UploadPaintPixels();
        paintingDirty = true;
    }

    private void UpdateQuickPaletteHighlights()
    {
        foreach (var border in quickPaletteSwatches)
        {
            if (border.Tag is Color c && c == paintColor)
            {
                border.BorderBrush = BrushFor("AccentBrush");
                border.BorderThickness = new Thickness(2.5);
            }
            else
            {
                border.BorderBrush = BrushFor("HairlineBrush");
                border.BorderThickness = new Thickness(1);
            }
        }
    }

    private int paintLastStatusX = -999;
    private int paintLastStatusY = -999;
    private int paintLastStatusZoom = -999;

    private void UpdatePaintStatus()
    {
        if (paintStatusText is null)
        {
            return;
        }
        var x = (int)paintHoverCell.X;
        var y = (int)paintHoverCell.Y;
        var zoomPercent = (int)Math.Round(paintZoom * 100);
        if (x == paintLastStatusX && y == paintLastStatusY && zoomPercent == paintLastStatusZoom)
        {
            return;
        }
        paintLastStatusX = x;
        paintLastStatusY = y;
        paintLastStatusZoom = zoomPercent;

        if (x >= 0 && y >= 0 && x < paintWidth && y < paintHeight)
        {
            paintStatusText.Text = $"X: {x}  Y: {y}  •  {zoomPercent}%";
        }
        else
        {
            paintStatusText.Text = $"{zoomPercent}%  •  {paintWidth} × {paintHeight} px";
        }
    }

    private void ApplyPaintTransform(FrameworkElement stage)
    {
        stage.RenderTransform = new TransformGroup
        {
            Children =
            [
                new ScaleTransform(paintZoom, paintZoom),
                new TranslateTransform(paintPan.X, paintPan.Y)
            ]
        };
    }

    private void RefreshPaintTransform()
    {
        if (paintViewport is Grid grid && grid.Children[0] is FrameworkElement stage)
        {
            ApplyPaintTransform(stage);
        }
        UpdatePaintFrame();
        UpdatePaintChrome();
    }

    private void UpdatePaintCursor()
    {
        if (paintBrushCursorOuter is null || paintBrushCursorInner is null)
        {
            return;
        }
        var show = paintTool is PaintTool.Brush or PaintTool.Eraser or PaintTool.Spray &&
            paintHoverCell.X >= 0 && paintHoverCell.Y >= 0 &&
            paintHoverCell.X < paintWidth && paintHoverCell.Y < paintHeight;
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        paintBrushCursorOuter.Visibility = visibility;
        paintBrushCursorInner.Visibility = visibility;
        if (!show)
        {
            return;
        }
        var diameter = Math.Max(1, paintSize);
        var strokeThick = 1.0 / Math.Max(0.1, paintZoom);
        foreach (var ring in new[] { paintBrushCursorOuter, paintBrushCursorInner })
        {
            ring.Width = diameter;
            ring.Height = diameter;
            ring.StrokeThickness = strokeThick;
            Canvas.SetLeft(ring, paintHoverCell.X - diameter / 2.0);
            Canvas.SetTop(ring, paintHoverCell.Y - diameter / 2.0);
        }
    }

    private void UpdatePaintChrome()
    {
        if (paintZoomLabel is not null)
        {
            paintZoomLabel.Text = ((int)Math.Round(paintZoom * 100)) + "%";
        }
        if (paintUndoButton is not null)
        {
            paintUndoButton.IsEnabled = paintUndoStack.Count > 0;
            paintUndoButton.Opacity = paintUndoStack.Count > 0 ? 1 : 0.4;
        }
        if (paintRedoButton is not null)
        {
            paintRedoButton.IsEnabled = paintRedoStack.Count > 0;
            paintRedoButton.Opacity = paintRedoStack.Count > 0 ? 1 : 0.4;
        }
        UpdatePaintStatus();
    }

    private void UpdatePaintSizeLabel()
    {
    }

    private Point PaintToScreen(Point cell) => new(cell.X * paintZoom + paintPan.X, cell.Y * paintZoom + paintPan.Y);

    private Point ScreenToPaint(Point screen) => new((screen.X - paintPan.X) / paintZoom, (screen.Y - paintPan.Y) / paintZoom);

    private Point[] ShapeFrameCorners()
    {
        var radians = shapeFrameRotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var halfW = shapeFrameWidth / 2;
        var halfH = shapeFrameHeight / 2;
        return
        [
            RotateFramePoint(-halfW, -halfH, cos, sin),
            RotateFramePoint(halfW, -halfH, cos, sin),
            RotateFramePoint(halfW, halfH, cos, sin),
            RotateFramePoint(-halfW, halfH, cos, sin)
        ];

        Point RotateFramePoint(double lx, double ly, double c, double s) =>
            new(shapeFrameX + lx * c - ly * s, shapeFrameY + lx * s + ly * c);
    }

    private Point ShapeFrameEdgeMid(int edge)
    {
        var corners = ShapeFrameCorners();
        var a = corners[edge % 4];
        var b = corners[(edge + 1) % 4];
        return new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    }

    private Point ShapeFrameRotateHandle()
    {
        var radians = shapeFrameRotation * Math.PI / 180;
        var top = ShapeFrameEdgeMid(0);
        var dx = -Math.Sin(radians);
        var dy = -Math.Cos(radians);
        var screen = PaintToScreen(top);
        return new Point(screen.X + dx * 44, screen.Y + dy * 44);
    }

    private Point[] TextFrameScreenCorners()
    {
        var w = Math.Max(16, PaintTextEffW());
        var h = Math.Max(16, PaintTextEffH());
        var cx = paintTextX + w / 2.0;
        var cy = paintTextY + h / 2.0;
        var rad = paintTextRotation * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var hw = w / 2.0;
        var hh = h / 2.0;
        Point RotTx(double lx, double ly) =>
            PaintToScreen(new Point(cx + lx * cos - ly * sin, cy + lx * sin + ly * cos));
        return [RotTx(-hw, -hh), RotTx(hw, -hh), RotTx(hw, hh), RotTx(-hw, hh)];
    }

    private Point TextFrameRotateHandle()
    {
        var corners = TextFrameScreenCorners();
        var topMid = new Point((corners[0].X + corners[1].X) / 2, (corners[0].Y + corners[1].Y) / 2);
        var rad = paintTextRotation * Math.PI / 180.0;
        var dx = -Math.Sin(rad);
        var dy = -Math.Cos(rad);
        return new Point(topMid.X + dx * 44, topMid.Y + dy * 44);
    }

    private void UpdatePaintFrame()
    {
        var showShape = paintTool == PaintTool.Shape && paintFrameBorder is not null;
        var showText = paintTool == PaintTool.Text && paintFrameBorder is not null;
        var show = showShape || showText;
        var frameBrush = BrushFor("AccentBrush");
        paintFrameBorder!.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        paintFrameBorder.Stroke = frameBrush;
        foreach (var corner in paintFrameCorners)
        {
            corner.Visibility = showShape ? Visibility.Visible : Visibility.Collapsed;
            corner.Stroke = frameBrush;
        }
        foreach (var edge in paintFrameEdges)
        {
            edge.Visibility = showShape ? Visibility.Visible : Visibility.Collapsed;
            edge.Stroke = frameBrush;
        }
        foreach (var th in paintTextHandles)
        {
            th.Visibility = showText ? Visibility.Visible : Visibility.Collapsed;
            th.Stroke = frameBrush;
            th.Fill = BrushFor("PanelBrush");
        }
        foreach (var te in paintTextEdgeHandles)
        {
            te.Visibility = showText ? Visibility.Visible : Visibility.Collapsed;
            te.Stroke = frameBrush;
            te.Fill = BrushFor("PanelBrush");
        }
        if (paintFrameRotateLine is not null)
        {
            paintFrameRotateLine.Visibility = (showShape || showText) ? Visibility.Visible : Visibility.Collapsed;
            paintFrameRotateLine.Stroke = frameBrush;
        }
        if (paintFrameRotateHandle is not null)
        {
            paintFrameRotateHandle.Visibility = (showShape || showText) ? Visibility.Visible : Visibility.Collapsed;
            paintFrameRotateHandle.Stroke = frameBrush;
        }
        if (paintShapeSizeLabel is not null)
        {
            paintShapeSizeLabel.Text = $"{(int)Math.Round(shapeFrameWidth)}×{(int)Math.Round(shapeFrameHeight)}";
        }
        if (paintShapePreviewPolygon is not null)
        {
            paintShapePreviewPolygon.Visibility = showShape && paintShape == PaintShape.Rectangle ? Visibility.Visible : Visibility.Collapsed;
        }
        if (paintShapePreviewPath is not null)
        {
            paintShapePreviewPath.Visibility = showShape && paintShape == PaintShape.Ellipse ? Visibility.Visible : Visibility.Collapsed;
        }
        if (!show)
        {
            return;
        }

        var corners = ShapeFrameCorners();
        var collection = new PointCollection { PaintToScreen(corners[0]), PaintToScreen(corners[1]), PaintToScreen(corners[2]), PaintToScreen(corners[3]) };
        if (showText)
        {
            var tcs = TextFrameScreenCorners();
            collection = new PointCollection(tcs);
            paintFrameBorder.Points = collection;
            for (var i = 0; i < 4 && i < paintTextHandles.Count; i++)
            {
                Canvas.SetLeft(paintTextHandles[i], tcs[i].X - 7);
                Canvas.SetTop(paintTextHandles[i], tcs[i].Y - 7);
            }
            for (var e = 0; e < 4 && e < paintTextEdgeHandles.Count; e++)
            {
                var mid = new Point((tcs[e].X + tcs[(e + 1) % 4].X) / 2, (tcs[e].Y + tcs[(e + 1) % 4].Y) / 2);
                Canvas.SetLeft(paintTextEdgeHandles[e], mid.X - 6);
                Canvas.SetTop(paintTextEdgeHandles[e], mid.Y - 6);
            }
            var textTopMid = new Point((tcs[0].X + tcs[1].X) / 2, (tcs[0].Y + tcs[1].Y) / 2);
            var textRotHandle = TextFrameRotateHandle();
            paintFrameRotateLine!.X1 = textTopMid.X;
            paintFrameRotateLine.Y1 = textTopMid.Y;
            paintFrameRotateLine.X2 = textRotHandle.X;
            paintFrameRotateLine.Y2 = textRotHandle.Y;
            Canvas.SetLeft(paintFrameRotateHandle!, textRotHandle.X - 11);
            Canvas.SetTop(paintFrameRotateHandle!, textRotHandle.Y - 11);
        }
        else
        {
            paintFrameBorder.Points = collection;
        }

        var isTransparent = paintColor.A == 0;
        Brush fillBrush = paintShapeFilled
            ? (isTransparent ? new SolidColorBrush(Color.FromArgb(90, 255, 60, 60)) : new SolidColorBrush(paintColor))
            : Brushes.Transparent;
        Brush strokeBrush = !paintShapeFilled
            ? (isTransparent ? new SolidColorBrush(Color.FromArgb(160, 255, 60, 60)) : new SolidColorBrush(paintColor))
            : Brushes.Transparent;
        var strokeThick = paintShapeFilled ? 0 : Math.Max(1.0, paintSize * paintZoom);

        if (paintShape == PaintShape.Rectangle && paintShapePreviewPolygon is not null)
        {
            paintShapePreviewPolygon.Points = collection;
            paintShapePreviewPolygon.Fill = fillBrush;
            paintShapePreviewPolygon.Stroke = strokeBrush;
            paintShapePreviewPolygon.StrokeThickness = strokeThick;
        }
        else if (paintShape == PaintShape.Ellipse && paintShapePreviewPath is not null)
        {
            var centerScreen = PaintToScreen(new Point(shapeFrameX, shapeFrameY));
            var rxScreen = Math.Max(0.5, shapeFrameWidth / 2.0 * paintZoom);
            var ryScreen = Math.Max(0.5, shapeFrameHeight / 2.0 * paintZoom);
            var geom = new EllipseGeometry(new Point(0, 0), rxScreen, ryScreen)
            {
                Transform = new TransformGroup
                {
                    Children =
                    [
                        new RotateTransform(shapeFrameRotation),
                        new TranslateTransform(centerScreen.X, centerScreen.Y)
                    ]
                }
            };
            paintShapePreviewPath.Data = geom;
            paintShapePreviewPath.Fill = fillBrush;
            paintShapePreviewPath.Stroke = strokeBrush;
            paintShapePreviewPath.StrokeThickness = strokeThick;
        }

        if (!showText)
        {
            for (var i = 0; i < 4 && i < paintFrameCorners.Count; i++)
            {
                var screen = PaintToScreen(corners[i]);
                Canvas.SetLeft(paintFrameCorners[i], screen.X - 7);
                Canvas.SetTop(paintFrameCorners[i], screen.Y - 7);
            }
            for (var edge = 0; edge < 4 && edge < paintFrameEdges.Count; edge++)
            {
                var screen = PaintToScreen(ShapeFrameEdgeMid(edge));
                Canvas.SetLeft(paintFrameEdges[edge], screen.X - 6);
                Canvas.SetTop(paintFrameEdges[edge], screen.Y - 6);
            }
            if (!showText)
            {
                var top = PaintToScreen(ShapeFrameEdgeMid(0));
                var handle = ShapeFrameRotateHandle();
                paintFrameRotateLine!.X1 = top.X;
                paintFrameRotateLine.Y1 = top.Y;
                paintFrameRotateLine.X2 = handle.X;
                paintFrameRotateLine.Y2 = handle.Y;
                Canvas.SetLeft(paintFrameRotateHandle!, handle.X - 10);
                Canvas.SetTop(paintFrameRotateHandle!, handle.Y - 10);
            }
        }
    }

    private PaintFrameAction HitPaintFrame(Point screen, out int handle)
    {
        handle = -1;
        if (paintTool == PaintTool.Text)
        {
            var textRotHandle = TextFrameRotateHandle();
            if (Distance(screen, textRotHandle) < 18)
            {
                return PaintFrameAction.Rotate;
            }
            var tcs = TextFrameScreenCorners();
            for (var i = 0; i < 4; i++)
            {
                if (Distance(screen, tcs[i]) < 18)
                {
                    handle = i;
                    return PaintFrameAction.Resize;
                }
            }
            for (var e = 0; e < 4; e++)
            {
                var mid = new Point((tcs[e].X + tcs[(e + 1) % 4].X) / 2, (tcs[e].Y + tcs[(e + 1) % 4].Y) / 2);
                if (Distance(screen, mid) < 16)
                {
                    handle = 4 + e;
                    return PaintFrameAction.Resize;
                }
            }
            var w = Math.Max(16, PaintTextEffW());
            var h = Math.Max(16, PaintTextEffH());
            var cx = paintTextX + w / 2.0;
            var cy = paintTextY + h / 2.0;
            var pt = ScreenToPaint(screen);
            var rad = -paintTextRotation * Math.PI / 180.0;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            var lx = pt.X - cx;
            var ly = pt.Y - cy;
            var rx = lx * cos - ly * sin;
            var ry = lx * sin + ly * cos;
            if (Math.Abs(rx) <= w / 2.0 + 2 && Math.Abs(ry) <= h / 2.0 + 2)
            {
                return PaintFrameAction.Move;
            }
            return PaintFrameAction.None;
        }
        if (paintTool != PaintTool.Shape)
        {
            return PaintFrameAction.None;
        }
        var rotateHandle = ShapeFrameRotateHandle();
        if (Distance(screen, rotateHandle) < 18)
        {
            return PaintFrameAction.Rotate;
        }
        var corners = ShapeFrameCorners();
        for (var i = 0; i < 4; i++)
        {
            if (Distance(screen, PaintToScreen(corners[i])) < 16)
            {
                handle = i;
                return PaintFrameAction.Resize;
            }
        }
        for (var edge = 0; edge < 4; edge++)
        {
            if (DistanceToSegment(screen, PaintToScreen(corners[edge]), PaintToScreen(corners[(edge + 1) % 4])) < 12)
            {
                handle = 4 + edge;
                return PaintFrameAction.Resize;
            }
        }
        if (PointInPolygon(screen, corners))
        {
            return PaintFrameAction.Move;
        }
        return PaintFrameAction.None;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        return Distance(p, new Point(a.X + dx * t, a.Y + dy * t));
    }

    private bool PointInPolygon(Point screen, Point[] corners)
    {
        var point = ScreenToPaint(screen);
        var radians = -shapeFrameRotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var lx = point.X - shapeFrameX;
        var ly = point.Y - shapeFrameY;
        var rx = lx * cos - ly * sin;
        var ry = lx * sin + ly * cos;
        return Math.Abs(rx) <= shapeFrameWidth / 2 && Math.Abs(ry) <= shapeFrameHeight / 2;
    }

    private void DrawRotatedShapePreview()
    {
        if (paintShapeBase is null)
        {
            paintShapeBase = (int[])paintPixels.Clone();
        }
        var preview = (int[])paintShapeBase.Clone();
        RasterizeRotatedShape(preview, paintShapeFilled);
        paintPixels = preview;
        UploadPaintPixels();
    }

    private void RasterizeRotatedShape(int[] buffer, bool filled)
    {
        var radians = shapeFrameRotation * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var halfW = shapeFrameWidth / 2;
        var halfH = shapeFrameHeight / 2;

        Point Rotate(double lx, double ly) =>
            new(shapeFrameX + lx * cos - ly * sin, shapeFrameY + lx * sin + ly * cos);

        if (paintShape == PaintShape.Ellipse && filled)
        {
            var left = (int)Math.Floor(shapeFrameX - halfW - halfH);
            var right = (int)Math.Ceiling(shapeFrameX + halfW + halfH);
            var top = (int)Math.Floor(shapeFrameY - halfW - halfH);
            var bottom = (int)Math.Ceiling(shapeFrameY + halfW + halfH);
            for (var y = Math.Max(top, 0); y <= Math.Min(bottom, paintHeight - 1); y++)
            {
                for (var x = Math.Max(left, 0); x <= Math.Min(right, paintWidth - 1); x++)
                {
                    var lx = x - shapeFrameX;
                    var ly = y - shapeFrameY;
                    var nx = (lx * cos + ly * sin) / Math.Max(halfW, 0.5);
                    var ny = (-lx * sin + ly * cos) / Math.Max(halfH, 0.5);
                    if (nx * nx + ny * ny <= 1)
                    {
                        var index = y * paintWidth + x;
                        buffer[index] = BlendPixel(buffer[index], paintColor);
                    }
                }
            }
            return;
        }

        if (paintShape == PaintShape.Rectangle && filled)
        {
            var corners = ShapeFrameCorners();
            var minX = (int)Math.Floor(corners.Min(p => p.X));
            var maxX = (int)Math.Ceiling(corners.Max(p => p.X));
            var minY = (int)Math.Floor(corners.Min(p => p.Y));
            var maxY = (int)Math.Ceiling(corners.Max(p => p.Y));
            for (var y = Math.Max(minY, 0); y <= Math.Min(maxY, paintHeight - 1); y++)
            {
                for (var x = Math.Max(minX, 0); x <= Math.Min(maxX, paintWidth - 1); x++)
                {
                    var lx = x - shapeFrameX;
                    var ly = y - shapeFrameY;
                    var rx = lx * cos + ly * sin;
                    var ry = -lx * sin + ly * cos;
                    if (Math.Abs(rx) <= halfW && Math.Abs(ry) <= halfH)
                    {
                        var index = y * paintWidth + x;
                        buffer[index] = BlendPixel(buffer[index], paintColor);
                    }
                }
            }
            return;
        }

        if (paintShape == PaintShape.Ellipse)
        {
            var perimeter = (int)(Math.PI * (halfW + halfH) * 2);
            for (var step = 0; step <= perimeter; step++)
            {
                var angle = step * 2 * Math.PI / Math.Max(perimeter, 1);
                var local = new Point(halfW * Math.Cos(angle), halfH * Math.Sin(angle));
                var rotated = Rotate(local.X, local.Y);
                StampOnto(buffer, (int)Math.Round(rotated.X), (int)Math.Round(rotated.Y));
            }
            return;
        }

        var rectCorners = ShapeFrameCorners();
        for (var edge = 0; edge < 4; edge++)
        {
            var a = rectCorners[edge];
            var b = rectCorners[(edge + 1) % 4];
            var steps = (int)Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            steps = Math.Max(steps, 1);
            for (var step = 0; step <= steps; step++)
            {
                var t = (double)step / steps;
                StampOnto(buffer, (int)Math.Round(a.X + (b.X - a.X) * t), (int)Math.Round(a.Y + (b.Y - a.Y) * t));
            }
        }
    }

    private void StampPaintShape()
    {
        PushPaintUndo();
        if (paintShapeBase is not null)
        {
            RasterizeRotatedShape(paintShapeBase, paintShapeFilled);
            paintPixels = (int[])paintShapeBase.Clone();
        }
        else
        {
            RasterizeRotatedShape(paintPixels, paintShapeFilled);
        }
        paintShapeBase = (int[])paintPixels.Clone();
        paintShapeStamped = true;
        paintingDirty = true;
        UploadPaintPixels();
    }

    private void FitPaintToView()
    {
        if (paintViewport is null || paintWidth <= 0 || paintHeight <= 0)
        {
            return;
        }
        var viewWidth = paintViewport.ActualWidth;
        var viewHeight = paintViewport.ActualHeight;
        if (viewWidth <= 0 || viewHeight <= 0)
        {
            return;
        }
        var targetZoom = Math.Clamp(Math.Min((viewWidth - 32) / paintWidth, (viewHeight - 32) / paintHeight), 0.1, 32);
        paintZoom = targetZoom;
        paintPan = new Point(
            (viewWidth - paintWidth * paintZoom) / 2,
            (viewHeight - paintHeight * paintZoom) / 2);
        RefreshPaintTransform();
    }

    private void ZoomPaintToCenter(double zoom)
    {
        if (paintViewport is null || paintViewport.ActualWidth <= 0 || paintViewport.ActualHeight <= 0)
        {
            ZoomPaint(zoom);
            return;
        }
        ZoomPaint(zoom, new Point(paintViewport.ActualWidth / 2, paintViewport.ActualHeight / 2));
    }

    private void ZoomPaint(double zoom, Point? center = null)
    {
        zoom = Math.Clamp(zoom, 0.1, 32);
        if (paintViewport is not null)
        {
            var point = center ?? new Point(
                paintViewport.ActualWidth > 0 ? paintViewport.ActualWidth / 2 : 0,
                paintViewport.ActualHeight > 0 ? paintViewport.ActualHeight / 2 : 0);
            var scale = zoom / paintZoom;
            paintPan = new Point(
                point.X - (point.X - paintPan.X) * scale,
                point.Y - (point.Y - paintPan.Y) * scale);
        }
        paintZoom = zoom;
        RefreshPaintTransform();
    }

    private void RebuildPaintTools()
    {
        if (paintToolsPanel is null)
        {
            return;
        }
        paintToolsPanel.Children.Clear();
        AddPaintToolButton(PaintTool.Brush, "brush", "Кисть (B)");
        AddPaintToolButton(PaintTool.Spray, "spray", "Спрей (S)");
        AddPaintToolButton(PaintTool.Eraser, "eraser", "Ластик (E)");
        AddPaintToolButton(PaintTool.Fill, "fillbucket", "Заливка (G)");
        AddPaintToolButton(PaintTool.Pipette, "pipette", "Пипетка (I)");
        AddPaintToolButton(PaintTool.Shape, "shape", "Фигура (U)");
        AddPaintToolButton(PaintTool.Line, "line", "Линия (L)");
        AddPaintToolButton(PaintTool.Text, "text", "Текст (T)");
        AddPaintToolButton(PaintTool.Hand, "handmove", "Рука (H)");
    }

    private void SelectPaintTool(PaintTool tool)
    {
        if (tool == PaintTool.Pipette)
        {
            paintToolBeforePipette = paintTool != PaintTool.Pipette ? paintTool : paintToolBeforePipette;
        }
        paintSprayTimer?.Stop();
        var leaving = paintTool;
        paintTool = tool;
        if (leaving == PaintTool.Text && tool != PaintTool.Text)
        {
            CommitPaintTextPreview();
        }
        if (tool == PaintTool.Text)
        {
            if (!paintTextPlaced)
            {
                paintTextX = Math.Max(0, paintWidth / 2.0 - 40);
                paintTextY = Math.Max(0, paintHeight / 2.0 - 20);
                paintTextRotation = 0;
                paintTextPlaced = true;
            }
            DrawPaintTextPreview();
        }
        if (tool == PaintTool.Shape)
        {
            if (!shapeFrameActive)
            {
                shapeFrameX = paintWidth / 2.0;
                shapeFrameY = paintHeight / 2.0;
                var side = Math.Max(8, Math.Min(paintWidth, paintHeight) / 2.0);
                shapeFrameWidth = side;
                shapeFrameHeight = side;
                shapeFrameRotation = 0;
                shapeFrameActive = true;
            }
            if (paintShapeBase is null)
            {
                paintShapeBase = (int[])paintPixels.Clone();
            }
            DrawRotatedShapePreview();
        }
        else
        {
            if (shapeFrameActive && paintShapeBase is not null)
            {
                if (!paintShapeStamped)
                {
                    PushPaintUndo();
                }
                paintShapeStamped = false;
                paintShapeBase = null;
                shapeFrameActive = false;
                paintingDirty = true;
            }
            else if (tool == PaintTool.Line)
            {
                paintShape = PaintShape.Line;
            }
        }
        RebuildPaintTools();
        RebuildPaintOptions();
        UpdatePaintFrame();
        UpdatePaintCursor();
    }

    private Border BuildPaintTextFloatCard()
    {
        var card = new Border
        {
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12, 14, 14),
            Width = 230,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 10, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18, Opacity = 0.25, ShadowDepth = 4, Direction = 270
            }
        };

        card.MouseLeftButtonDown += (_, e) => e.Handled = true;
        card.MouseMove += (_, e) => e.Handled = true;
        card.MouseLeftButtonUp += (_, e) => e.Handled = true;
        card.MouseWheel += (_, e) => e.Handled = true;

        var layout = new StackPanel { Orientation = Orientation.Vertical };

        var header = new TextBlock
        {
            Text = "✏  Текст",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFor("InkBrush"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        layout.Children.Add(header);

        paintTextInputBox = new TextBox
        {
            Style = (Style)FindResource("ProjectTextBox"),
            Text = paintText,
            AcceptsReturn = true,
            MinLines = 2,
            MaxLines = 4,
            MaxLength = 400,
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "Текст для штампа"
        };
        paintTextInputBox.TextChanged += (_, _) =>
        {
            paintText = paintTextInputBox!.Text;
            paintTextSeed = paintRandom.Next();
            InvalidatePaintText();
        };
        layout.Children.Add(paintTextInputBox);

        paintFontButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "Выбрать шрифт"
        };
        paintFontButton.Click += (_, _) => ShowPaintFontDialog();
        layout.Children.Add(paintFontButton);
        RefreshPaintFontButton();

        Border MakeDivider() => new()
        {
            Height = 1,
            Background = BrushFor("HairlineBrush"),
            Margin = new Thickness(0, 6, 0, 6)
        };
        TextBlock MakeLabel(string text) => new()
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = BrushFor("MutedBrush"),
            Margin = new Thickness(0, 0, 0, 3)
        };

        layout.Children.Add(MakeDivider());
        layout.Children.Add(MakeLabel("Кегль"));
        paintFontSizeSlider = CreateFloatSlider(8, 256, paintFontSize, value =>
        {
            paintFontSize = value;
            InvalidatePaintText();
        });
        layout.Children.Add(paintFontSizeSlider);

        layout.Children.Add(MakeLabel("Интервал"));
        layout.Children.Add(CreateFloatSlider(-100, 200, paintSpacing, value =>
        {
            paintSpacing = value;
            InvalidatePaintText();
        }));

        layout.Children.Add(MakeLabel("Странность"));
        layout.Children.Add(CreateFloatSlider(0, 100, paintWeirdness, value =>
        {
            paintWeirdness = value;
            paintTextSeed = paintRandom.Next();
            InvalidatePaintText();
        }));

        layout.Children.Add(MakeDivider());
        layout.Children.Add(MakeLabel("Цвет текста"));
        paintTextSwatch = CreatePaintSwatch("Цвет текста", paintTextColor, color =>
        {
            paintTextColor = color;
            RefreshPaintTextSwatches();
            InvalidatePaintText();
        });
        layout.Children.Add(paintTextSwatch);

        layout.Children.Add(MakeDivider());
        var outlineCheck = new CheckBox
        {
            Content = "Обводка",
            IsChecked = paintOutlineOn,
            Foreground = BrushFor("BodyBrush"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        };
        outlineCheck.Checked   += (_, _) => { paintOutlineOn = true;  RefreshPaintOutlineControls(); InvalidatePaintText(); };
        outlineCheck.Unchecked += (_, _) => { paintOutlineOn = false; RefreshPaintOutlineControls(); InvalidatePaintText(); };
        layout.Children.Add(outlineCheck);

        paintOutlineWidthSlider = new StackPanel();
        paintOutlineWidthSlider.Children.Add(MakeLabel("Ширина обводки"));
        paintOutlineWidthSlider.Children.Add(CreateFloatSlider(1, 24, paintOutlineWidth, value =>
        {
            paintOutlineWidth = value;
            InvalidatePaintText();
        }));
        paintOutlineSwatch = CreatePaintSwatch("Цвет обводки", paintOutlineColor, color =>
        {
            paintOutlineColor = color;
            RefreshPaintTextSwatches();
            InvalidatePaintText();
        });
        paintOutlineWidthSlider.Children.Add(paintOutlineSwatch);
        layout.Children.Add(paintOutlineWidthSlider);
        RefreshPaintOutlineControls();

        layout.Children.Add(MakeDivider());
        var stampBtn = new Button
        {
            Style = (Style)FindResource("PrimaryButton"),
            Content = "✓  Поставить текст (Enter)",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 7, 0, 7),
            ToolTip = "Нанести текст на холст (Enter)"
        };
        stampBtn.Click += (_, _) => StampPaintText();
        AutomationProperties.SetName(stampBtn, "Поставить текст");
        layout.Children.Add(stampBtn);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 520,
            Content = layout
        };
        card.Child = scroll;
        return card;
    }

    private Slider CreateFloatSlider(double min, double max, double current, Action<double> onChange)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = current,
            Margin = new Thickness(0, 0, 0, 6),
            TickFrequency = 1,
            SmallChange = 1,
            LargeChange = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        slider.ValueChanged += (_, e) => onChange(e.NewValue);
        return slider;
    }

    private void AddPaintToolButton(PaintTool tool, string icon, string label)
    {
        var selected = paintTool == tool;
        var button = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Width = 44,
            Height = 44,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 2, 0, 2),
            ToolTip = label,
            Background = selected ? BrushFor("SelectedBrush") : Brushes.Transparent,
            BorderBrush = selected ? BrushFor("AccentBrush") : Brushes.Transparent,
            BorderThickness = new Thickness(selected ? 1.5 : 0)
        };
        var iconElement = CreateSheetIcon(icon, 22, BrushFor(selected ? "AccentBrush" : "BodyBrush"));
        button.Content = iconElement;
        button.Click += (_, _) => SelectPaintTool(tool);
        AutomationProperties.SetName(button, label);
        paintToolsPanel!.Children.Add(button);
    }

    private void RebuildPaintOptions()
    {
        if (paintOptionsPanel is null)
        {
            return;
        }
        paintOptionsPanel.Children.Clear();
        paintShapeRow = null;
        paintColorSwatch = null;

        if (paintTool != PaintTool.Text && paintTextFloatCard is not null && paintViewport is Grid vpRemove)
        {
            vpRemove.Children.Remove(paintTextFloatCard);
            paintTextFloatCard = null;
        }
        if (paintTool == PaintTool.Text && paintTextFloatCard is null && paintViewport is Grid vpAdd)
        {
            paintTextFloatCard = BuildPaintTextFloatCard();
            Panel.SetZIndex(paintTextFloatCard, 150);
            vpAdd.Children.Add(paintTextFloatCard);
        }

        switch (paintTool)
        {
            case PaintTool.Brush:
                paintOptionsPanel.Children.Add(CreateToolBadge("brush", "Кисть"));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(CreateSizeSliderGroup("Размер:", 1, 64, paintSize, [1, 2, 4, 8, 12, 16, 24, 32, 48], value =>
                {
                    paintSize = value;
                    UpdatePaintSizeLabel();
                    UpdatePaintCursor();
                }));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(CreateColorRow());
                break;
            case PaintTool.Spray:
                paintOptionsPanel.Children.Add(CreateToolBadge("spray", "Спрей"));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(CreateSizeSliderGroup("Радиус:", 1, 64, paintSize, [1, 2, 4, 8, 12, 16, 24, 32, 48], value =>
                {
                    paintSize = value;
                    UpdatePaintSizeLabel();
                    UpdatePaintCursor();
                }));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(CreateSizeSliderGroup("Плотность:", 5, 100, paintSprayDensity, [10, 30, 60, 100], value =>
                {
                    paintSprayDensity = value;
                }));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(CreateColorRow());
                break;
            case PaintTool.Eraser:
                paintOptionsPanel.Children.Add(CreateToolBadge("eraser", "Ластик"));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(CreateSizeSliderGroup("Размер:", 1, 64, paintSize, [1, 2, 4, 8, 12, 16, 24, 32, 48], value =>
                {
                    paintSize = value;
                    UpdatePaintSizeLabel();
                    UpdatePaintCursor();
                }));
                break;
            case PaintTool.Fill:
                paintOptionsPanel.Children.Add(CreateToolBadge("fillbucket", "Заливка"));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(CreateToleranceSliderGroup("Допуск:", 0, 200, paintTolerance, [0, 16, 32, 64, 128], value =>
                {
                    paintTolerance = value;
                    UpdatePaintSizeLabel();
                }));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(CreateColorRow());
                break;
            case PaintTool.Pipette:
                paintOptionsPanel.Children.Add(CreateToolBadge("pipette", "Пипетка"));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(new TextBlock
                {
                    Text = "Кликните по холсту, чтобы взять цвет (автовозврат к предыдущему инструменту)",
                    FontSize = 12,
                    Foreground = BrushFor("MutedBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                break;
            case PaintTool.Shape:
                paintOptionsPanel.Children.Add(CreateToolBadge("shape", "Фигура"));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintShapeRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                AddPaintShapeButton(PaintShape.Rectangle, "shape", "Прямоугольник");
                AddPaintShapeButton(PaintShape.Ellipse, "ellipse", "Эллипс");
                paintOptionsPanel.Children.Add(paintShapeRow);
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                var fillCheck = new CheckBox
                {
                    Content = "Заливка",
                    IsChecked = paintShapeFilled,
                    Foreground = BrushFor("BodyBrush"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                fillCheck.Checked += (_, _) => { paintShapeFilled = true; DrawRotatedShapePreview(); UpdatePaintFrame(); };
                fillCheck.Unchecked += (_, _) => { paintShapeFilled = false; DrawRotatedShapePreview(); UpdatePaintFrame(); };
                paintOptionsPanel.Children.Add(fillCheck);
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(CreateSizeSliderGroup("Толщина:", 1, 64, paintSize, [1, 2, 4, 8, 16], value =>
                {
                    paintSize = value;
                    UpdatePaintSizeLabel();
                    if (paintTool == PaintTool.Shape)
                    {
                        DrawRotatedShapePreview();
                    }
                    UpdatePaintFrame();
                }));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(CreateColorRow());
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintShapeSizeLabel = new TextBlock
                {
                    Text = $"{(int)Math.Round(shapeFrameWidth)}×{(int)Math.Round(shapeFrameHeight)}",
                    FontSize = 11,
                    FontWeight = FontWeights.Medium,
                    Foreground = BrushFor("MutedBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                paintOptionsPanel.Children.Add(paintShapeSizeLabel);
                var stampButton = new Button
                {
                    Style = (Style)FindResource("PrimaryButton"),
                    Content = "✓  Поставить фигуру (Enter)",
                    FontSize = 12,
                    Padding = new Thickness(10, 4, 10, 4),
                    ToolTip = "Нанести фигуру на холст (Enter)"
                };
                stampButton.Click += (_, _) => StampPaintShape();
                AutomationProperties.SetName(stampButton, "Поставить фигуру");
                paintOptionsPanel.Children.Add(stampButton);
                break;
            case PaintTool.Text:
                paintOptionsPanel.Children.Add(CreateToolBadge("text", "Текст"));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(new TextBlock
                {
                    Text = "Панель настроек текста справа →",
                    FontSize = 12,
                    Foreground = BrushFor("MutedBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                var textStampButton = new Button
                {
                    Style = (Style)FindResource("PrimaryButton"),
                    Content = "✓  Поставить текст (Enter)",
                    FontSize = 12,
                    Padding = new Thickness(10, 4, 10, 4),
                    ToolTip = "Нанести текст на холст (Enter)"
                };
                textStampButton.Click += (_, _) => StampPaintText();
                AutomationProperties.SetName(textStampButton, "Поставить текст");
                paintOptionsPanel.Children.Add(textStampButton);
                break;
            case PaintTool.Line:
                paintOptionsPanel.Children.Add(CreateToolBadge("line", "Линия"));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(CreateSizeSliderGroup("Толщина:", 1, 64, paintSize, [1, 2, 4, 8, 16], value =>
                {
                    paintSize = value;
                    UpdatePaintSizeLabel();
                }));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(CreateColorRow());
                break;
            default:
                paintOptionsPanel.Children.Add(CreateToolBadge("handmove", "Рука"));
                paintOptionsPanel.Children.Add(CreateOptionSeparator());
                paintOptionsPanel.Children.Add(new TextBlock
                {
                    Text = "Перетаскивайте холст мышью • Также работает Пробел+ЛКМ или колесо мыши",
                    FontSize = 12,
                    Foreground = BrushFor("MutedBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                break;
        }
    }

    private StackPanel CreateToolBadge(string icon, string label)
    {
        var badge = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        badge.Children.Add(CreateSheetIcon(icon, 18, BrushFor("AccentBrush")));
        badge.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFor("InkBrush"),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        return badge;
    }

    private Border CreateOptionSeparator() => new()
    {
        Width = 1,
        Height = 18,
        Background = BrushFor("HairlineBrush"),
        Margin = new Thickness(10, 0, 10, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private void AddPaintShapeButton(PaintShape shape, string icon, string label)
    {
        var selected = paintShape == shape;
        var button = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(2, 0, 2, 0),
            ToolTip = label,
            Background = selected ? BrushFor("SelectedBrush") : Brushes.Transparent,
            BorderBrush = selected ? BrushFor("AccentBrush") : Brushes.Transparent,
            BorderThickness = new Thickness(selected ? 1 : 0)
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(CreateSheetIcon(icon, 16, BrushFor(selected ? "AccentBrush" : "BodyBrush")));
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = BrushFor(selected ? "AccentBrush" : "BodyBrush"),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        button.Content = row;
        button.Click += (_, _) =>
        {
            paintShape = shape;
            RebuildPaintOptions();
            DrawRotatedShapePreview();
            UpdatePaintFrame();
        };
        AutomationProperties.SetName(button, label);
        paintShapeRow!.Children.Add(button);
    }

    private StackPanel CreateSizeSliderGroup(string label, double min, double max, double value, double[] quickSizes, Action<double> set)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = BrushFor("BodyBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        var valueLabel = new TextBlock
        {
            Text = $"{(int)value} px",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = BrushFor("InkBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 36,
            Margin = new Thickness(0, 0, 6, 0)
        };

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            Width = 90,
            VerticalAlignment = VerticalAlignment.Center
        };

        var chipsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };

        void UpdateActiveChip(double currentVal)
        {
            foreach (UIElement child in chipsPanel.Children)
            {
                if (child is Button chipBtn && chipBtn.Tag is double s)
                {
                    var isCurrent = Math.Abs(currentVal - s) < 0.5;
                    chipBtn.Background = isCurrent ? BrushFor("AccentSoftBrush") : BrushFor("PanelBrush");
                    chipBtn.BorderBrush = isCurrent ? BrushFor("AccentBrush") : BrushFor("HairlineBrush");
                    chipBtn.BorderThickness = new Thickness(1);
                    if (chipBtn.Content is TextBlock tb)
                    {
                        tb.Foreground = isCurrent ? BrushFor("AccentBrush") : BrushFor("BodyBrush");
                    }
                }
            }
        }

        slider.ValueChanged += (_, _) =>
        {
            set(slider.Value);
            valueLabel.Text = $"{(int)slider.Value} px";
            UpdateActiveChip(slider.Value);
        };

        foreach (var s in quickSizes)
        {
            var isCurrent = Math.Abs(value - s) < 0.5;
            var chip = new Button
            {
                Style = (Style)FindResource("ToolButton"),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(2, 0, 2, 0),
                Tag = s,
                Background = isCurrent ? BrushFor("AccentSoftBrush") : BrushFor("PanelBrush"),
                BorderBrush = isCurrent ? BrushFor("AccentBrush") : BrushFor("HairlineBrush"),
                BorderThickness = new Thickness(1),
                Content = new TextBlock
                {
                    Text = s.ToString(),
                    FontSize = 11,
                    Foreground = isCurrent ? BrushFor("AccentBrush") : BrushFor("BodyBrush")
                },
                ToolTip = $"Размер {s} px"
            };
            chip.Click += (_, _) =>
            {
                slider.Value = s;
                set(s);
                valueLabel.Text = $"{(int)s} px";
                UpdateActiveChip(s);
            };
            chipsPanel.Children.Add(chip);
        }

        panel.Children.Add(slider);
        panel.Children.Add(valueLabel);
        panel.Children.Add(chipsPanel);
        return panel;
    }

    private StackPanel CreateToleranceSliderGroup(string label, double min, double max, double value, double[] quickTols, Action<double> set)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = BrushFor("BodyBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        var valueLabel = new TextBlock
        {
            Text = ((int)value).ToString(),
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = BrushFor("InkBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 28,
            Margin = new Thickness(0, 0, 6, 0)
        };

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            Width = 80,
            VerticalAlignment = VerticalAlignment.Center
        };

        var chipsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };

        void UpdateActiveChip(double currentVal)
        {
            foreach (UIElement child in chipsPanel.Children)
            {
                if (child is Button chipBtn && chipBtn.Tag is double s)
                {
                    var isCurrent = Math.Abs(currentVal - s) < 0.5;
                    chipBtn.Background = isCurrent ? BrushFor("AccentSoftBrush") : BrushFor("PanelBrush");
                    chipBtn.BorderBrush = isCurrent ? BrushFor("AccentBrush") : BrushFor("HairlineBrush");
                    chipBtn.BorderThickness = new Thickness(1);
                    if (chipBtn.Content is TextBlock tb)
                    {
                        tb.Foreground = isCurrent ? BrushFor("AccentBrush") : BrushFor("BodyBrush");
                    }
                }
            }
        }

        slider.ValueChanged += (_, _) =>
        {
            set(slider.Value);
            valueLabel.Text = ((int)slider.Value).ToString();
            UpdateActiveChip(slider.Value);
        };

        foreach (var s in quickTols)
        {
            var isCurrent = Math.Abs(value - s) < 0.5;
            var chip = new Button
            {
                Style = (Style)FindResource("ToolButton"),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(2, 0, 2, 0),
                Tag = s,
                Background = isCurrent ? BrushFor("AccentSoftBrush") : BrushFor("PanelBrush"),
                BorderBrush = isCurrent ? BrushFor("AccentBrush") : BrushFor("HairlineBrush"),
                BorderThickness = new Thickness(1),
                Content = new TextBlock
                {
                    Text = s.ToString(),
                    FontSize = 11,
                    Foreground = isCurrent ? BrushFor("AccentBrush") : BrushFor("BodyBrush")
                },
                ToolTip = $"Допуск {s}"
            };
            chip.Click += (_, _) =>
            {
                slider.Value = s;
                set(s);
                valueLabel.Text = ((int)s).ToString();
                UpdateActiveChip(s);
            };
            chipsPanel.Children.Add(chip);
        }

        panel.Children.Add(slider);
        panel.Children.Add(valueLabel);
        panel.Children.Add(chipsPanel);
        return panel;
    }

    private StackPanel CreateColorRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = "Цвет:",
            FontSize = 12,
            Foreground = BrushFor("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        paintColorSwatch = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(paintColor),
            BorderBrush = BrushFor("AccentBrush"),
            BorderThickness = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center
        };
        var swatchButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Padding = new Thickness(0),
            Content = paintColorSwatch,
            ToolTip = "Выбор цвета (кликните для палитры)"
        };
        swatchButton.Click += (_, _) => ShowPaintColorDialog();
        row.Children.Add(swatchButton);
        return row;
    }

    private void UpdateColorSwatchBorder()
    {
        if (paintColorSwatch is not null)
        {
            paintColorSwatch.Background = new SolidColorBrush(paintColor);
        }
        if (paintRailColorSwatch is not null)
        {
            paintRailColorSwatch.Background = new SolidColorBrush(paintColor);
        }
    }

    private Color paintDialogColor = Colors.Black;
    private static readonly List<Color> PaintColorHistory = [];
    private Border? paintDialogNewPreview;
    private TextBox? paintDialogHexBox;
    private StackPanel? paintDialogTabContent;
    private string paintDialogTab = "hsv2d";
    private LinearGradientBrush? paintDialogAlphaBrush;
    private Image? paintHsv2dImage;
    private Canvas? paintHsv2dCanvas;
    private System.Windows.Shapes.Ellipse? paintHsv2dCursor;
    private Image? paintHueImage;
    private Canvas? paintHueCanvas;
    private System.Windows.Shapes.Rectangle? paintHueCursor;
    private double paintDialogHue;
    private double paintDialogSat;
    private double paintDialogVal;

    private Action<Color>? paintDialogApply;
    private bool paintDialogPipette;

    private void ShowPaintColorDialog() => ShowPaintColorDialog(paintColor, SetPaintColor, showPipette: true);

    private void ShowPaintColorDialog(Color initial, Action<Color> apply, bool showPipette)
    {
        paintDialogColor = initial;
        paintDialogApply = apply;
        paintDialogPipette = showPipette;
        var overlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0))
        };
        Panel.SetZIndex(overlay, 40);
        var card = new Border
        {
            Width = 360,
            Padding = new Thickness(20),
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var layout = new StackPanel();
        layout.Children.Add(new TextBlock
        {
            Text = "Цвет",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFor("InkBrush")
        });

        var previewRow = new Grid { Margin = new Thickness(0, 14, 0, 6) };
        previewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        previewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        previewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        previewRow.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Текущий", FontSize = 12, Foreground = BrushFor("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center },
                new Border { Height = 44, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(initial), BorderBrush = BrushFor("HairlineBrush"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 6, 0, 0) }
            }
        });
        paintDialogNewPreview = new Border
        {
            Height = 44,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(paintDialogColor),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 6, 0, 0)
        };
        var newStack = new StackPanel();
        newStack.Children.Add(new TextBlock { Text = "Новый", FontSize = 12, Foreground = BrushFor("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        newStack.Children.Add(paintDialogNewPreview);
        Grid.SetColumn(newStack, 2);
        previewRow.Children.Add(newStack);
        layout.Children.Add(previewRow);

        var pipetteButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = "Пипетка",
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        pipetteButton.Click += (_, _) =>
        {
            WorkspaceContentHost.Children.Remove(overlay);
            paintTool = PaintTool.Pipette;
            RebuildPaintTools();
            RebuildPaintOptions();
        };
        pipetteButton.Visibility = paintDialogPipette ? Visibility.Visible : Visibility.Collapsed;
        layout.Children.Add(pipetteButton);

        layout.Children.Add(new TextBlock
        {
            Text = "Недавние",
            FontSize = 12,
            Foreground = BrushFor("MutedBrush"),
            Margin = new Thickness(0, 12, 0, 6)
        });
        var history = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
        if (PaintColorHistory.Count == 0)
        {
            history.Children.Add(new TextBlock { Text = "Пока пусто", FontSize = 12, Foreground = BrushFor("MutedBrush") });
        }
        foreach (var historyColor in PaintColorHistory)
        {
            var captured = historyColor;
            var dot = new Button
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(captured),
                BorderBrush = BrushFor("HairlineBrush"),
                BorderThickness = new Thickness(1),
                ToolTip = "Выбрать цвет"
            };
            dot.Click += (_, _) => SetPaintDialogColor(captured);
            history.Children.Add(dot);
        }
        layout.Children.Add(history);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 8) };
        foreach (var (id, label) in new[] { ("hsv2d", "HSV"), ("presets", "Пресеты"), ("rgb", "RGB") })
        {
            var tabButton = new Button
            {
                Style = (Style)FindResource("ToolButton"),
                Content = label,
                Padding = new Thickness(16, 6, 16, 6),
                Margin = new Thickness(2, 0, 2, 0),
                FontWeight = paintDialogTab == id ? FontWeights.SemiBold : FontWeights.Regular,
                Foreground = BrushFor(paintDialogTab == id ? "AccentBrush" : "BodyBrush")
            };
            tabButton.Click += (_, _) =>
            {
                paintDialogTab = id;
                RebuildPaintDialogTab();
                foreach (var child in tabs.Children)
                {
                    if (child is Button other && other != tabButton)
                    {
                        other.FontWeight = FontWeights.Regular;
                        other.Foreground = BrushFor("BodyBrush");
                    }
                }
                tabButton.FontWeight = FontWeights.SemiBold;
                tabButton.Foreground = BrushFor("AccentBrush");
            };
            tabs.Children.Add(tabButton);
        }
        layout.Children.Add(tabs);
        paintDialogTabContent = new StackPanel();
        layout.Children.Add(paintDialogTabContent);
        RebuildPaintDialogTab();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancelButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Content = "Отмена",
            Padding = new Thickness(14, 9, 14, 9)
        };
        var okButton = new Button
        {
            Style = (Style)FindResource("PrimaryButton"),
            Content = "Выбрать",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(22, 9, 22, 9)
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);
        layout.Children.Add(buttons);
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 680,
            Content = layout
        };
        ApplyThinScrollBars(scrollViewer);
        card.Child = scrollViewer;
        var cardScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxHeight = SystemParameters.PrimaryScreenHeight - 80,
            Content = card
        };
        overlay.Children.Add(cardScroll);
        overlay.MouseDown += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, overlay))
            {
                WorkspaceContentHost.Children.Remove(overlay);
            }
        };
        cancelButton.Click += (_, _) => WorkspaceContentHost.Children.Remove(overlay);
        okButton.Click += (_, _) =>
        {
            paintDialogApply?.Invoke(paintDialogColor);
            if (PaintColorHistory.Count == 0 || PaintColorHistory[0] != paintDialogColor)
            {
                PaintColorHistory.Insert(0, paintDialogColor);
                while (PaintColorHistory.Count > 12)
                {
                    PaintColorHistory.RemoveAt(PaintColorHistory.Count - 1);
                }
            }
            WorkspaceContentHost.Children.Remove(overlay);
        };
        WorkspaceContentHost.Children.Add(overlay);
    }

    private void RebuildPaintDialogTab()
    {
        if (paintDialogTabContent is null)
        {
            return;
        }
        paintDialogTabContent.Children.Clear();
        switch (paintDialogTab)
        {
            case "rgb":
                BuildPaintDialogRgb(paintDialogTabContent);
                break;
            case "hsv2d":
                BuildPaintDialogHsv2d(paintDialogTabContent);
                break;
            case "presets":
                var presets = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 392 };
                foreach (var preset in PaintPalettePresets)
                {
                    var captured = preset;
                    var isTransp = captured.A == 0;
                    var dot = new Border
                    {
                        Width = 36,
                        Height = 36,
                        Margin = new Thickness(4),
                        CornerRadius = new CornerRadius(6),
                        BorderBrush = BrushFor("HairlineBrush"),
                        BorderThickness = new Thickness(1.5),
                        Cursor = Cursors.Hand,
                        ToolTip = isTransp ? "Прозрачный" : $"#{captured.R:X2}{captured.G:X2}{captured.B:X2}"
                    };
                    if (isTransp)
                    {
                        var cg = new GeometryGroup();
                        cg.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
                        cg.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));
                        var cd = new DrawingGroup();
                        cd.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
                        cd.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(180, 180, 180)), null, cg));
                        dot.Background = new DrawingBrush { Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute, TileMode = TileMode.Tile, Drawing = cd };
                    }
                    else
                    {
                        dot.Background = new SolidColorBrush(captured);
                    }
                    dot.MouseLeftButtonDown += (_, _) => SetPaintDialogColor(Color.FromArgb(captured.A, captured.R, captured.G, captured.B));
                    presets.Children.Add(dot);
                }
                paintDialogTabContent.Children.Add(presets);
                break;
            default:
                break;
        }
        paintDialogTabContent.Children.Add(CreatePaintDialogAlpha());
        paintDialogHexBox = new TextBox
        {
            Style = (Style)FindResource("ProjectTextBox"),
            Text = $"#{paintDialogColor.R:X2}{paintDialogColor.G:X2}{paintDialogColor.B:X2}",
            MaxLength = 9,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            ToolTip = "HEX, например #A9583E"
        };
        paintDialogHexBox.TextChanged += (_, _) =>
        {
            var text = paintDialogHexBox!.Text.Trim().TrimStart('#');
            if ((text.Length == 6 || text.Length == 8) &&
                uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value))
            {
                var color = text.Length == 6
                    ? Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value)
                    : Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
                paintDialogColor = color;
                RefreshPaintDialogPreview();
            }
        };
        paintDialogTabContent.Children.Add(paintDialogHexBox);
    }

    private StackPanel CreatePaintDialogAlpha()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(new TextBlock { Text = "Прозрачность", FontSize = 13, Foreground = BrushFor("BodyBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        paintDialogAlphaBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5)
        };
        RefreshPaintDialogAlphaBrush();
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 255,
            Value = paintDialogColor.A,
            Width = 200,
            VerticalAlignment = VerticalAlignment.Center,
            Background = paintDialogAlphaBrush
        };
        var valueLabel = new TextBlock
        {
            Text = paintDialogColor.A.ToString(),
            FontSize = 13,
            Foreground = BrushFor("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            MinWidth = 32
        };
        slider.ValueChanged += (_, _) =>
        {
            var color = paintDialogColor;
            paintDialogColor = Color.FromArgb((byte)slider.Value, color.R, color.G, color.B);
            valueLabel.Text = ((int)slider.Value).ToString();
            RefreshPaintDialogAlphaBrush();
            RefreshPaintDialogPreview();
        };
        row.Children.Add(slider);
        row.Children.Add(valueLabel);
        return row;
    }

    private void RefreshPaintDialogAlphaBrush()
    {
        if (paintDialogAlphaBrush is null)
        {
            return;
        }
        paintDialogAlphaBrush.GradientStops.Clear();
        paintDialogAlphaBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, paintDialogColor.R, paintDialogColor.G, paintDialogColor.B), 0));
        paintDialogAlphaBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, paintDialogColor.R, paintDialogColor.G, paintDialogColor.B), 1));
    }

    private void BuildPaintDialogRgb(StackPanel parent)
    {
        parent.Children.Add(CreatePaintDialogChannel("R", paintDialogColor.R, value =>
            paintDialogColor = Color.FromArgb(paintDialogColor.A, value, paintDialogColor.G, paintDialogColor.B)));
        parent.Children.Add(CreatePaintDialogChannel("G", paintDialogColor.G, value =>
            paintDialogColor = Color.FromArgb(paintDialogColor.A, paintDialogColor.R, value, paintDialogColor.B)));
        parent.Children.Add(CreatePaintDialogChannel("B", paintDialogColor.B, value =>
            paintDialogColor = Color.FromArgb(paintDialogColor.A, paintDialogColor.R, paintDialogColor.G, value)));
    }

    private StackPanel CreatePaintDialogChannel(string label, byte value, Action<byte> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = BrushFor("BodyBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), MinWidth = 14 });
        var valueLabel = new TextBlock
        {
            Text = value.ToString(),
            FontSize = 13,
            Foreground = BrushFor("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            MinWidth = 32
        };
        var slider = new Slider { Minimum = 0, Maximum = 255, Value = value, Width = 200, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, _) =>
        {
            set((byte)slider.Value);
            valueLabel.Text = ((int)slider.Value).ToString();
            RefreshPaintDialogAlphaBrush();
            RefreshPaintDialogPreview();
        };
        row.Children.Add(slider);
        row.Children.Add(valueLabel);
        return row;
    }

    private void BuildPaintDialogHsv2d(StackPanel parent)
    {
        RgbToHsv(paintDialogColor, out paintDialogHue, out paintDialogSat, out paintDialogVal);

        const double svSize = 260;
        const double hueW = 24;
        const double gap = 10;

        var container = new Grid { Margin = new Thickness(0, 10, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(svSize) });
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(hueW) });

        var svHolder = new Grid { Width = svSize, Height = svSize, ClipToBounds = true };
        paintHsv2dImage = new Image { Width = svSize, Height = svSize, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Cross };
        RenderOptions.SetBitmapScalingMode(paintHsv2dImage, BitmapScalingMode.NearestNeighbor);
        RedrawHsv2dCanvas();
        paintHsv2dCanvas = new Canvas { Width = svSize, Height = svSize, IsHitTestVisible = false };
        paintHsv2dCursor = new System.Windows.Shapes.Ellipse
        {
            Width = 14, Height = 14,
            Stroke = Brushes.White, StrokeThickness = 2,
            IsHitTestVisible = false
        };
        paintHsv2dCanvas.Children.Add(paintHsv2dCursor);
        svHolder.Children.Add(paintHsv2dImage);
        svHolder.Children.Add(paintHsv2dCanvas);
        UpdateHsv2dCursorPos(svSize);

        paintHsv2dImage.MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(paintHsv2dImage);
            paintDialogSat = Math.Clamp(p.X / svSize, 0, 1);
            paintDialogVal = Math.Clamp(1 - p.Y / svSize, 0, 1);
            paintDialogColor = HsvToColor(paintDialogHue, paintDialogSat, paintDialogVal, paintDialogColor.A);
            UpdateHsv2dCursorPos(svSize);
            RefreshPaintDialogAlphaBrush();
            RefreshPaintDialogPreview();
            paintHsv2dImage.CaptureMouse();
        };
        paintHsv2dImage.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(paintHsv2dImage);
            paintDialogSat = Math.Clamp(p.X / svSize, 0, 1);
            paintDialogVal = Math.Clamp(1 - p.Y / svSize, 0, 1);
            paintDialogColor = HsvToColor(paintDialogHue, paintDialogSat, paintDialogVal, paintDialogColor.A);
            UpdateHsv2dCursorPos(svSize);
            RefreshPaintDialogAlphaBrush();
            RefreshPaintDialogPreview();
        };
        paintHsv2dImage.MouseLeftButtonUp += (_, _) => paintHsv2dImage.ReleaseMouseCapture();
        Grid.SetColumn(svHolder, 0);
        container.Children.Add(svHolder);

        var hueHolder = new Grid { Width = hueW, Height = svSize, ClipToBounds = true };
        paintHueImage = new Image { Width = hueW, Height = svSize, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand };
        RenderOptions.SetBitmapScalingMode(paintHueImage, BitmapScalingMode.NearestNeighbor);
        RedrawHueCanvas(svSize);
        paintHueCanvas = new Canvas { Width = hueW, Height = svSize, IsHitTestVisible = false };
        paintHueCursor = new System.Windows.Shapes.Rectangle
        {
            Width = hueW, Height = 4,
            Stroke = Brushes.White, StrokeThickness = 1.5,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        paintHueCanvas.Children.Add(paintHueCursor);
        hueHolder.Children.Add(paintHueImage);
        hueHolder.Children.Add(paintHueCanvas);
        UpdateHueCursorPos(svSize);

        paintHueImage.MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(paintHueImage);
            paintDialogHue = Math.Clamp(360 - p.Y / svSize * 360, 0, 360);
            paintDialogColor = HsvToColor(paintDialogHue, paintDialogSat, paintDialogVal, paintDialogColor.A);
            RedrawHsv2dCanvas();
            UpdateHueCursorPos(svSize);
            RefreshPaintDialogAlphaBrush();
            RefreshPaintDialogPreview();
            paintHueImage.CaptureMouse();
        };
        paintHueImage.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(paintHueImage);
            paintDialogHue = Math.Clamp(360 - p.Y / svSize * 360, 0, 360);
            paintDialogColor = HsvToColor(paintDialogHue, paintDialogSat, paintDialogVal, paintDialogColor.A);
            RedrawHsv2dCanvas();
            UpdateHueCursorPos(svSize);
            RefreshPaintDialogAlphaBrush();
            RefreshPaintDialogPreview();
        };
        paintHueImage.MouseLeftButtonUp += (_, _) => paintHueImage.ReleaseMouseCapture();
        Grid.SetColumn(hueHolder, 2);
        container.Children.Add(hueHolder);

        parent.Children.Add(container);
    }

    private void RedrawHsv2dCanvas()
    {
        if (paintHsv2dImage is null) return;
        const int w = 260, h = 260;
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new int[w * h];
        for (var y = 0; y < h; y++)
        {
            var val = 1.0 - (double)y / h;
            for (var x = 0; x < w; x++)
            {
                var sat = (double)x / w;
                var c = HsvToColor(paintDialogHue, sat, val, 255);
                pixels[y * w + x] = (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;
            }
        }
        bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        paintHsv2dImage.Source = bmp;
    }

    private void RedrawHueCanvas(double h)
    {
        if (paintHueImage is null) return;
        const int w = 24;
        var hi = (int)h;
        var bmp = new WriteableBitmap(w, hi, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new int[w * hi];
        for (var y = 0; y < hi; y++)
        {
            var hue = 360.0 - (double)y / hi * 360.0;
            var c = HsvToColor(hue, 1, 1, 255);
            var packed = (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;
            for (var x = 0; x < w; x++) pixels[y * w + x] = packed;
        }
        bmp.WritePixels(new Int32Rect(0, 0, w, hi), pixels, w * 4, 0);
        paintHueImage.Source = bmp;
    }

    private void UpdateHsv2dCursorPos(double svSize)
    {
        if (paintHsv2dCursor is null) return;
        var cx = paintDialogSat * svSize - 7;
        var cy = (1 - paintDialogVal) * svSize - 7;
        Canvas.SetLeft(paintHsv2dCursor, cx);
        Canvas.SetTop(paintHsv2dCursor, cy);
        var bg = HsvToColor(paintDialogHue, paintDialogSat, paintDialogVal, 255);
        var lum = 0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B;
        paintHsv2dCursor.Stroke = lum > 128 ? Brushes.Black : Brushes.White;
    }

    private void UpdateHueCursorPos(double svSize)
    {
        if (paintHueCursor is null) return;
        var y = (1 - paintDialogHue / 360.0) * svSize - 2;
        Canvas.SetLeft(paintHueCursor, 0);
        Canvas.SetTop(paintHueCursor, y);
    }

    private StackPanel CreatePaintDialogGradientChannel(string label, double value, double min, double max, LinearGradientBrush track, Action<double> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = BrushFor("BodyBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), MinWidth = 52 });
        var valueLabel = new TextBlock
        {
            Text = ((int)value).ToString(),
            FontSize = 13,
            Foreground = BrushFor("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            MinWidth = 32
        };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 200, VerticalAlignment = VerticalAlignment.Center, Background = track };
        slider.ValueChanged += (_, _) =>
        {
            set(slider.Value);
            valueLabel.Text = ((int)slider.Value).ToString();
            RefreshPaintDialogAlphaBrush();
            RefreshPaintDialogPreview();
        };
        row.Children.Add(slider);
        row.Children.Add(valueLabel);
        return row;
    }



    private void SetPaintDialogColor(Color color)
    {
        paintDialogColor = color;
        RefreshPaintDialogAlphaBrush();
        RefreshPaintDialogPreview();
        RebuildPaintDialogTab();
    }

    private void RefreshPaintDialogPreview()
    {
        if (paintDialogNewPreview is not null)
        {
            paintDialogNewPreview.Background = new SolidColorBrush(paintDialogColor);
        }
        if (paintDialogHexBox is not null && !paintDialogHexBox.IsFocused)
        {
            paintDialogHexBox.Text = $"#{paintDialogColor.R:X2}{paintDialogColor.G:X2}{paintDialogColor.B:X2}";
        }
    }

    private static void RgbToHsv(Color color, out double h, out double s, out double v)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        v = max;
        s = max == 0 ? 0 : (max - min) / max;
        h = 0;
        if (max != min)
        {
            if (max == r) h = 60 * ((g - b) / (max - min) % 6);
            else if (max == g) h = 60 * ((b - r) / (max - min) + 2);
            else h = 60 * ((r - g) / (max - min) + 4);
        }
        if (h < 0) h += 360;
    }

    private static Color HsvToColor(double h, double s, double v, byte alpha)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        return Color.FromArgb(alpha, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private void SetPaintColor(Color color)
    {
        if (paintColor == color)
        {
            return;
        }
        paintColor = color;
        if (paintColorSwatch is not null)
        {
            paintColorSwatch.Background = new SolidColorBrush(color);
        }
        if (paintRailColorSwatch is not null)
        {
            paintRailColorSwatch.Background = new SolidColorBrush(color);
        }
        if (paintBrushCursorInner is not null)
        {
            paintBrushCursorInner.Stroke = new SolidColorBrush(color);
        }
        if (paintTool == PaintTool.Shape)
        {
            DrawRotatedShapePreview();
        }
        UpdatePaintFrame();
        UpdateQuickPaletteHighlights();
    }

    private void PushPaintUndo()
    {
        paintUndoStack.Add((int[])paintPixels.Clone());
        if (paintUndoStack.Count > MaxPaintUndo)
        {
            paintUndoStack.RemoveAt(0);
        }
        paintRedoStack.Clear();
        UpdatePaintChrome();
    }

    private void UndoPaint()
    {
        if (paintUndoStack.Count == 0)
        {
            return;
        }
        paintRedoStack.Add((int[])paintPixels.Clone());
        paintPixels = (int[])paintUndoStack[^1].Clone();
        paintUndoStack.RemoveAt(paintUndoStack.Count - 1);
        paintingDirty = true;
        UploadPaintPixels();
        UpdatePaintChrome();
    }

    private void RedoPaint()
    {
        if (paintRedoStack.Count == 0)
        {
            return;
        }
        paintUndoStack.Add((int[])paintPixels.Clone());
        paintPixels = (int[])paintRedoStack[^1].Clone();
        paintRedoStack.RemoveAt(paintRedoStack.Count - 1);
        paintingDirty = true;
        UploadPaintPixels();
        UpdatePaintChrome();
    }

    private static int ToPixel(Color color) => (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;

    private static Color FromPixel(int pixel) => Color.FromArgb(
        (byte)(pixel >> 24), (byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel);

    private static int BlendPixel(int destination, Color source)
    {
        if (source.A == 0)
        {
            return 0;
        }
        if (source.A == 255)
        {
            return ToPixel(source);
        }
        var back = FromPixel(destination);
        var alpha = source.A / 255.0;
        var backAlpha = back.A / 255.0 * (1 - alpha);
        var outAlpha = alpha + backAlpha;
        if (outAlpha <= 0)
        {
            return 0;
        }
        byte Blend(byte front, byte backChannel) =>
            (byte)((front * alpha + backChannel * backAlpha) / outAlpha);
        return ((int)(outAlpha * 255) << 24) | (Blend(source.R, back.R) << 16) | (Blend(source.G, back.G) << 8) | Blend(source.B, back.B);
    }

    private void StampPaint(int x, int y, bool erase)
    {
        if (paintSize <= 1)
        {
            if (x >= 0 && y >= 0 && x < paintWidth && y < paintHeight)
            {
                var index = y * paintWidth + x;
                paintPixels[index] = erase ? 0 : BlendPixel(paintPixels[index], paintColor);
            }
            return;
        }
        var radius = Math.Max(1, (int)Math.Round(paintSize / 2.0));
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius)
                {
                    continue;
                }
                var px = x + dx;
                var py = y + dy;
                if (px < 0 || py < 0 || px >= paintWidth || py >= paintHeight)
                {
                    continue;
                }
                var index = py * paintWidth + px;
                paintPixels[index] = erase ? 0 : BlendPixel(paintPixels[index], paintColor);
            }
        }
    }

    private void StrokePaintTo(int x, int y, bool erase)
    {
        var steps = Math.Max(Math.Abs(x - (int)paintLastCell.X), Math.Abs(y - (int)paintLastCell.Y));
        steps = Math.Max(steps, 1);
        for (var step = 1; step <= steps; step++)
        {
            var t = (double)step / steps;
            StampPaint(
                (int)Math.Round(paintLastCell.X + (x - paintLastCell.X) * t),
                (int)Math.Round(paintLastCell.Y + (y - paintLastCell.Y) * t),
                erase);
        }
        paintLastCell = new Point(x, y);
    }

    private void FloodFillPaint(int x, int y)
    {
        if (x < 0 || y < 0 || x >= paintWidth || y >= paintHeight)
        {
            return;
        }
        var target = paintPixels[y * paintWidth + x];
        var replacement = ToPixel(paintColor);
        if (target == replacement)
        {
            return;
        }
        var tolerance = paintTolerance * paintTolerance * 3;
        var targetColor = FromPixel(target);
        bool Matches(int pixel)
        {
            var current = FromPixel(pixel);
            var dr = current.R - targetColor.R;
            var dg = current.G - targetColor.G;
            var db = current.B - targetColor.B;
            var da = (current.A - targetColor.A) * 2;
            return dr * dr + dg * dg + db * db + da * da <= tolerance;
        }

        var total = paintWidth * paintHeight;
        var visited = new bool[total];
        var queue = new Queue<int>();
        var startIdx = y * paintWidth + x;
        if (!Matches(paintPixels[startIdx]))
        {
            return;
        }
        visited[startIdx] = true;
        queue.Enqueue(startIdx);

        while (queue.Count > 0)
        {
            var idx = queue.Dequeue();
            paintPixels[idx] = replacement;
            var cx = idx % paintWidth;
            var cy = idx / paintWidth;

            if (cx > 0)
            {
                var n = idx - 1;
                if (!visited[n] && Matches(paintPixels[n]))
                {
                    visited[n] = true;
                    queue.Enqueue(n);
                }
            }
            if (cx < paintWidth - 1)
            {
                var n = idx + 1;
                if (!visited[n] && Matches(paintPixels[n]))
                {
                    visited[n] = true;
                    queue.Enqueue(n);
                }
            }
            if (cy > 0)
            {
                var n = idx - paintWidth;
                if (!visited[n] && Matches(paintPixels[n]))
                {
                    visited[n] = true;
                    queue.Enqueue(n);
                }
            }
            if (cy < paintHeight - 1)
            {
                var n = idx + paintWidth;
                if (!visited[n] && Matches(paintPixels[n]))
                {
                    visited[n] = true;
                    queue.Enqueue(n);
                }
            }
        }
    }

    private void DrawPaintShape(int[] buffer, int x0, int y0, int x1, int y1)
    {
        var left = Math.Min(x0, x1);
        var right = Math.Max(x0, x1);
        var top = Math.Min(y0, y1);
        var bottom = Math.Max(y0, y1);
        var thickness = Math.Max(1, (int)Math.Round(paintSize));

        void Line(int ax, int ay, int bx, int by)
        {
            var steps = Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay));
            steps = Math.Max(steps, 1);
            for (var step = 0; step <= steps; step++)
            {
                var t = (double)step / steps;
                StampOnto(buffer,
                    (int)Math.Round(ax + (bx - ax) * t),
                    (int)Math.Round(ay + (by - ay) * t));
            }
        }

        if (paintTool == PaintTool.Line || paintShape == PaintShape.Line)
        {
            Line(x0, y0, x1, y1);
            return;
        }

        switch (paintShape)
        {
            case PaintShape.Rectangle:
                if (paintShapeFilled)
                {
                    for (var y = top; y <= bottom; y++)
                    {
                        for (var x = left; x <= right; x++)
                        {
                            StampOnto(buffer, x, y);
                        }
                    }
                }
                else
                {
                    for (var offset = 0; offset < thickness; offset++)
                    {
                        Line(left + offset, top + offset, right - offset, top + offset);
                        Line(left + offset, bottom - offset, right - offset, bottom - offset);
                        Line(left + offset, top + offset, left + offset, bottom - offset);
                        Line(right - offset, top + offset, right - offset, bottom - offset);
                    }
                }
                break;
            case PaintShape.Ellipse:
                {
                    var cx = (left + right) / 2.0;
                    var cy = (top + bottom) / 2.0;
                    var rx = Math.Max((right - left) / 2.0, 0.5);
                    var ry = Math.Max((bottom - top) / 2.0, 0.5);
                    if (paintShapeFilled)
                    {
                        for (var y = top; y <= bottom; y++)
                        {
                            for (var x = left; x <= right; x++)
                            {
                                var nx = (x - cx) / rx;
                                var ny = (y - cy) / ry;
                                if (nx * nx + ny * ny <= 1)
                                {
                                    StampOnto(buffer, x, y);
                                }
                            }
                        }
                    }
                    else
                    {
                        var perimeter = (int)(Math.PI * (rx + ry) * 2);
                        for (var step = 0; step <= perimeter; step++)
                        {
                            var angle = step * 2 * Math.PI / Math.Max(perimeter, 1);
                            StampOnto(buffer, (int)Math.Round(cx + rx * Math.Cos(angle)), (int)Math.Round(cy + ry * Math.Sin(angle)));
                        }
                    }
                }
                break;
            default:
                Line(x0, y0, x1, y1);
                break;
        }
    }

    private void StampOnto(int[] buffer, int x, int y)
    {
        if (paintSize <= 1)
        {
            if (x >= 0 && y >= 0 && x < paintWidth && y < paintHeight)
            {
                var index = y * paintWidth + x;
                buffer[index] = BlendPixel(buffer[index], paintColor);
            }
            return;
        }
        var radius = Math.Max(1, (int)Math.Round(paintSize / 2.0));
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius)
                {
                    continue;
                }
                var px = x + dx;
                var py = y + dy;
                if (px < 0 || py < 0 || px >= paintWidth || py >= paintHeight)
                {
                    continue;
                }
                var index = py * paintWidth + px;
                buffer[index] = BlendPixel(buffer[index], paintColor);
            }
        }
    }

    private Point PaintCell(MouseEventArgs e)
    {
        var position = e.GetPosition(paintImage);
        return new Point(
            (int)Math.Floor(position.X),
            (int)Math.Floor(position.Y));
    }

    private Point PaintImagePos(Point viewportPos) => new(
        (viewportPos.X - paintPan.X) / paintZoom,
        (viewportPos.Y - paintPan.Y) / paintZoom);

    private static Point PaintCellAt(Point imagePos) => new(
        (int)Math.Floor(imagePos.X),
        (int)Math.Floor(imagePos.Y));

    private bool PaintEraseActive() => paintTool == PaintTool.Eraser || paintStylusEraser;

    private void PaintViewport_MouseDown(object sender, MouseButtonEventArgs e) =>
        PaintPointerDown(e.GetPosition(paintViewport));

    private void PaintViewport_MouseMove(object sender, MouseEventArgs e)
    {
        PaintPointerMove(e.GetPosition(paintViewport), e.LeftButton == MouseButtonState.Pressed);
        UpdatePaintStatus();
    }

    private void PaintViewport_MouseUp(object sender, MouseButtonEventArgs e) => PaintPointerUp(e.GetPosition(paintViewport));

    private void PaintPointerDown(Point screen)
    {
        if (paintImage is null || activeProject is null)
        {
            return;
        }
        if (paintSpacePanning)
        {
            paintPanning = true;
            paintPanStart = screen;
            paintPanOrigin = paintPan;
            paintViewport?.CaptureMouse();
            return;
        }

        var cell = PaintCellAt(PaintImagePos(screen));
        var x = (int)cell.X;
        var y = (int)cell.Y;
        var inside = x >= 0 && y >= 0 && x < paintWidth && y < paintHeight;

        switch (paintTool)
        {
            case PaintTool.Hand:
                paintPanning = true;
                paintPanStart = screen;
                paintPanOrigin = paintPan;
                paintViewport?.CaptureMouse();
                break;
            case PaintTool.Pipette:
                if (inside)
                {
                    SetPaintColor(FromPixel(paintPixels[y * paintWidth + x]));
                    paintTool = paintToolBeforePipette;
                    RebuildPaintTools();
                    RebuildPaintOptions();
                }
                break;
            case PaintTool.Fill:
                paintFillApplied = false;
                if (inside)
                {
                    PushPaintUndo();
                    FloodFillPaint(x, y);
                    paintingDirty = true;
                    UploadPaintPixels();
                    paintFillApplied = true;
                }
                break;
            case PaintTool.Shape:
                {
                    var action = HitPaintFrame(screen, out var handle);
                    if (action == PaintFrameAction.None && inside)
                    {
                        if (shapeFrameActive && paintShapeBase is not null)
                        {
                            PushPaintUndo();
                            paintShapeBase = (int[])paintPixels.Clone();
                        }
                        shapeFrameX = cell.X;
                        shapeFrameY = cell.Y;
                        shapeFrameWidth = 2;
                        shapeFrameHeight = 2;
                        shapeFrameRotation = 0;
                        shapeFrameActive = true;
                        action = PaintFrameAction.Resize;
                        handle = 1;
                    }
                    if (action != PaintFrameAction.None)
                    {
                        paintFrameAction = action;
                        paintFrameHandle = handle;
                        shapeFrameGrabRotation = shapeFrameRotation;
                        shapeFrameGrabWidth = shapeFrameWidth;
                        shapeFrameGrabHeight = shapeFrameHeight;
                        var grabCell = ScreenToPaint(screen);
                        shapeFrameGrabCell = grabCell;
                        shapeFrameCenter0 = new Point(shapeFrameX, shapeFrameY);
                        var grabRadians = shapeFrameRotation * Math.PI / 180;
                        var grabCos = Math.Cos(grabRadians);
                        var grabSin = Math.Sin(grabRadians);
                        Point ToGrabLocal(Point p)
                        {
                            var dx = p.X - shapeFrameX;
                            var dy = p.Y - shapeFrameY;
                            return new Point(dx * grabCos + dy * grabSin, -dx * grabSin + dy * grabCos);
                        }
                        if (action == PaintFrameAction.Resize)
                        {
                            var corners = ShapeFrameCorners();
                            var opposite = handle < 4 ? corners[(handle + 2) % 4] : ShapeFrameEdgeMid((handle - 4 + 2) % 4);
                            shapeFrameOpp = ToGrabLocal(opposite);
                        }
                        if (paintShapeBase is null)
                        {
                            paintShapeBase = (int[])paintPixels.Clone();
                        }
                        paintShapeStamped = false;
                        paintViewport?.CaptureMouse();
                        DrawRotatedShapePreview();
                        UpdatePaintFrame();
                    }
                    break;
                }
            case PaintTool.Line:
                PushPaintUndo();
                paintShapeBase = (int[])paintPixels.Clone();
                paintLastCell = new Point(x, y);
                paintStroking = true;
                paintViewport?.CaptureMouse();
                break;
            case PaintTool.Text:
                {
                    var action = HitPaintFrame(screen, out var textHandle);
                    if (action == PaintFrameAction.Rotate)
                    {
                        paintFrameAction = PaintFrameAction.Rotate;
                        paintTextGrabRotation = paintTextRotation;
                        shapeFrameGrabCell = ScreenToPaint(screen);
                        paintViewport?.CaptureMouse();
                    }
                    else if (action == PaintFrameAction.Resize)
                    {
                        paintFrameAction = PaintFrameAction.Resize;
                        paintFrameHandle = textHandle;
                        paintTextGrabScaleX = paintTextScaleX;
                        paintTextGrabScaleY = paintTextScaleY;
                        shapeFrameGrabCell = ScreenToPaint(screen);
                        paintViewport?.CaptureMouse();
                    }
                    else if (action == PaintFrameAction.Move)
                    {
                        paintFrameAction = PaintFrameAction.Move;
                        var grabCell = ScreenToPaint(screen);
                        shapeFrameGrabCell = new Point(grabCell.X - paintTextX, grabCell.Y - paintTextY);
                        paintViewport?.CaptureMouse();
                    }
                    else if (inside)
                    {
                        var cellClick = ScreenToPaint(screen);
                        paintTextX = cellClick.X;
                        paintTextY = cellClick.Y;
                        paintTextPlaced = true;
                        paintFrameAction = PaintFrameAction.Move;
                        shapeFrameGrabCell = new Point(0, 0);
                        paintViewport?.CaptureMouse();
                        DrawPaintTextPreview();
                    }
                    break;
                }
            default:
                PushPaintUndo();
                paintLastCell = new Point(x, y);
                if (inside)
                {
                    if (paintTool == PaintTool.Spray)
                    {
                        SprayPaintAt(x, y);
                        StartSprayTimer();
                    }
                    else
                    {
                        StampPaint(x, y, PaintEraseActive());
                    }
                    UploadPaintPixels();
                }
                paintStroking = true;
                paintingDirty = true;
                paintViewport?.CaptureMouse();
                break;
        }
    }

    private void PaintPointerMove(Point screen, bool pressed)
    {
        if ((paintPanning || paintRmbPanning) && paintViewport is not null)
        {
            paintPan = new Point(
                paintPanOrigin.X + screen.X - paintPanStart.X,
                paintPanOrigin.Y + screen.Y - paintPanStart.Y);
            RefreshPaintTransform();
            return;
        }

        var hover = PaintCellAt(PaintImagePos(screen));
        paintHoverCell = hover;
        UpdatePaintCursor();

        if (paintTool == PaintTool.Pipette && pressed)
        {
            var px = (int)hover.X;
            var py = (int)hover.Y;
            if (px >= 0 && py >= 0 && px < paintWidth && py < paintHeight)
            {
                SetPaintColor(FromPixel(paintPixels[py * paintWidth + px]));
            }
            return;
        }

        if (paintTool == PaintTool.Text && paintFrameAction != PaintFrameAction.None)
        {
            if (!pressed)
            {
                paintFrameAction = PaintFrameAction.None;
                return;
            }
            if (paintFrameAction == PaintFrameAction.Rotate)
            {
                var textCell = ScreenToPaint(screen);
                var w = Math.Max(16, PaintTextEffW());
                var h = Math.Max(16, PaintTextEffH());
                var cx = paintTextX + w / 2.0;
                var cy = paintTextY + h / 2.0;
                var startAngle = Math.Atan2(shapeFrameGrabCell.Y - cy, shapeFrameGrabCell.X - cx);
                var nowAngle = Math.Atan2(textCell.Y - cy, textCell.X - cx);
                paintTextRotation = paintTextGrabRotation + (nowAngle - startAngle) * 180.0 / Math.PI;
                DrawPaintTextPreview();
                return;
            }
            if (paintFrameAction == PaintFrameAction.Move)
            {
                var textCell = ScreenToPaint(screen);
                paintTextX = textCell.X - shapeFrameGrabCell.X;
                paintTextY = textCell.Y - shapeFrameGrabCell.Y;
                DrawPaintTextPreview();
                return;
            }
            if (paintFrameAction == PaintFrameAction.Resize)
            {
                var textCell = ScreenToPaint(screen);
                var baseW = Math.Max(1, paintTextCacheWidth);
                var baseH = Math.Max(1, paintTextCacheHeight);
                if (paintFrameHandle >= 4)
                {
                    switch (paintFrameHandle)
                    {
                        case 5:
                            paintTextScaleX = Math.Max(2.0 / baseW, (textCell.X - paintTextX) / baseW);
                            break;
                        case 7:
                            {
                                var right = paintTextX + baseW * paintTextScaleX;
                                var newW = Math.Max(2, right - textCell.X);
                                paintTextX = right - newW;
                                paintTextScaleX = newW / baseW;
                                break;
                            }
                        case 6:
                            paintTextScaleY = Math.Max(2.0 / baseH, (textCell.Y - paintTextY) / baseH);
                            break;
                        default:
                            {
                                var bottom = paintTextY + baseH * paintTextScaleY;
                                var newH = Math.Max(2, bottom - textCell.Y);
                                paintTextY = bottom - newH;
                                paintTextScaleY = newH / baseH;
                                break;
                            }
                    }
                    DrawPaintTextPreview();
                    return;
                }
                var effW = baseW * paintTextGrabScaleX;
                var effH = baseH * paintTextGrabScaleY;
                double cornerOppX = paintFrameHandle == 1 || paintFrameHandle == 2 ? paintTextX : paintTextX + effW;
                double cornerOppY = paintFrameHandle == 2 || paintFrameHandle == 3 ? paintTextY : paintTextY + effH;
                var cornerNewW = Math.Abs(textCell.X - cornerOppX);
                var cornerNewH = Math.Abs(textCell.Y - cornerOppY);
                var cornerDiag = Math.Sqrt(cornerNewW * cornerNewW + cornerNewH * cornerNewH);
                var cornerOrigDiag = Math.Sqrt(effW * effW + effH * effH);
                if (cornerOrigDiag > 1)
                {
                    var factor = cornerDiag / cornerOrigDiag;
                    paintTextScaleX = paintTextGrabScaleX * factor;
                    paintTextScaleY = paintTextGrabScaleY * factor;
                    if (paintFrameHandle == 0) { paintTextX = textCell.X; paintTextY = textCell.Y; }
                    else if (paintFrameHandle == 1) { paintTextY = textCell.Y; }
                    else if (paintFrameHandle == 3) { paintTextX = textCell.X; }
                }
                DrawPaintTextPreview();
                return;
            }
            return;
        }
        if (paintTool == PaintTool.Shape && paintFrameAction != PaintFrameAction.None)
        {
            if (!pressed)
            {
                paintFrameAction = PaintFrameAction.None;
                return;
            }
            var frameCell = ScreenToPaint(screen);
            switch (paintFrameAction)
            {
                case PaintFrameAction.Move:
                    shapeFrameX += frameCell.X - shapeFrameGrabCell.X;
                    shapeFrameY += frameCell.Y - shapeFrameGrabCell.Y;
                    shapeFrameGrabCell = frameCell;
                    break;
                case PaintFrameAction.Resize:
                    {
                        var moveRadians = shapeFrameGrabRotation * Math.PI / 180;
                        var moveCos = Math.Cos(moveRadians);
                        var moveSin = Math.Sin(moveRadians);
                        Point ToMoveLocal(Point p)
                        {
                            var dx = p.X - shapeFrameCenter0.X;
                            var dy = p.Y - shapeFrameCenter0.Y;
                            return new Point(dx * moveCos + dy * moveSin, -dx * moveSin + dy * moveCos);
                        }
                        var cursor = ToMoveLocal(frameCell);
                        var minX = shapeFrameOpp.X;
                        var maxX = shapeFrameOpp.X;
                        var minY = shapeFrameOpp.Y;
                        var maxY = shapeFrameOpp.Y;
                        var adjustX = paintFrameHandle < 4 || paintFrameHandle == 5 || paintFrameHandle == 7;
                        var adjustY = paintFrameHandle < 4 || paintFrameHandle == 4 || paintFrameHandle == 6;
                        if (adjustX)
                        {
                            minX = Math.Min(shapeFrameOpp.X, cursor.X);
                            maxX = Math.Max(shapeFrameOpp.X, cursor.X);
                            if (maxX - minX < 2)
                            {
                                maxX = minX + 2;
                            }
                        }
                        else
                        {
                            minX = -shapeFrameGrabWidth / 2;
                            maxX = shapeFrameGrabWidth / 2;
                        }
                        if (adjustY)
                        {
                            minY = Math.Min(shapeFrameOpp.Y, cursor.Y);
                            maxY = Math.Max(shapeFrameOpp.Y, cursor.Y);
                            if (maxY - minY < 2)
                            {
                                maxY = minY + 2;
                            }
                        }
                        else
                        {
                            minY = -shapeFrameGrabHeight / 2;
                            maxY = shapeFrameGrabHeight / 2;
                        }
                        var midLocal = new Point((minX + maxX) / 2, (minY + maxY) / 2);
                        shapeFrameWidth = maxX - minX;
                        shapeFrameHeight = maxY - minY;
                        shapeFrameX = shapeFrameCenter0.X + midLocal.X * moveCos - midLocal.Y * moveSin;
                        shapeFrameY = shapeFrameCenter0.Y + midLocal.X * moveSin + midLocal.Y * moveCos;
                        break;
                    }
                case PaintFrameAction.Rotate:
                    {
                        var startAngle = Math.Atan2(shapeFrameGrabCell.Y - shapeFrameY, shapeFrameGrabCell.X - shapeFrameX);
                        var nowAngle = Math.Atan2(frameCell.Y - shapeFrameY, frameCell.X - shapeFrameX);
                        shapeFrameRotation = shapeFrameGrabRotation + (nowAngle - startAngle) * 180 / Math.PI;
                        break;
                    }
                default:
                    return;
            }
            UpdatePaintFrame();
            DrawRotatedShapePreview();
            return;
        }

        if (!paintStroking || !pressed)
        {
            return;
        }

        var cell = PaintCellAt(PaintImagePos(screen));
        var x = (int)cell.X;
        var y = (int)cell.Y;

        if (paintTool == PaintTool.Line && paintShapeBase is not null)
        {
            var preview = (int[])paintShapeBase.Clone();
            DrawPaintShape(preview,
                (int)paintLastCell.X, (int)paintLastCell.Y,
                x, y);
            paintPixels = preview;
            paintingDirty = true;
            UploadPaintPixels();
            return;
        }

        if (paintTool == PaintTool.Spray)
        {
            var targetX = (int)Math.Clamp(x, 0, paintWidth - 1);
            var targetY = (int)Math.Clamp(y, 0, paintHeight - 1);
            var fromX = (int)Math.Clamp(paintLastCell.X, 0, paintWidth - 1);
            var fromY = (int)Math.Clamp(paintLastCell.Y, 0, paintHeight - 1);

            var steps = Math.Max(Math.Abs(targetX - fromX), Math.Abs(targetY - fromY));
            var radius = Math.Max(1.0, paintSize / 2.0);
            var stepStride = Math.Max(1, (int)Math.Ceiling(radius * 0.4));
            var numInterp = Math.Max(1, steps / stepStride);

            for (var s = 1; s <= numInterp; s++)
            {
                var t = (double)s / numInterp;
                var ix = (int)Math.Round(fromX + (targetX - fromX) * t);
                var iy = (int)Math.Round(fromY + (targetY - fromY) * t);
                var partialCount = Math.Max(2, (int)Math.Round(paintSprayDensity / Math.Max(1, numInterp)));
                SprayPaintAt(ix, iy, partialCount);
            }

            paintLastCell = new Point(targetX, targetY);
            paintingDirty = true;
            UploadPaintPixels();
            return;
        }

        StrokePaintTo(x, y, PaintEraseActive());
        paintingDirty = true;
        UploadPaintPixels();
    }

    private void StartSprayTimer()
    {
        if (paintSprayTimer is null)
        {
            paintSprayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(35)
            };
            paintSprayTimer.Tick += (_, _) =>
            {
                if (!paintStroking || paintTool != PaintTool.Spray)
                {
                    paintSprayTimer?.Stop();
                    return;
                }
                var x = (int)Math.Clamp(paintLastCell.X, 0, paintWidth - 1);
                var y = (int)Math.Clamp(paintLastCell.Y, 0, paintHeight - 1);
                SprayPaintAt(x, y);
                paintingDirty = true;
                UploadPaintPixels();
            };
        }
        paintSprayTimer.Start();
    }

    private void SprayPaintAt(int x, int y, int? customCount = null)
    {
        var radius = Math.Max(1.0, paintSize / 2.0);
        var count = customCount ?? Math.Max(1, (int)paintSprayDensity);
        var erase = PaintEraseActive();

        for (var i = 0; i < count; i++)
        {
            var angle = paintRandom.NextDouble() * 2 * Math.PI;
            var u1 = paintRandom.NextDouble();
            var u2 = paintRandom.NextDouble();
            var distFactor = Math.Min(u1, u2) + Math.Abs(u1 - u2) * 0.5;
            var distance = distFactor * radius;

            var px = x + (int)Math.Round(Math.Cos(angle) * distance);
            var py = y + (int)Math.Round(Math.Sin(angle) * distance);
            if (px < 0 || py < 0 || px >= paintWidth || py >= paintHeight)
            {
                continue;
            }
            var index = py * paintWidth + px;
            paintPixels[index] = erase ? 0 : BlendPixel(paintPixels[index], paintColor);
        }
    }

    private void PaintPointerUp(Point screen)
    {
        paintSprayTimer?.Stop();
        paintPanning = false;
        paintRmbPanning = false;
        paintStroking = false;
        if (paintFrameAction != PaintFrameAction.None)
        {
            paintFrameAction = PaintFrameAction.None;
            if (paintTool == PaintTool.Text)
            {
                DrawPaintTextPreview();
                UpdatePaintFrame();
            }
            else
            {
                DrawRotatedShapePreview();
                UpdatePaintFrame();
            }
        }
        else
        {
            if (paintTool != PaintTool.Text)
            {
                paintShapeBase = null;
            }
        }
        if (paintTool == PaintTool.Fill && paintFrameAction == PaintFrameAction.None && !paintFillApplied)
        {
            var release = PaintCellAt(PaintImagePos(screen));
            var rx = (int)release.X;
            var ry = (int)release.Y;
            if (rx >= 0 && ry >= 0 && rx < paintWidth && ry < paintHeight &&
                paintPixels[ry * paintWidth + rx] != ToPixel(paintColor))
            {
                PushPaintUndo();
                FloodFillPaint(rx, ry);
                paintingDirty = true;
                UploadPaintPixels();
            }
        }
        paintViewport?.ReleaseMouseCapture();
    }

    private void SavePainting()
    {
        if (activeProject is null || paintingScene is null || paintingItem is null || paintBitmap is null)
        {
            return;
        }

        try
        {
            var path = paintingPath;
            if (path is null)
            {
                var directory = projectContentStore.EnsureObjectResourceDirectory(activeProject, paintingScene, paintingItem.Id, "sprites");
                path = FreeFilePath(directory, (paintingNewFileName ?? "Рисунок") + ".png");
            }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(paintBitmap));
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
            paintingPath = path;
            paintingDirty = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Не удалось сохранить рисунок.", exception);
            MessageBox.Show(this, "Не удалось сохранить рисунок.", "Искра Студио", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        paintSprayTimer?.Stop();
        paintSpacePanning = false;
        Mouse.OverrideCursor = null;
        showingPainting = false;
        if (detailScene is not null && detailItem is not null)
        {
            ShowObjectDetail(detailScene, detailItem, ObjectDetailTab.Images);
        }
    }

    private void PaintScreen_KeyDown(object sender, KeyEventArgs e)
    {
        if (!showingPainting)
        {
            return;
        }
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (paintTextInputBox is not null && paintTextInputBox.IsKeyboardFocusWithin)
        {
            if (ctrl && e.Key == Key.Enter)
            {
                StampPaintText();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Space && !e.IsRepeat)
        {
            paintSpacePanning = true;
            Mouse.OverrideCursor = Cursors.Hand;
            e.Handled = true;
            return;
        }
        if (ctrl && !shift && e.Key == Key.Z)
        {
            UndoPaint();
            e.Handled = true;
            return;
        }
        if ((ctrl && e.Key == Key.Y) || (ctrl && shift && e.Key == Key.Z))
        {
            RedoPaint();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.S)
        {
            SavePainting();
            e.Handled = true;
            return;
        }
        if (ctrl && (e.Key == Key.D0 || e.Key == Key.NumPad0))
        {
            FitPaintToView();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Add || (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Insert)))
        {
            ZoomPaintToCenter(paintZoom * 1.25);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Subtract || (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Delete)))
        {
            ZoomPaintToCenter(paintZoom / 1.25);
            e.Handled = true;
            return;
        }
        if (!ctrl && !shift)
        {
            switch (e.Key)
            {
                case Key.B:
                    SelectPaintTool(PaintTool.Brush);
                    e.Handled = true;
                    break;
                case Key.S:
                    SelectPaintTool(PaintTool.Spray);
                    e.Handled = true;
                    break;
                case Key.E:
                    SelectPaintTool(PaintTool.Eraser);
                    e.Handled = true;
                    break;
                case Key.G:
                    SelectPaintTool(PaintTool.Fill);
                    e.Handled = true;
                    break;
                case Key.I:
                    SelectPaintTool(PaintTool.Pipette);
                    e.Handled = true;
                    break;
                case Key.U:
                    SelectPaintTool(PaintTool.Shape);
                    e.Handled = true;
                    break;
                case Key.L:
                    SelectPaintTool(PaintTool.Line);
                    e.Handled = true;
                    break;
                case Key.T:
                    SelectPaintTool(PaintTool.Text);
                    e.Handled = true;
                    break;
                case Key.H:
                    SelectPaintTool(PaintTool.Hand);
                    e.Handled = true;
                    break;
                case Key.C:
                    ShowPaintColorDialog();
                    e.Handled = true;
                    break;
                case Key.OemOpenBrackets:
                    paintSize = Math.Max(1, paintSize - 1);
                    UpdatePaintSizeLabel();
                    UpdatePaintCursor();
                    RebuildPaintOptions();
                    e.Handled = true;
                    break;
                case Key.OemCloseBrackets:
                    paintSize = Math.Min(64, paintSize + 1);
                    UpdatePaintSizeLabel();
                    UpdatePaintCursor();
                    RebuildPaintOptions();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (paintTool == PaintTool.Shape)
                    {
                        StampPaintShape();
                        e.Handled = true;
                    }
                    else if (paintTool == PaintTool.Text)
                    {
                        StampPaintText();
                        e.Handled = true;
                    }
                    break;
            }
        }
    }

    private void PaintScreen_KeyUp(object sender, KeyEventArgs e)
    {
        if (!showingPainting)
        {
            return;
        }
        if (e.Key == Key.Space)
        {
            paintSpacePanning = false;
            Mouse.OverrideCursor = null;
            e.Handled = true;
        }
    }
}
