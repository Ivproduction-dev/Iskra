using IskraStudio;
using Microsoft.Maui.Controls.Shapes;

namespace Iskra.Android;

public sealed class CreateProjectPage : ContentPage
{
    private readonly ProjectStore projectStore;
    private readonly Entry nameEntry = new() { Placeholder = "Например, Моя игра", MaxLength = 50, ReturnType = ReturnType.Done };
    private readonly Label errorLabel = MobileUi.Text("", 14, color: MobileTheme.Error);
    private ProjectOrientation orientation = ProjectOrientation.Horizontal;
    private Border horizontalCard = null!;
    private Border verticalCard = null!;

    public event Action<IskraProject>? Completed;
    public event Func<Task>? Cancelled;

    public CreateProjectPage(ProjectStore projectStore)
    {
        this.projectStore = projectStore;
        Title = "Новый проект";
        BackgroundColor = MobileTheme.SurfacePrimary;
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        NavigationPage.SetHasNavigationBar(this, false);

        var heading = MobileUi.TopBar("Новый проект", back: CancelAsync);
        var body = new VerticalStackLayout { Padding = new Thickness(20, 18, 20, 24), Spacing = 10 };
        body.Add(MobileUi.Text("Задай имя и формат будущей сцены", 15, color: MobileTheme.TextMuted));
        body.Add(MobileUi.Text("Имя проекта", 14, true));

        nameEntry.BackgroundColor = MobileTheme.SurfacePanel;
        nameEntry.HeightRequest = 52;
        nameEntry.ClearButtonVisibility = ClearButtonVisibility.WhileEditing;
        nameEntry.TextColor = MobileTheme.TextPrimary;
        nameEntry.PlaceholderColor = MobileTheme.TextMuted;
        body.Add(new Border
        {
            Content = nameEntry,
            Padding = new Thickness(12, 0),
            BackgroundColor = MobileTheme.SurfacePanel,
            Stroke = MobileTheme.Border,
            StrokeShape = new RoundRectangle { CornerRadius = 10 }
        });

        body.Add(MobileUi.Text("Ориентация сцены", 14, true));
        var orientations = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)),
            ColumnSpacing = 12,
            Margin = new Thickness(0, 2, 0, 4)
        };
        horizontalCard = OrientationCard("Горизонтальная", false);
        verticalCard = OrientationCard("Вертикальная", true);
        orientations.Add(horizontalCard, 0, 0);
        orientations.Add(verticalCard, 1, 0);
        body.Add(orientations);
        body.Add(errorLabel);
        body.Add(new Grid { VerticalOptions = LayoutOptions.Fill, HeightRequest = 20 });

        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)),
            ColumnSpacing = 12
        };
        actions.Add(MobileUi.Action("Отмена", async (_, _) => await CancelAsync(), secondary: true), 0, 0);
        actions.Add(MobileUi.Action("Создать", (_, _) => Create(), secondary: false), 1, 0);
        body.Add(actions);
        var scroll = new ScrollView { Content = body, VerticalOptions = LayoutOptions.Fill };
        var root = new Grid { RowDefinitions = new RowDefinitionCollection(new RowDefinition(60), new RowDefinition(GridLength.Star)) };
        root.Add(heading, 0, 0);
        root.Add(scroll, 0, 1);
        Content = root;
        UpdateOrientationCards();
    }

    private Border OrientationCard(string label, bool vertical)
    {
        var stack = new VerticalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Fill };
        stack.Add(MobileUi.OrientationPreview(vertical));
        stack.Add(MobileUi.Text(label, 14, true));
        var card = new Border
        {
            Content = stack,
            Padding = new Thickness(8, 12),
            BackgroundColor = MobileTheme.SurfacePanel,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            HeightRequest = 140
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            orientation = vertical ? ProjectOrientation.Vertical : ProjectOrientation.Horizontal;
            UpdateOrientationCards();
        };
        card.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(card, $"{label} ориентация");
        return card;
    }

    private void UpdateOrientationCards()
    {
        horizontalCard.Stroke = orientation == ProjectOrientation.Horizontal ? MobileTheme.Accent : MobileTheme.Border;
        horizontalCard.StrokeThickness = orientation == ProjectOrientation.Horizontal ? 2 : 1;
        horizontalCard.BackgroundColor = orientation == ProjectOrientation.Horizontal ? MobileTheme.AccentSoft : MobileTheme.SurfacePanel;
        verticalCard.Stroke = orientation == ProjectOrientation.Vertical ? MobileTheme.Accent : MobileTheme.Border;
        verticalCard.StrokeThickness = orientation == ProjectOrientation.Vertical ? 2 : 1;
        verticalCard.BackgroundColor = orientation == ProjectOrientation.Vertical ? MobileTheme.AccentSoft : MobileTheme.SurfacePanel;
    }

    private void Create()
    {
        var projects = projectStore.LoadAll(out _);
        if (!projectStore.TryCreate(nameEntry.Text ?? string.Empty, orientation, projects, out var project, out var error) || project is null)
        {
            errorLabel.Text = error ?? "Не удалось создать проект.";
            return;
        }
        Completed?.Invoke(project);
    }

    private async Task CancelAsync()
    {
        if (Cancelled is not null)
        {
            await Cancelled.Invoke();
        }
    }
}
