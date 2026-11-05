using Microsoft.Extensions.DependencyInjection;

namespace Iskra.Android;

internal sealed class SettingsPage : ContentPage
{
    private readonly string[] themes = ["Системная", "Светлая", "Тёмная"];

    public SettingsPage()
    {
        Title = "Настройки";
        BackgroundColor = MobileTheme.SurfacePrimary;
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        NavigationPage.SetHasNavigationBar(this, false);
        var header = MobileUi.TopBar("Настройки", back: async () => await Navigation.PopAsync());
        var body = new VerticalStackLayout { Padding = new Thickness(20, 18), Spacing = 12 };
        body.Add(MobileUi.Text("Тема оформления", 24, true));
        body.Add(MobileUi.Text("Применяется сразу и запоминается.", 14, color: MobileTheme.TextMuted));
        var current = Preferences.Get("theme", "auto");
        foreach (var theme in themes)
        {
            var button = new Button
            {
                Text = theme,
                FontSize = 16,
                CornerRadius = 12,
                Padding = new Thickness(12, 10),
                BackgroundColor = Matches(current, theme) ? MobileTheme.Accent : MobileTheme.SurfaceSelected,
                TextColor = Matches(current, theme) ? Colors.White : MobileTheme.TextPrimary,
                HorizontalOptions = LayoutOptions.Fill
            };
            button.Clicked += (_, _) =>
            {
                Preferences.Set("theme", Key(theme));
                Apply(theme);
                Refresh(body);
            };
            body.Add(button);
        }
        var root = new Grid { RowDefinitions = new RowDefinitionCollection(new RowDefinition(60), new RowDefinition(GridLength.Star)) };
        root.Add(header, 0, 0);
        root.Add(new ScrollView { Content = body }, 1, 0);
        Content = root;
    }

    private static bool Matches(string current, string theme) => current == Key(theme);

    private static string Key(string theme) => theme switch
    {
        "Светлая" => "light",
        "Тёмная" => "dark",
        _ => "auto"
    };

    private static void Apply(string theme)
    {
        if (Application.Current is null) return;
        Application.Current.UserAppTheme = Key(theme) switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
    }

    private void Refresh(VerticalStackLayout body)
    {
        var current = Preferences.Get("theme", "auto");
        var index = 0;
        foreach (var child in body.Children.OfType<Button>())
        {
            var theme = themes[index++];
            child.BackgroundColor = Matches(current, theme) ? MobileTheme.Accent : MobileTheme.SurfaceSelected;
            child.TextColor = Matches(current, theme) ? Colors.White : MobileTheme.TextPrimary;
        }
    }
}
