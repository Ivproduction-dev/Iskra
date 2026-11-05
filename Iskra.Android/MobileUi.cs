using Microsoft.Maui.Controls.Shapes;

namespace Iskra.Android;

internal static class MobileUi
{
    public static Label Text(string value, double size = 16, bool bold = false, Color? color = null) => new()
    {
        Text = value,
        FontSize = size,
        FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
        TextColor = color ?? MobileTheme.TextPrimary,
        VerticalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap
    };

    public static Border Panel(View content, Thickness? padding = null) => new()
    {
        Content = content,
        Padding = padding ?? new Thickness(16),
        BackgroundColor = MobileTheme.SurfacePanel,
        Stroke = MobileTheme.Border,
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 14 }
    };

    public static Button Action(string text, EventHandler handler, bool secondary = false)
    {
        var button = new Button
        {
            Text = text,
            TextColor = secondary ? MobileTheme.TextBody : Color.FromArgb("#FFFFFF"),
            BackgroundColor = secondary ? MobileTheme.SurfacePanel : MobileTheme.Accent,
            BorderColor = secondary ? MobileTheme.Border : Colors.Transparent,
            BorderWidth = secondary ? 1 : 0,
            CornerRadius = 10,
            Padding = new Thickness(16, 12),
            MinimumHeightRequest = 48,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold
        };
        button.Clicked += handler;
        return button;
    }

    public static Button IconButton(string text, string description, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            TextColor = MobileTheme.TextBody,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(8),
            MinimumWidthRequest = 48,
            MinimumHeightRequest = 48,
            FontSize = 21,
            AutomationId = description
        };
        SemanticProperties.SetDescription(button, description);
        button.Clicked += handler;
        return button;
    }

    public static Grid TopBar(string title, Func<Task>? back = null, Func<Task>? more = null)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(48), new ColumnDefinition(GridLength.Star), new ColumnDefinition(48)),
            Padding = new Thickness(16, 8, 12, 8),
            HeightRequest = 60
        };
        View leading;
        if (back is null)
        {
            leading = new GraphicsView { Drawable = new BrandGlyphDrawable(), WidthRequest = 32, HeightRequest = 32 };
            SemanticProperties.SetDescription(leading, "Логотип Искры");
        }
        else
        {
            leading = IconButton("‹", "Назад", async (_, _) => await back());
        }
        grid.Add(leading, 0, 0);
        var titleLabel = Text(title, 16, true);
        titleLabel.LineBreakMode = LineBreakMode.TailTruncation;
        titleLabel.MaxLines = 1;
        grid.Add(titleLabel, 1, 0);
        if (more is not null)
        {
            grid.Add(IconButton("⋮", "Меню", async (_, _) => await more()), 2, 0);
        }
        return grid;
    }

    public static GraphicsView OrientationPreview(bool vertical) => new()
    {
        Drawable = new OrientationDrawable(vertical),
        HeightRequest = 68,
        WidthRequest = 100,
        HorizontalOptions = LayoutOptions.Center
    };

    private sealed class BrandGlyphDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.FillColor = MobileTheme.Accent;
            canvas.FillRoundedRectangle(1, 1, dirtyRect.Width - 2, dirtyRect.Height - 2, 8);
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 2;
            canvas.DrawRoundedRectangle(5, 5, dirtyRect.Width - 10, dirtyRect.Height - 10, 5);
            var cx = dirtyRect.Width / 2;
            var cy = dirtyRect.Height / 2;
            canvas.StrokeSize = 1.8f;
            canvas.DrawLine(cx, cy - 8, cx, cy + 8);
            canvas.DrawLine(cx - 8, cy, cx + 8, cy);
            canvas.DrawLine(cx - 5.5f, cy - 5.5f, cx + 5.5f, cy + 5.5f);
            canvas.DrawLine(cx + 5.5f, cy - 5.5f, cx - 5.5f, cy + 5.5f);
        }
    }

    private sealed class OrientationDrawable(bool vertical) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var width = vertical ? 32 : 54;
            var height = vertical ? 54 : 34;
            var x = (dirtyRect.Width - width) / 2;
            var y = (dirtyRect.Height - height) / 2;
            canvas.FillColor = MobileTheme.SurfaceSoft;
            canvas.StrokeColor = MobileTheme.TextBody;
            canvas.StrokeSize = 2;
            canvas.FillRoundedRectangle(x, y, width, height, 7);
            canvas.DrawRoundedRectangle(x, y, width, height, 7);
            canvas.FillColor = MobileTheme.Accent;
            if (vertical)
            {
                canvas.FillEllipse(x + width / 2 - 2, y + height - 8, 4, 4);
            }
            else
            {
                canvas.DrawLine(x + width / 2 - 8, y + height + 4, x + width / 2 + 8, y + height + 4);
                canvas.DrawLine(x + width / 2, y + height, x + width / 2, y + height + 4);
            }
        }
    }
}
