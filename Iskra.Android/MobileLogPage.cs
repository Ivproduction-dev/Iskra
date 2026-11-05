namespace Iskra.Android;

internal sealed class MobileLogPage : ContentPage
{
    private readonly Label content = new()
    {
        FontSize = 12,
        TextColor = Color.FromArgb("#DFDBD3"),
        LineBreakMode = LineBreakMode.WordWrap
    };

    public MobileLogPage()
    {
        Title = "Журнал";
        BackgroundColor = MobileTheme.SurfacePrimary;
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        NavigationPage.SetHasNavigationBar(this, false);
        var header = MobileUi.TopBar("Журнал", back: async () => await Navigation.PopAsync());
        var actions = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(20, 8) };
        actions.Add(MobileUi.IconButton("↻", "Обновить журнал", (_, _) => Load()));
        var root = new Grid
        {
            RowDefinitions = new RowDefinitionCollection(new RowDefinition(60), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star))
        };
        root.Add(header, 0, 0);
        root.Add(actions, 1, 0);
        root.Add(new ScrollView { Content = content }, 2, 0);
        Content = root;
        Load();
    }

    private void Load()
    {
        var text = MobileLog.Read();
        content.Text = string.IsNullOrWhiteSpace(text) ? "Журнал пуст." : text;
    }
}
