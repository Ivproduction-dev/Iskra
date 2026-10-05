using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace IskraStudio;

public partial class MainWindow
{
    private void ShowSceneListPage()
    {
        if (activeProject is null)
        {
            return;
        }

        showingObjects = false;
        showingDetail = false;
        showingPainting = false;
        activeScene = null;
        StopDetailMedia();
        workspaceSelectionMode = WorkspaceSelectionMode.None;
        selectedSceneIds.Clear();
        WorkspaceTitle.Text = "Список сцен";
        WorkspaceBackButton.Content = "←  Проекты";
        WorkspaceMenuButton.ToolTip = "Действия со сценами";
        WorkspaceMenuButton.Visibility = Visibility.Visible;
        WorkspaceConfirmSelectionButton.Visibility = Visibility.Collapsed;
        WorkspaceCancelSelectionButton.Visibility = Visibility.Collapsed;
        CreateProjectButton.Visibility = Visibility.Visible;
        UpdateFloatingCreateButton();
        BuildSceneList();
    }

    private void BuildSceneList()
    {
        WorkspaceContentHost.Children.Clear();
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var content = new StackPanel
        {
            Margin = new Thickness(32, 30, 32, 110),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        BindingOperations.SetBinding(content, FrameworkElement.WidthProperty, new Binding(nameof(ScrollViewer.ActualWidth))
        {
            Source = scrollViewer,
            Converter = new InsetWidthConverter(),
            ConverterParameter = 64.0
        });
        if (activeProject is null || activeProject.Scenes.Count == 0)
        {
            content.Children.Add(new Border
            {
                Background = BrushFor("PanelBrush"),
                BorderBrush = BrushFor("HairlineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(28),
                Child = new TextBlock
                {
                    Text = "Нет сцен. Создайте первую через +",
                    FontSize = 14,
                    Foreground = BrushFor("MutedBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            });
        }
        else
        {
            foreach (var scene in activeProject.Scenes.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                content.Children.Add(CreateSceneRow(scene));
            }
        }

        scrollViewer.Content = content;
        WorkspaceContentHost.Children.Add(scrollViewer);
    }

    private UIElement CreateSceneRow(IskraScene scene)
    {
        var row = new Grid { Height = 124, Margin = new Thickness(0, 0, 0, 24) };
        var openButton = new Button
        {
            Style = (Style)FindResource("ProjectCardButton"),
            Height = 124,
            Tag = scene,
            Padding = new Thickness(0),
            IsHitTestVisible = workspaceSelectionMode == WorkspaceSelectionMode.None
        };
        openButton.Click += SceneRow_Click;
        AutomationProperties.SetName(openButton, $"Открыть сцену {scene.Name}");

        var main = new Grid { Margin = new Thickness(96, 0, 64, 0) };
        var name = new TextBlock
        {
            Text = scene.Name,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFor("InkBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        main.Children.Add(name);
        openButton.Content = main;
        row.Children.Add(openButton);

        var selectionButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Width = 48,
            Height = 124,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = workspaceSelectionMode == WorkspaceSelectionMode.None ? Visibility.Collapsed : Visibility.Visible,
            Tag = scene
        };
        selectionButton.Click += ToggleSceneSelection_Click;
        AutomationProperties.SetName(selectionButton, $"Выбрать сцену {scene.Name}");
        var isSelected = selectedSceneIds.Contains(scene.Id);
        selectionButton.Content = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(5),
            BorderBrush = BrushFor(isSelected ? "AccentBrush" : "MutedBrush"),
            BorderThickness = new Thickness(1.5),
            Background = isSelected ? BrushFor("AccentBrush") : BrushFor("PanelBrush"),
            Child = isSelected ? new TextBlock
            {
                Text = "✓",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = BrushFor("OnAccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            } : null
        };
        row.Children.Add(selectionButton);

        var actionsButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Width = 44,
            Height = 52,
            Padding = new Thickness(0),
            Content = "⋮",
            FontSize = 20,
            ToolTip = $"Действия со сценой {scene.Name}",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Tag = scene
        };
        actionsButton.Click += EntryActions_Click;
        AutomationProperties.SetName(actionsButton, $"Действия со сценой {scene.Name}");
        row.Children.Add(actionsButton);
        return row;
    }

    private void SceneRow_Click(object sender, RoutedEventArgs e)
    {
        if (workspaceSelectionMode != WorkspaceSelectionMode.None || sender is not Button { Tag: IskraScene scene })
        {
            return;
        }
        ShowObjectListPage(scene);
    }

    private void SceneTools_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || activeProject is null)
        {
            return;
        }

        WorkspaceDeleteActionMenuItem.Content = "Удалить";
        WorkspaceRenameActionMenuItem.Content = showingObjects ? "Переименовать сцену" : "Переименовать сцену";
        WorkspaceToolsPopup.PlacementTarget = button;
        WorkspaceToolsPopup.HorizontalOffset = -(280 - button.ActualWidth);
        WorkspaceToolsPopup.IsOpen = true;
    }

    private void BeginDeleteScenes_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceToolsPopup.IsOpen = false;
        if (showingObjects && activeScene is not null)
        {
            RequestDeleteScenes([activeScene]);
            return;
        }

        workspaceSelectionMode = WorkspaceSelectionMode.DeleteScenes;
        selectedSceneIds.Clear();
        WorkspaceConfirmSelectionButton.Content = "✓";
        WorkspaceConfirmSelectionButton.ToolTip = "Удалить";
        WorkspaceConfirmSelectionButton.Visibility = Visibility.Visible;
        WorkspaceCancelSelectionButton.Visibility = Visibility.Visible;
        WorkspaceMenuButton.Visibility = Visibility.Collapsed;
        BuildSceneList();
    }

    private void BeginRenameScene_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceToolsPopup.IsOpen = false;
        if (showingObjects && activeScene is not null)
        {
            StartRenameScene(activeScene);
            return;
        }

        workspaceSelectionMode = WorkspaceSelectionMode.RenameScene;
        selectedSceneIds.Clear();
        WorkspaceConfirmSelectionButton.Content = "✓";
        WorkspaceConfirmSelectionButton.ToolTip = "Переименовать выбранную сцену";
        WorkspaceConfirmSelectionButton.Visibility = Visibility.Visible;
        WorkspaceCancelSelectionButton.Visibility = Visibility.Visible;
        WorkspaceMenuButton.Visibility = Visibility.Collapsed;
        BuildSceneList();
    }

    private void WorkspaceProjectOptions_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceToolsPopup.IsOpen = false;
        ShowProjectDialog(ProjectDialogMode.Options, activeProject);
    }

    private void ToggleSceneSelection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IskraScene scene })
        {
            return;
        }

        if (workspaceSelectionMode == WorkspaceSelectionMode.RenameScene)
        {
            selectedSceneIds.Clear();
            selectedSceneIds.Add(scene.Id);
        }
        else if (workspaceSelectionMode == WorkspaceSelectionMode.DeleteScenes && !selectedSceneIds.Add(scene.Id))
        {
            selectedSceneIds.Remove(scene.Id);
        }
        BuildSceneList();
    }

    private void ConfirmSceneSelection_Click(object sender, RoutedEventArgs e)
    {
        if (activeProject is null || selectedSceneIds.Count == 0)
        {
            return;
        }

        var selection = activeProject.Scenes.Where(scene => selectedSceneIds.Contains(scene.Id)).ToList();
        if (workspaceSelectionMode == WorkspaceSelectionMode.RenameScene)
        {
            EndSceneSelection();
            StartRenameScene(selection[0]);
        }
        else if (workspaceSelectionMode == WorkspaceSelectionMode.DeleteScenes)
        {
            RequestDeleteScenes(selection);
        }
    }

    private void CancelSceneSelection_Click(object sender, RoutedEventArgs e) => EndSceneSelection();

    private void EndSceneSelection()
    {
        workspaceSelectionMode = WorkspaceSelectionMode.None;
        selectedSceneIds.Clear();
        WorkspaceConfirmSelectionButton.Visibility = Visibility.Collapsed;
        WorkspaceCancelSelectionButton.Visibility = Visibility.Collapsed;
        WorkspaceMenuButton.Visibility = Visibility.Visible;
        BuildSceneList();
    }

    private Brush BrushFor(string key) => (Brush)FindResource(key);
}
