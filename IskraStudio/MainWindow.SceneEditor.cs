using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IskraStudio;

public partial class MainWindow
{
    private void ShowObjectListPage(IskraScene scene)
    {
        if (activeProject is null)
        {
            return;
        }

        activeScene = scene;
        showingObjects = true;
        showingDetail = false;
        StopDetailMedia();
        workspaceSelectionMode = WorkspaceSelectionMode.None;
        selectedSceneIds.Clear();
        WorkspaceTitle.Text = scene.Name;
        WorkspaceBackButton.Content = "←  Список сцен";
        WorkspaceMenuButton.ToolTip = "Действия со сценой";
        WorkspaceMenuButton.Visibility = Visibility.Visible;
        WorkspaceConfirmSelectionButton.Visibility = Visibility.Collapsed;
        WorkspaceCancelSelectionButton.Visibility = Visibility.Collapsed;
        CreateProjectButton.Visibility = Visibility.Visible;
        UpdateFloatingCreateButton();
        BuildObjectList();
    }

    private void BuildObjectList()
    {
        WorkspaceContentHost.Children.Clear();
        if (activeScene is null)
        {
            return;
        }

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

        content.Children.Add(CreateBackgroundRow());

        content.Children.Add(new Border
        {
            Height = 1,
            Background = BrushFor("HairlineBrush"),
            Margin = new Thickness(0, 22, 0, 18)
        });
        content.Children.Add(new TextBlock
        {
            Text = "Объекты",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFor("InkBrush"),
            Margin = new Thickness(0, 0, 0, 14)
        });

        if (activeScene.Objects.Count == 0)
        {
            content.Children.Add(new Border
            {
                Background = BrushFor("PanelBrush"),
                BorderBrush = BrushFor("HairlineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22),
                Child = new TextBlock
                {
                    Text = "Нет объектов",
                    FontSize = 14,
                    Foreground = BrushFor("MutedBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            });
        }
        else
        {
            foreach (var item in activeScene.Objects.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                content.Children.Add(CreateObjectRow(item));
            }
        }

        scrollViewer.Content = content;
        WorkspaceContentHost.Children.Add(scrollViewer);
    }

    private UIElement CreateBackgroundRow()
    {
        var background = new IskraObject { Id = IskraObject.BackgroundId, Name = "Фон" };
        return CreateEntryRow("Фон", null, "Фон сцены", FindObjectPreviewPath(background.Id), () =>
        {
            if (activeScene is not null)
            {
                ShowObjectDetail(activeScene, background);
            }
        });
    }

    private UIElement CreateObjectRow(IskraObject item)
    {
        var actionsButton = new Button
        {
            Style = (Style)FindResource("ToolButton"),
            Width = 44,
            Height = 52,
            Padding = new Thickness(0),
            Content = "⋮",
            FontSize = 20,
            ToolTip = $"Действия с объектом {item.Name}",
            Tag = item
        };
        actionsButton.Click += EntryActions_Click;
        AutomationProperties.SetName(actionsButton, $"Действия с объектом {item.Name}");
        return CreateEntryRow(item.Name, actionsButton, $"Объект {item.Name}", FindObjectPreviewPath(item.Id), () =>
        {
            if (activeScene is not null)
            {
                ShowObjectDetail(activeScene, item);
            }
        });
    }

    private string? FindObjectPreviewPath(Guid objectId)
    {
        if (activeProject is null || activeScene is null)
        {
            return null;
        }
        var directory = projectContentStore.GetObjectResourceDirectory(activeProject, activeScene, objectId, "sprites");
        return ProjectContentStore.FindFirstPreviewImage(directory);
    }

    private static ImageSource? LoadPreviewImage(string? path)
    {
        if (path is null)
        {
            return null;
        }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 192;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static Button? FindButtonAncestor(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Button button)
            {
                return button;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private UIElement CreateEntryRow(string name, Button? actionsButton, string automationName, string? previewPath, Action? open)
    {
        var row = new Border
        {
            Height = 124,
            Margin = new Thickness(0, 0, 0, 24),
            Background = BrushFor("PanelBrush"),
            BorderBrush = BrushFor("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };
        var layout = new Grid { Margin = new Thickness(12, 0, 10, 0) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (actionsButton is not null)
        {
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        layout.Children.Add(new Border
        {
            Width = 96,
            Height = 96,
            CornerRadius = new CornerRadius(10),
            Background = BrushFor("SoftBrush"),
            ClipToBounds = true,
            Child = LoadPreviewImage(previewPath) is { } preview
                ? new Image { Source = preview, Stretch = Stretch.UniformToFill }
                : (UIElement)new TextBlock
                {
                    Text = "◇",
                    FontSize = 46,
                    Foreground = BrushFor("AccentBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
        });
        var nameBlock = new TextBlock
        {
            Text = name,
            FontSize = 19,
            FontWeight = FontWeights.Medium,
            Foreground = BrushFor("InkBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameBlock, 1);
        layout.Children.Add(nameBlock);
        if (actionsButton is not null)
        {
            Grid.SetColumn(actionsButton, 2);
            layout.Children.Add(actionsButton);
        }
        row.Child = layout;
        AutomationProperties.SetName(row, automationName);
        if (open is not null)
        {
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (FindButtonAncestor(e.OriginalSource as DependencyObject) is not null)
                {
                    return;
                }
                open();
            };
        }
        return row;
    }

    private void UpdateFloatingCreateButton()
    {
        var text = showingObjects ? "Создать объект" :
            WorkspaceView.Visibility == Visibility.Visible ? "Создать сцену" : "Создать проект";
        CreateProjectButton.ToolTip = text;
        AutomationProperties.SetName(CreateProjectButton, text);
    }

    private void ShowCreateSceneDialog()
    {
        if (activeProject is null)
        {
            return;
        }
        entryDialogMode = EntryDialogMode.CreateScene;
        sceneBeingEdited = null;
        ShowEntityDialog("Новая сцена", "Задай имя сцены. Если оставить поле пустым, Искра выберет свободное имя.", string.Empty, "Добавить");
    }

    private void ShowCreateObjectDialog(IskraScene scene)
    {
        entryDialogMode = EntryDialogMode.CreateObject;
        sceneBeingEdited = scene;
        ShowEntityDialog("Новый объект", "Введи имя объекта. Его содержимое можно будет настроить позже.", string.Empty, "Добавить");
    }

    private void StartRenameScene(IskraScene scene)
    {
        if (workspaceSelectionMode != WorkspaceSelectionMode.None)
        {
            EndSceneSelection();
        }
        entryDialogMode = EntryDialogMode.RenameScene;
        entryBeingRenamed = scene;
        sceneBeingEdited = scene;
        ShowEntityDialog("Переименовать сцену", "Укажи новое имя сцены и проверь его перед подтверждением.", scene.Name, "Продолжить");
    }

    private void StartRenameObject(IskraScene scene, IskraObject item)
    {
        entryDialogMode = EntryDialogMode.RenameObject;
        entryBeingRenamed = item;
        sceneBeingEdited = scene;
        ShowEntityDialog("Переименовать объект", "Укажи новое имя объекта.", item.Name, "Продолжить");
    }

    private void ShowEntityDialog(string title, string subtitle, string name, string saveLabel)
    {
        EntityDialogTitle.Text = title;
        EntityDialogSubtitle.Text = subtitle;
        EntityNameBox.Text = name;
        EntityNameError.Text = string.Empty;
        EntityNameError.Visibility = Visibility.Collapsed;
        EntityDialogSaveButton.Content = saveLabel;
        EntityDialogOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => EntityNameBox.Focus()));
    }

    private void SaveEntityDialog_Click(object sender, RoutedEventArgs e)
    {
        if (activeProject is null)
        {
            return;
        }

        var input = EntityNameBox.Text;
        switch (entryDialogMode)
        {
            case EntryDialogMode.CreateScene:
                if (!projectContentStore.TryCreateScene(activeProject, input, out _, out var sceneError))
                {
                    ShowEntityNameError(sceneError);
                    return;
                }
                CloseEntityDialog();
                ShowSceneListPage();
                return;
            case EntryDialogMode.CreateObject:
                if (sceneBeingEdited is null)
                {
                    ShowEntityNameError("Не удалось найти сцену.");
                    return;
                }
                if (!projectContentStore.TryCreateObject(activeProject, sceneBeingEdited, input, out _, out var objectError))
                {
                    ShowEntityNameError(objectError);
                    return;
                }
                CloseEntityDialog();
                BuildObjectList();
                return;
            case EntryDialogMode.RenameScene:
            case EntryDialogMode.RenameObject:
                pendingRenameName = input.Trim();
                if (pendingRenameName.Length == 0)
                {
                    ShowEntityNameError("Введите новое имя.");
                    return;
                }

                RenameOldName.Text = entryBeingRenamed switch
                {
                    IskraScene scene => scene.Name,
                    IskraObject item => item.Name,
                    _ => string.Empty
                };
                RenameNewName.Text = pendingRenameName;
                RenameConfirmError.Text = string.Empty;
                RenameConfirmError.Visibility = Visibility.Collapsed;
                CloseEntityDialog();
                RenameConfirmOverlay.Visibility = Visibility.Visible;
                return;
        }
    }

    private void ConfirmRename_Click(object sender, RoutedEventArgs e)
    {
        if (activeProject is null || sceneBeingEdited is null)
        {
            return;
        }

        string? error;
        var success = entryBeingRenamed switch
        {
            IskraScene scene => projectContentStore.TryRenameScene(activeProject, scene, pendingRenameName, out error),
            IskraObject item => projectContentStore.TryRenameObject(activeProject, sceneBeingEdited, item, pendingRenameName, out error),
            _ => FailRename(out error)
        };
        if (!success)
        {
            RenameConfirmError.Text = error ?? "Не удалось переименовать элемент.";
            RenameConfirmError.Visibility = Visibility.Visible;
            return;
        }

        RenameConfirmOverlay.Visibility = Visibility.Collapsed;
        entryBeingRenamed = null;
        pendingRenameName = string.Empty;
        if (showingObjects && activeScene is not null)
        {
            WorkspaceTitle.Text = activeScene.Name;
            BuildObjectList();
        }
        else
        {
            BuildSceneList();
        }
    }

    private static bool FailRename(out string? error)
    {
        error = "Не удалось найти элемент для переименования.";
        return false;
    }

    private void CancelRename_Click(object sender, RoutedEventArgs e)
    {
        RenameConfirmOverlay.Visibility = Visibility.Collapsed;
        entryBeingRenamed = null;
        sceneBeingEdited = null;
        pendingRenameName = string.Empty;
    }

    private void RenameConfirmOverlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, RenameConfirmOverlay))
        {
            CancelRename_Click(sender, e);
        }
    }

    private void EntityDialogOverlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, EntityDialogOverlay))
        {
            CloseEntityDialog();
        }
    }

    private void CloseEntityDialog_Click(object sender, RoutedEventArgs e) => CloseEntityDialog();

    private void CloseEntityDialog()
    {
        EntityDialogOverlay.Visibility = Visibility.Collapsed;
        if (entryDialogMode is EntryDialogMode.CreateScene or EntryDialogMode.CreateObject)
        {
            sceneBeingEdited = null;
            entryBeingRenamed = null;
        }
    }

    private void ShowEntityNameError(string? error)
    {
        EntityNameError.Text = error ?? "Не удалось сохранить изменения.";
        EntityNameError.Visibility = Visibility.Visible;
        EntityNameBox.Focus();
    }

    private void EntryActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: object entry } button)
        {
            return;
        }
        entryForActions = entry;
        sceneForActions = entry is IskraScene scene ? scene : activeScene;
        SceneActionsPopup.PlacementTarget = button;
        SceneActionsPopup.HorizontalOffset = -(208 - button.ActualWidth);
        SceneActionsPopup.IsOpen = true;
        e.Handled = true;
    }

    private void RenameEntry_Click(object sender, RoutedEventArgs e)
    {
        SceneActionsPopup.IsOpen = false;
        switch (entryForActions)
        {
            case IskraScene scene:
                StartRenameScene(scene);
                break;
            case IskraObject item when sceneForActions is not null:
                StartRenameObject(sceneForActions, item);
                break;
        }
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        SceneActionsPopup.IsOpen = false;
        switch (entryForActions)
        {
            case IskraScene scene:
                RequestDeleteScenes([scene]);
                break;
            case IskraObject item when sceneForActions is not null:
                RequestDeleteObject(sceneForActions, item);
                break;
        }
    }

    private void RequestDeleteScenes(List<IskraScene> scenes)
    {
        scenesBeingDeleted = scenes;
        objectBeingDeleted = null;
        objectDeletionScene = null;
        DeleteEntryTitle.Text = scenes.Count == 1 ? "Удалить сцену?" : $"Удалить сцены ({scenes.Count})?";
        var names = string.Join(", ", scenes.Select(scene => $"«{scene.Name}»"));
        DeleteEntryMessage.Text = $"Будут удалены {names} и все ресурсы этих сцен. Это действие нельзя отменить.";
        ShowDeleteConfirmation();
    }

    private void RequestDeleteObject(IskraScene scene, IskraObject item)
    {
        scenesBeingDeleted = [];
        objectBeingDeleted = item;
        objectDeletionScene = scene;
        DeleteEntryTitle.Text = "Удалить объект?";
        DeleteEntryMessage.Text = $"Объект «{item.Name}» и его ресурсы будут удалены из сцены «{scene.Name}». Это действие нельзя отменить.";
        ShowDeleteConfirmation();
    }

    private void ShowDeleteConfirmation()
    {
        DeleteEntryError.Text = string.Empty;
        DeleteEntryError.Visibility = Visibility.Collapsed;
        DeleteEntryOverlay.Visibility = Visibility.Visible;
    }

    private void ConfirmDeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (activeProject is null)
        {
            return;
        }

        bool success;
        string? error;
        if (scenesBeingDeleted.Count > 0)
        {
            success = projectContentStore.TryDeleteScenes(activeProject, scenesBeingDeleted, out error);
        }
        else if (objectBeingDeleted is not null && objectDeletionScene is not null)
        {
            success = projectContentStore.TryDeleteObject(activeProject, objectDeletionScene, objectBeingDeleted, out error);
        }
        else
        {
            return;
        }

        if (!success)
        {
            DeleteEntryError.Text = error ?? "Не удалось удалить элемент.";
            DeleteEntryError.Visibility = Visibility.Visible;
            return;
        }

        DeleteEntryOverlay.Visibility = Visibility.Collapsed;
        scenesBeingDeleted = [];
        objectBeingDeleted = null;
        objectDeletionScene = null;
        EndSceneSelection();
        if (showingObjects && activeScene is not null && activeProject.Scenes.Any(scene => scene.Id == activeScene.Id))
        {
            activeScene = activeProject.Scenes.First(scene => scene.Id == activeScene.Id);
            BuildObjectList();
        }
        else
        {
            ShowSceneListPage();
        }
    }

    private void CancelDeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        DeleteEntryOverlay.Visibility = Visibility.Collapsed;
        scenesBeingDeleted = [];
        objectBeingDeleted = null;
        objectDeletionScene = null;
        if (workspaceSelectionMode != WorkspaceSelectionMode.None)
        {
            EndSceneSelection();
        }
    }

    private void DeleteEntryOverlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, DeleteEntryOverlay))
        {
            CancelDeleteEntry_Click(sender, e);
        }
    }
}
