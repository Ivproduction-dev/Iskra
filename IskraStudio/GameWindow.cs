using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IskraStudio;

internal sealed class GameWindow : Window
{
    public Grid SceneSurface { get; }

    public GameWindow(ProjectOrientation orientation)
    {
        Title = "Искра — игра";
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = true;
        MinWidth = orientation == ProjectOrientation.Horizontal ? 420 : 320;
        MinHeight = orientation == ProjectOrientation.Horizontal ? 300 : 420;
        Width = orientation == ProjectOrientation.Horizontal ? 1024 : 520;
        Height = orientation == ProjectOrientation.Horizontal ? 640 : 960;

        var width = orientation == ProjectOrientation.Horizontal ? 1920 : 1080;
        var height = orientation == ProjectOrientation.Horizontal ? 1080 : 1920;
        SceneSurface = new Grid
        {
            Width = width,
            Height = height,
            Background = Brushes.Black,
            ClipToBounds = true
        };

        var viewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = SceneSurface,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Content = new Grid { Background = Brushes.Black, Children = { viewbox } };
    }
}
