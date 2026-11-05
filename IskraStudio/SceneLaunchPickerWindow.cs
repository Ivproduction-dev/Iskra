using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IskraStudio;

internal sealed class SceneLaunchPickerWindow : Window
{
    private readonly ListBox scenes = new();

    public SceneLaunchPickerWindow(IReadOnlyList<IskraScene> availableScenes, IskraScene? currentScene)
    {
        Title = "Выбор сцены";
        Width = 500;
        Height = 600;
        MinWidth = 380;
        MinHeight = 420;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("CanvasBrush", Color.FromRgb(250, 249, 245));
        Foreground = Brush("TextPrimaryBrush", Color.FromRgb(20, 20, 19));
        ShowInTaskbar = false;

        scenes.ItemsSource = availableScenes;
        scenes.DisplayMemberPath = nameof(IskraScene.Name);
        scenes.SelectedItem = currentScene is null
            ? availableScenes[0]
            : availableScenes.FirstOrDefault(scene => scene.Id == currentScene.Id) ?? availableScenes[0];
        scenes.BorderThickness = new Thickness(0);
        scenes.Background = Brushes.Transparent;
        scenes.FontSize = 16;
        scenes.Margin = new Thickness(0, 16, 0, 16);

        var title = new TextBlock
        {
            Text = "Какую сцену запустить?",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        };
        var subtitle = new TextBlock
        {
            Text = "Выбери сцену из списка. Текущая сцена уже выделена.",
            FontSize = 14,
            Foreground = Brush("TextMutedBrush", Color.FromRgb(108, 106, 100)),
            Margin = new Thickness(0, 6, 0, 0)
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Отмена", MinWidth = 96, MinHeight = 42, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var launch = new Button
        {
            Content = "Запустить",
            MinWidth = 112,
            MinHeight = 42,
            IsDefault = true,
            Background = Brush("AccentBrush", Color.FromRgb(169, 88, 62)),
            Foreground = Brushes.White
        };
        launch.Click += (_, _) =>
        {
            if (scenes.SelectedItem is not IskraScene)
            {
                return;
            }
            DialogResult = true;
        };
        scenes.MouseDoubleClick += (_, _) =>
        {
            if (scenes.SelectedItem is IskraScene) DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(launch);

        var layout = new Grid { Margin = new Thickness(24) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(title);
        Grid.SetRow(subtitle, 1);
        layout.Children.Add(subtitle);
        Grid.SetRow(scenes, 2);
        layout.Children.Add(scenes);
        Grid.SetRow(buttons, 3);
        layout.Children.Add(buttons);
        Content = layout;
    }

    public IskraScene? SelectedScene => scenes.SelectedItem as IskraScene;

    private static Brush Brush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
}
