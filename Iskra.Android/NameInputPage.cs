using Microsoft.Maui.Controls.Shapes;

namespace Iskra.Android;

internal sealed class NameInputPage : ContentPage
{
    private readonly Entry name = new() { MaxLength = 50, ReturnType = ReturnType.Done };
    private readonly Label error = MobileUi.Text("", 14, color: MobileTheme.Error);
    private readonly Func<string, Task<string?>> submit;

    public NameInputPage(string title, string description, string initialValue, Func<string, Task<string?>> submit)
    {
        this.submit = submit;
        BackgroundColor = MobileTheme.SurfacePrimary;
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        NavigationPage.SetHasNavigationBar(this, false);
        name.Text = initialValue;
        name.Placeholder = "Имя";
        name.TextColor = MobileTheme.TextPrimary;
        name.PlaceholderColor = MobileTheme.TextMuted;
        name.BackgroundColor = MobileTheme.SurfacePanel;
        var heading = MobileUi.TopBar(title, back: CancelAsync);
        var body = new VerticalStackLayout { Padding = new Thickness(20, 22, 20, 24), Spacing = 14 };
        body.Add(MobileUi.Text(title, 28));
        body.Add(MobileUi.Text(description, 15, color: MobileTheme.TextMuted));
        body.Add(MobileUi.Text("Имя", 14, true));
        body.Add(new Border
        {
            Content = name,
            Padding = new Thickness(12, 0),
            BackgroundColor = MobileTheme.SurfacePanel,
            Stroke = MobileTheme.Border,
            StrokeShape = new RoundRectangle { CornerRadius = 10 }
        });
        body.Add(error);
        body.Add(new Grid { HeightRequest = 24 });
        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)),
            ColumnSpacing = 12
        };
        actions.Add(MobileUi.Action("Отмена", async (_, _) => await CancelAsync(), secondary: true), 0, 0);
        actions.Add(MobileUi.Action("Готово", async (_, _) => await SubmitAsync()), 1, 0);
        body.Add(actions);
        var root = new Grid { RowDefinitions = new RowDefinitionCollection(new RowDefinition(60), new RowDefinition(GridLength.Star)) };
        root.Add(heading, 0, 0);
        root.Add(new ScrollView { Content = body }, 0, 1);
        Content = root;
    }

    private async Task SubmitAsync()
    {
        var message = await submit(name.Text ?? string.Empty);
        if (message is null)
        {
            await Navigation.PopModalAsync();
            return;
        }
        error.Text = message;
    }

    private async Task CancelAsync() => await Navigation.PopModalAsync();
}
