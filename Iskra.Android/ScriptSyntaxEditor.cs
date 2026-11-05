namespace Iskra.Android;

internal sealed class ScriptSyntaxEditor : Editor
{
    private readonly Label lineIndicator = new()
    {
        FontSize = 11,
        TextColor = Color.FromArgb("#AAA59C"),
        HorizontalOptions = LayoutOptions.End,
        Margin = new Thickness(0, 2, 2, 0)
    };

    public ScriptSyntaxEditor()
    {
        TextChanged += (_, _) => UpdateLine();
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CursorPosition)) UpdateLine();
        };
        var stack = new VerticalStackLayout { Spacing = 0 };
        Loaded += (_, _) =>
        {
            if (Parent is Grid grid)
            {
                grid.Children.Add(lineIndicator);
                Grid.SetRow(lineIndicator, Grid.GetRow(this));
                Grid.SetRowSpan(lineIndicator, Grid.GetRowSpan(this));
                lineIndicator.HorizontalOptions = LayoutOptions.End;
                lineIndicator.VerticalOptions = LayoutOptions.End;
                lineIndicator.InputTransparent = true;
            }
        };
        UpdateLine();
    }

    private void UpdateLine()
    {
        var text = Text ?? string.Empty;
        var position = Math.Clamp(CursorPosition, 0, text.Length);
        var line = 1;
        for (var index = 0; index < position && index < text.Length; index++)
        {
            if (text[index] == '\n') line++;
        }
        var total = 1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n') total++;
        }
        lineIndicator.Text = $"Строка {line} из {total}";
    }
}

internal sealed record ScriptSuggestion(string Prefix, string Command, string? Placeholder = null);
