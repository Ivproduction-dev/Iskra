using Microsoft.Maui.Controls.Shapes;
using SkiaSharp;

namespace Iskra.Android;

internal sealed class PaintColorPage : ContentPage
{
    private static readonly SKColor[] Presets =
    [
        new(0x00, 0x74, 0xCD), new(0x00, 0xB4, 0xF1), new(0x07, 0x87, 0x07), new(0x8E, 0xC4, 0x30),
        new(0x61, 0x29, 0x0E), new(0xA8, 0x48, 0x18), new(0xDE, 0xAE, 0x66), new(0xF0, 0xE4, 0xA8),
        new(0x7D, 0x13, 0x79), new(0xA7, 0x43, 0xD1), new(0xCA, 0x01, 0x86), new(0xEB, 0x90, 0xD3),
        new(0xC5, 0x06, 0x0E), new(0xEB, 0x46, 0x18), new(0xF9, 0x92, 0x1C), new(0xF3, 0xD6, 0x05),
        new(0x00, 0x00, 0x00), new(0xA3, 0xA3, 0xA3), new(0xFF, 0xFF, 0xFF), new(0x00, 0x00, 0x00, 0x00)
    ];

    private readonly Action<SKColor> apply;
    private readonly Action? closed;
    private readonly GraphicsView svView = new() { HeightRequest = 220, VerticalOptions = LayoutOptions.Fill };
    private readonly GraphicsView hueView = new() { HeightRequest = 32 };
    private readonly GraphicsView alphaView = new() { HeightRequest = 32 };
    private readonly Border preview = new() { HeightRequest = 76, Stroke = MobileTheme.Border, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 12 } };
    private readonly Label hexLabel = MobileUi.Text("#000000", 13, true, MobileTheme.TextMuted);
    private readonly Label alphaLabel = MobileUi.Text("Альфа 255", 13, color: MobileTheme.TextMuted);
    private readonly Entry hexEntry = new() { Placeholder = "#RRGGBB", MaxLength = 9, ReturnType = ReturnType.Done };
    private readonly Entry redEntry = new() { Keyboard = Keyboard.Numeric, MaxLength = 3, ReturnType = ReturnType.Next };
    private readonly Entry greenEntry = new() { Keyboard = Keyboard.Numeric, MaxLength = 3, ReturnType = ReturnType.Next };
    private readonly Entry blueEntry = new() { Keyboard = Keyboard.Numeric, MaxLength = 3, ReturnType = ReturnType.Done };
    private float hue;
    private float saturation;
    private float value;
    private byte alpha = 255;
    private bool closing;

    public PaintColorPage(SKColor initial, Action<SKColor> apply, Action? closed = null)
    {
        this.apply = apply;
        this.closed = closed;
        BackgroundColor = MobileTheme.SurfacePrimary;
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        NavigationPage.SetHasNavigationBar(this, false);
        SetColorState(initial, refreshEntries: false);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(48), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(72), new ColumnDefinition(48)),
            Padding = new Thickness(12, 8, 8, 8),
            HeightRequest = 60
        };
        header.Add(MobileUi.IconButton("←", "Отмена", async (_, _) => await CloseAsync(applyColor: false)), 0, 0);
        header.Add(MobileUi.Text("Цвет", 18, true), 1, 0);
        var done = new Button
        {
            Text = "Готово",
            TextColor = Colors.White,
            BackgroundColor = MobileTheme.Accent,
            CornerRadius = 9,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(10, 6),
            MinimumHeightRequest = 40
        };
        done.Clicked += async (_, _) => await CloseAsync(applyColor: true);
        header.Add(done, 2, 0);
        header.Add(MobileUi.IconButton("✕", "Закрыть", async (_, _) => await CloseAsync(applyColor: false)), 3, 0);

        svView.Drawable = new ColorDrawable(this, ColorPart.SaturationValue);
        hueView.Drawable = new ColorDrawable(this, ColorPart.Hue);
        alphaView.Drawable = new ColorDrawable(this, ColorPart.Alpha);
        svView.StartInteraction += (_, e) =>
        {
            HoldParentScroll(svView, true);
            PickSaturationValue(e);
        };
        svView.DragInteraction += (_, e) => PickSaturationValue(e);
        svView.EndInteraction += (_, _) => HoldParentScroll(svView, false);
        svView.CancelInteraction += (_, _) => HoldParentScroll(svView, false);
        hueView.StartInteraction += (_, e) =>
        {
            HoldParentScroll(hueView, true);
            PickHue(e);
        };
        hueView.DragInteraction += (_, e) => PickHue(e);
        hueView.EndInteraction += (_, _) => HoldParentScroll(hueView, false);
        hueView.CancelInteraction += (_, _) => HoldParentScroll(hueView, false);
        alphaView.StartInteraction += (_, e) =>
        {
            HoldParentScroll(alphaView, true);
            PickAlpha(e);
        };
        alphaView.DragInteraction += (_, e) => PickAlpha(e);
        alphaView.EndInteraction += (_, _) => HoldParentScroll(alphaView, false);
        alphaView.CancelInteraction += (_, _) => HoldParentScroll(alphaView, false);

        foreach (var entry in new[] { hexEntry, redEntry, greenEntry, blueEntry })
        {
            entry.TextColor = MobileTheme.TextPrimary;
            entry.BackgroundColor = MobileTheme.SurfacePanel;
            entry.PlaceholderColor = MobileTheme.TextMuted;
        }

        var body = new VerticalStackLayout { Padding = new Thickness(16, 8, 16, 24), Spacing = 12 };
        body.Add(MobileUi.Panel(new VerticalStackLayout
        {
            Spacing = 6,
            Children = { preview, hexLabel, alphaLabel }
        }, new Thickness(12)));

        body.Add(SectionLabel("ОТТЕНОК И ЯРКОСТЬ"));
        body.Add(new Border
        {
            Content = svView,
            Stroke = MobileTheme.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 }
        });

        body.Add(SectionLabel("ТОН"));
        body.Add(hueView);
        body.Add(SectionLabel("АЛЬФА"));
        body.Add(alphaView);

        var rgbGrid = new Grid { ColumnSpacing = 8, ColumnDefinitions = new ColumnDefinitionCollection(
            new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
            new ColumnDefinition(GridLength.Star), new ColumnDefinition(96)) };
        rgbGrid.Add(ChannelBlock("R", redEntry), 0, 0);
        rgbGrid.Add(ChannelBlock("G", greenEntry), 1, 0);
        rgbGrid.Add(ChannelBlock("B", blueEntry), 2, 0);
        var applyRgb = new Button
        {
            Text = "Применить",
            FontSize = 12,
            TextColor = MobileTheme.TextBody,
            BackgroundColor = MobileTheme.SurfaceSoft,
            CornerRadius = 8,
            Padding = new Thickness(6, 6),
            VerticalOptions = LayoutOptions.End,
            HeightRequest = 42
        };
        applyRgb.Clicked += (_, _) => ApplyRgbEntries();
        rgbGrid.Add(applyRgb, 3, 0);
        body.Add(rgbGrid);

        var hexGrid = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(96))
        };
        hexGrid.Add(ChannelBlock("HEX", hexEntry), 0, 0);
        var applyHex = new Button
        {
            Text = "Применить",
            FontSize = 12,
            TextColor = MobileTheme.TextBody,
            BackgroundColor = MobileTheme.SurfaceSoft,
            CornerRadius = 8,
            Padding = new Thickness(6, 6),
            VerticalOptions = LayoutOptions.End,
            HeightRequest = 42
        };
        applyHex.Clicked += (_, _) => ApplyHexEntry();
        hexEntry.Completed += (_, _) => ApplyHexEntry();
        hexGrid.Add(applyHex, 1, 0);
        body.Add(hexGrid);

        body.Add(SectionLabel("БЫСТРАЯ ПАЛИТРА"));
        body.Add(BuildPresets());

        var root = new Grid { RowDefinitions = new RowDefinitionCollection(new RowDefinition(60), new RowDefinition(GridLength.Star)) };
        root.Add(header, 0, 0);
        root.Add(new ScrollView { Content = body }, 0, 1);
        Content = root;
        RefreshSurface(updateEntries: true);
    }

    private static Label SectionLabel(string text) => MobileUi.Text(text, 11, true, MobileTheme.TextMuted);

    private static View ChannelBlock(string title, Entry entry) => new VerticalStackLayout
    {
        Spacing = 4,
        Children =
        {
            MobileUi.Text(title, 11, true, MobileTheme.TextMuted),
            new Border
            {
                Content = entry,
                Padding = new Thickness(6, 0),
                BackgroundColor = MobileTheme.SurfacePanel,
                Stroke = MobileTheme.Border,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 8 }
            }
        }
    };

    private View BuildPresets()
    {
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (var column = 0; column < 5; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }
        for (var row = 0; row < 4; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(46));
        }

        for (var index = 0; index < Presets.Length; index++)
        {
            var preset = Presets[index];
            var swatch = new Border
            {
                BackgroundColor = ToMaui(preset),
                Stroke = MobileTheme.Border,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 9 },
                Content = preset.Alpha == 0 ? MobileUi.Text("∅", 18, true, MobileTheme.TextMuted) : null
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => SetColorState(preset, refreshEntries: true);
            swatch.GestureRecognizers.Add(tap);
            SemanticProperties.SetDescription(swatch, preset.Alpha == 0 ? "Прозрачный цвет" : $"Цвет {preset.ToHex()}");
            grid.Add(swatch, index % 5, index / 5);
        }
        return grid;
    }

    private void PickSaturationValue(TouchEventArgs e)
    {
        if (e.Touches.Length == 0)
        {
            return;
        }
        var width = Math.Max(1, svView.Width);
        var height = Math.Max(1, svView.Height);
        saturation = Math.Clamp((float)(e.Touches[0].X / width), 0, 1);
        value = Math.Clamp(1 - (float)(e.Touches[0].Y / height), 0, 1);
        RefreshSurface(updateEntries: true);
    }

    private void PickHue(TouchEventArgs e)
    {
        if (e.Touches.Length == 0)
        {
            return;
        }
        var width = Math.Max(1, hueView.Width);
        hue = Math.Clamp((float)(e.Touches[0].X / width), 0, 0.9999f) * 360;
        RefreshSurface(updateEntries: true);
    }

    private void PickAlpha(TouchEventArgs e)
    {
        if (e.Touches.Length == 0)
        {
            return;
        }
        var width = Math.Max(1, alphaView.Width);
        alpha = (byte)Math.Clamp(Math.Round(e.Touches[0].X / width * 255), 0, 255);
        RefreshSurface(updateEntries: true);
    }

    private void SetColorState(SKColor color, bool refreshEntries)
    {
        var (h, s, v) = RgbToHsv(color.Red, color.Green, color.Blue);
        hue = h;
        saturation = s;
        value = v;
        alpha = color.Alpha;
        if (refreshEntries)
        {
            RefreshSurface(updateEntries: true);
        }
        else
        {
            RefreshSurface(updateEntries: false);
        }
    }

    private void ApplyRgbEntries()
    {
        var red = ParseChannel(redEntry.Text);
        var green = ParseChannel(greenEntry.Text);
        var blue = ParseChannel(blueEntry.Text);
        if (red is null || green is null || blue is null)
        {
            RefreshSurface(updateEntries: true);
            return;
        }
        SetColorState(new SKColor((byte)red.Value, (byte)green.Value, (byte)blue.Value, alpha), refreshEntries: true);
    }

    private void ApplyHexEntry()
    {
        var raw = (hexEntry.Text ?? string.Empty).Trim().TrimStart('#');
        if (TryParseHex(hexEntry.Text, out var color))
        {
            SetColorState(raw.Length == 8 ? color : color.WithAlpha(alpha), refreshEntries: true);
        }
        else
        {
            hexEntry.Text = CurrentColor().ToHex();
        }
    }

    private static int? ParseChannel(string? text) =>
        int.TryParse((text ?? string.Empty).Trim(), out var channel) && channel is >= 0 and <= 255 ? channel : null;

    private static bool TryParseHex(string? text, out SKColor color)
    {
        color = SKColors.Black;
        var value = (text ?? string.Empty).Trim().TrimStart('#');
        if (value.Length == 3)
        {
            value = string.Concat(value.Select(character => new string(character, 2)));
        }
        if (value.Length is not (6 or 8) ||
            !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }
        if (value.Length == 6)
        {
            color = new SKColor((byte)(parsed >> 16), (byte)(parsed >> 8), (byte)parsed);
        }
        else
        {
            color = new SKColor((byte)(parsed >> 16), (byte)(parsed >> 8), (byte)parsed, (byte)(parsed >> 24));
        }
        return true;
    }

    private void RefreshSurface(bool updateEntries)
    {
        var color = CurrentColor();
        preview.BackgroundColor = ToMaui(color);
        hexLabel.Text = color.ToHex();
        alphaLabel.Text = $"Альфа {alpha}";
        if (updateEntries)
        {
            redEntry.Text = color.Red.ToString();
            greenEntry.Text = color.Green.ToString();
            blueEntry.Text = color.Blue.ToString();
            hexEntry.Text = color.ToHex();
        }
        svView.Invalidate();
        hueView.Invalidate();
        alphaView.Invalidate();
    }

    private SKColor CurrentColor() => HsvToColor(hue, saturation, value, alpha);

    private static Microsoft.Maui.Graphics.Color ToMaui(SKColor color) =>
        Microsoft.Maui.Graphics.Color.FromRgba(color.Red, color.Green, color.Blue, color.Alpha);

    private static (float Hue, float Saturation, float Value) RgbToHsv(byte red, byte green, byte blue)
    {
        var r = red / 255f;
        var g = green / 255f;
        var b = blue / 255f;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var hue = 0f;
        if (delta > 0.00001f)
        {
            if (max == r)
            {
                hue = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                hue = 60 * ((b - r) / delta + 2);
            }
            else
            {
                hue = 60 * ((r - g) / delta + 4);
            }
        }
        if (hue < 0)
        {
            hue += 360;
        }
        var saturation = max <= 0 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    private static SKColor HsvToColor(float hue, float saturation, float value, byte alpha)
    {
        var h = ((hue % 360) + 360) % 360 / 60f;
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(h % 2 - 1));
        var m = value - chroma;
        (float r, float g, float b) = h switch
        {
            < 1 => (chroma, x, 0f),
            < 2 => (x, chroma, 0f),
            < 3 => (0f, chroma, x),
            < 4 => (0f, x, chroma),
            < 5 => (x, 0f, chroma),
            _ => (chroma, 0f, x)
        };
        return new SKColor(
            (byte)Math.Clamp(Math.Round((r + m) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((g + m) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((b + m) * 255), 0, 255),
            alpha);
    }

    private static void HoldParentScroll(GraphicsView view, bool hold)
    {
        if (view.Handler?.PlatformView is global::Android.Views.View native)
        {
            native.Parent?.RequestDisallowInterceptTouchEvent(hold);
        }
    }

    private async Task CloseAsync(bool applyColor)
    {
        if (closing)
        {
            return;
        }
        closing = true;
        if (applyColor)
        {
            try
            {
                apply(CurrentColor());
            }
            catch (Exception exception)
            {
                MobileLog.Write("Не удалось применить выбранный цвет.", exception);
                await DisplayAlertAsync("Не удалось применить цвет", "Попробуй выбрать цвет ещё раз.", "Понятно");
                closing = false;
                return;
            }
        }
        closed?.Invoke();
        try
        {
            await Navigation.PopModalAsync();
        }
        catch (Exception exception)
        {
            MobileLog.Write("Не удалось закрыть выбор цвета.", exception);
            closing = false;
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(applyColor: false);
        return true;
    }

    private enum ColorPart
    {
        SaturationValue,
        Hue,
        Alpha
    }

    private sealed class ColorDrawable(PaintColorPage owner, ColorPart part) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            switch (part)
            {
                case ColorPart.SaturationValue:
                    DrawSaturationValue(canvas, dirtyRect);
                    break;
                case ColorPart.Hue:
                    DrawHue(canvas, dirtyRect);
                    break;
                case ColorPart.Alpha:
                    DrawAlpha(canvas, dirtyRect);
                    break;
            }
        }

        private void DrawSaturationValue(ICanvas canvas, RectF rect)
        {
            var baseColor = ToMaui(HsvToColor(owner.hue, 1, 1, 255));
            canvas.FillColor = baseColor;
            canvas.FillRectangle(rect);
            canvas.SetFillPaint(new LinearGradientPaint
            {
                StartColor = Colors.White,
                EndColor = Colors.White.WithAlpha(0),
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            }, rect);
            canvas.FillRectangle(rect);
            canvas.SetFillPaint(new LinearGradientPaint
            {
                StartColor = Colors.Black.WithAlpha(0),
                EndColor = Colors.Black,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            }, rect);
            canvas.FillRectangle(rect);

            var x = owner.saturation * rect.Width;
            var y = (1 - owner.value) * rect.Height;
            canvas.StrokeColor = Colors.Black.WithAlpha(0.55f);
            canvas.StrokeSize = 4;
            canvas.DrawCircle(x, y, 8);
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 2.5f;
            canvas.DrawCircle(x, y, 8);
        }

        private void DrawHue(ICanvas canvas, RectF rect)
        {
            const int steps = 72;
            var width = rect.Width / steps;
            for (var step = 0; step < steps; step++)
            {
                canvas.FillColor = ToMaui(HsvToColor(step * 360f / steps, 1, 1, 255));
                canvas.FillRectangle(rect.X + step * width, rect.Y, width + 0.5f, rect.Height);
            }
            var x = owner.hue / 360f * rect.Width;
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 3;
            canvas.DrawLine(x, rect.Y, x, rect.Bottom);
            canvas.StrokeColor = Colors.Black.WithAlpha(0.5f);
            canvas.StrokeSize = 1;
            canvas.DrawLine(x - 3, rect.Y, x - 3, rect.Bottom);
            canvas.DrawLine(x + 3, rect.Y, x + 3, rect.Bottom);
        }

        private void DrawAlpha(ICanvas canvas, RectF rect)
        {
            const float cell = 8;
            for (var y = 0; y < rect.Height; y += (int)cell)
            {
                for (var x = 0; x < rect.Width; x += (int)cell)
                {
                    canvas.FillColor = ((int)(x / cell) + (int)(y / cell)) % 2 == 0
                        ? Microsoft.Maui.Graphics.Color.FromArgb("#F2F2F2")
                        : Microsoft.Maui.Graphics.Color.FromArgb("#BDBDBD");
                    canvas.FillRectangle(rect.X + x, rect.Y + y, cell, cell);
                }
            }
            var color = ToMaui(HsvToColor(owner.hue, owner.saturation, owner.value, 255));
            canvas.SetFillPaint(new LinearGradientPaint
            {
                StartColor = color.WithAlpha(0),
                EndColor = color,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            }, rect);
            canvas.FillRectangle(rect);
            var marker = owner.alpha / 255f * rect.Width;
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 3;
            canvas.DrawLine(marker, rect.Y, marker, rect.Bottom);
            canvas.StrokeColor = Colors.Black.WithAlpha(0.5f);
            canvas.StrokeSize = 1;
            canvas.DrawLine(marker - 3, rect.Y, marker - 3, rect.Bottom);
            canvas.DrawLine(marker + 3, rect.Y, marker + 3, rect.Bottom);
        }
    }
}
