using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows.Shell;

namespace IskraStudio;

public partial class MainWindow : Window
{
    private enum ProjectDialogMode
    {
        Create,
        Rename,
        Options
    }

    private readonly ProjectStore projectStore = new();
    private readonly ProjectContentStore projectContentStore;
    private readonly ProjectArchiveService archiveService;
    private readonly AppSettingsStore appSettingsStore = new();
    private readonly ObservableCollection<IskraProject> projects = [];
    private IskraAppSettings appSettings = new();
    private UIElement? previousView;
    private IskraProject? projectForActions;
    private IskraProject? projectBeingEdited;
    private IskraProject? projectBeingDeleted;
    private ProjectDialogMode projectDialogMode;
    private int selectedTheme;

    public MainWindow()
    {
        InitializeComponent();
        projectContentStore = new ProjectContentStore(projectStore);
        archiveService = new ProjectArchiveService(projectStore);
        appSettings = appSettingsStore.Load();
        selectedTheme = Math.Clamp(appSettings.Theme, 0, 2);
        ThemeSelector.SelectedIndex = selectedTheme;
        ApplyTheme(selectedTheme switch
        {
            1 => true,
            2 => false,
            _ => DetectSystemLightTheme()
        });
        ApplyWindowMode(appSettings.UseCustomWindow);
        var loadedProjects = projectStore.LoadAll(out var skippedProjects);
        foreach (var project in loadedProjects)
        {
            projects.Add(project);
        }
        ProjectItems.ItemsSource = projects;
        RefreshProjectViews();

        if (skippedProjects.Count > 0)
        {
            var names = string.Join("\n", skippedProjects.Select(name => $"• {name}"));
            MessageBox.Show(
                this,
                $"Не удалось прочитать данные этих проектов. Они не удалены, но пока скрыты из списка:\n\n{names}",
                "Некоторые проекты недоступны",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void AppMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            ImportProjectMenuItem.Visibility = ProjectListView.Visibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
            AppMenuPopup.PlacementTarget = button;
            AppMenuPopup.HorizontalOffset = -(190 - button.ActualWidth);
            AppMenuPopup.IsOpen = !AppMenuPopup.IsOpen;
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        AppMenuPopup.IsOpen = false;
        WorkspaceToolsPopup.IsOpen = false;
        previousView = WorkspaceView.Visibility == Visibility.Visible
            ? WorkspaceView
            : ProjectListView.Visibility == Visibility.Visible
                ? ProjectListView
                : LibraryView;
        LibraryView.Visibility = Visibility.Collapsed;
        ProjectListView.Visibility = Visibility.Collapsed;
        WorkspaceView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        ThemeSelector.SelectedIndex = selectedTheme;
        WindowModeSelector.SelectedIndex = appSettings.UseCustomWindow ? 1 : 0;
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsView.Visibility = Visibility.Collapsed;
        (previousView ?? LibraryView).Visibility = Visibility.Visible;
    }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ThemeSelector.SelectedIndex < 0)
        {
            return;
        }

        selectedTheme = ThemeSelector.SelectedIndex;
        appSettings.Theme = selectedTheme;
        SaveAppSettings();
        ApplyTheme(selectedTheme switch
        {
            1 => true,
            2 => false,
            _ => DetectSystemLightTheme()
        });
    }

    private void WindowModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || WindowModeSelector.SelectedIndex < 0)
        {
            return;
        }

        appSettings.UseCustomWindow = WindowModeSelector.SelectedIndex == 1;
        ApplyWindowMode(appSettings.UseCustomWindow);
        SaveAppSettings();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        UpdateCustomWindowButtons();
    }

    private void ShowCreateProject_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceView.Visibility == Visibility.Visible && activeProject is not null)
        {
            if (showingObjects)
            {
                ShowCreateObjectDialog(activeScene!);
            }
            else
            {
                ShowCreateSceneDialog();
            }
            return;
        }

        ShowProjectDialog(ProjectDialogMode.Create, null);
    }

    private void ShowProjectDialog(ProjectDialogMode mode, IskraProject? project)
    {
        projectDialogMode = mode;
        projectBeingEdited = project;
        ProjectNameError.Text = string.Empty;
        ProjectNameError.Visibility = Visibility.Collapsed;
        ProjectExportSection.Visibility = mode == ProjectDialogMode.Options ? Visibility.Visible : Visibility.Collapsed;
        ProjectOrientationSection.Visibility = mode == ProjectDialogMode.Create ? Visibility.Visible : Visibility.Collapsed;

        switch (mode)
        {
            case ProjectDialogMode.Create:
                CreateProjectTitle.Text = "Новый проект";
                ProjectDialogSubtitle.Text = "Задай имя и формат будущей сцены";
                ProjectDialogSaveButton.Content = "Создать";
                ProjectNameBox.Clear();
                HorizontalOrientationOption.IsChecked = true;
                break;
            case ProjectDialogMode.Rename:
                CreateProjectTitle.Text = "Переименовать проект";
                ProjectDialogSubtitle.Text = "Укажи новое имя проекта.";
                ProjectDialogSaveButton.Content = "Сохранить";
                ProjectNameBox.Text = project?.Name ?? string.Empty;
                break;
            case ProjectDialogMode.Options:
                CreateProjectTitle.Text = "Опции проекта";
                ProjectDialogSubtitle.Text = "Имя и перенос проекта. Ориентация задаётся при создании.";
                ProjectDialogSaveButton.Content = "Сохранить";
                ProjectNameBox.Text = project?.Name ?? string.Empty;
                break;
        }

        CreateProjectOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => ProjectNameBox.Focus()));
    }

    private void OpenProjectList_Click(object sender, RoutedEventArgs e)
    {
        LibraryView.Visibility = Visibility.Collapsed;
        WorkspaceView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        ProjectListView.Visibility = Visibility.Visible;
        ProjectListScrollViewer.ScrollToTop();
    }

    private void BackToLibrary_Click(object sender, RoutedEventArgs e)
    {
        ProjectListView.Visibility = Visibility.Collapsed;
        LibraryView.Visibility = Visibility.Visible;
    }

    private void CloseCreateProject_Click(object sender, RoutedEventArgs e)
    {
        CreateProjectOverlay.Visibility = Visibility.Collapsed;
        projectBeingEdited = null;
    }

    private void CreateProjectOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, CreateProjectOverlay))
        {
            CreateProjectOverlay.Visibility = Visibility.Collapsed;
            projectBeingEdited = null;
        }
    }

    private void ProjectDialogSave_Click(object sender, RoutedEventArgs e)
    {
        var orientation = projectDialogMode is ProjectDialogMode.Rename or ProjectDialogMode.Options && projectBeingEdited is { } editedProject
            ? editedProject.Orientation
            : VerticalOrientationOption.IsChecked == true
                ? ProjectOrientation.Vertical
                : ProjectOrientation.Horizontal;

        if (projectDialogMode == ProjectDialogMode.Create)
        {
            if (!projectStore.TryCreate(ProjectNameBox.Text, orientation, projects, out var createdProject, out var createError))
            {
                ShowProjectNameError(createError);
                return;
            }

            projects.Add(createdProject!);
            RefreshProjectViews();
            CreateProjectOverlay.Visibility = Visibility.Collapsed;
            projectBeingEdited = null;
            LibraryView.Visibility = Visibility.Collapsed;
            WorkspaceView.Visibility = Visibility.Collapsed;
            SettingsView.Visibility = Visibility.Collapsed;
            ProjectListView.Visibility = Visibility.Visible;
            ProjectListScrollViewer.ScrollToTop();
            return;
        }

        if (projectBeingEdited is not { } currentProject)
        {
            ShowProjectNameError("Не удалось найти проект для сохранения.");
            return;
        }

        if (!projectStore.TryUpdate(currentProject, ProjectNameBox.Text, orientation, projects, out var updatedProject, out var updateError))
        {
            ShowProjectNameError(updateError);
            return;
        }

        var projectIndex = projects.IndexOf(currentProject);
        if (projectIndex >= 0)
        {
            projects[projectIndex] = updatedProject!;
            if (ReferenceEquals(activeProject, currentProject))
            {
                activeProject = updatedProject;
                if (activeScene is not null)
                {
                    activeScene = updatedProject!.Scenes.FirstOrDefault(scene => scene.Id == activeScene.Id);
                }
            }
        }

        RefreshProjectViews();
        CreateProjectOverlay.Visibility = Visibility.Collapsed;
        projectBeingEdited = null;
        if (WorkspaceView.Visibility == Visibility.Visible && projectIndex >= 0)
        {
            WorkspaceTitle.Text = showingObjects ? activeScene?.Name ?? "Объекты" : "Список сцен";
        }
    }

    private void ShowProjectNameError(string? error)
    {
        ProjectNameError.Text = error ?? "Не удалось сохранить изменения.";
        ProjectNameError.Visibility = Visibility.Visible;
        ProjectNameBox.Focus();
    }

    private void ProjectActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IskraProject project } button)
        {
            return;
        }

        projectForActions = project;
        ProjectActionsPopup.PlacementTarget = button;
        ProjectActionsPopup.HorizontalOffset = -(216 - button.ActualWidth);
        ProjectActionsPopup.IsOpen = true;
        e.Handled = true;
    }

    private void RenameProject_Click(object sender, RoutedEventArgs e)
    {
        ProjectActionsPopup.IsOpen = false;
        ShowProjectDialog(ProjectDialogMode.Rename, projectForActions);
    }

    private void OpenProjectOptions_Click(object sender, RoutedEventArgs e)
    {
        ProjectActionsPopup.IsOpen = false;
        ShowProjectDialog(ProjectDialogMode.Options, projectForActions);
    }

    private void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        ProjectActionsPopup.IsOpen = false;
        projectBeingDeleted = projectForActions;
        if (projectBeingDeleted is null)
        {
            return;
        }

        DeleteProjectName.Text = $"«{projectBeingDeleted.Name}»";
        DeleteProjectError.Text = string.Empty;
        DeleteProjectError.Visibility = Visibility.Collapsed;
        DeleteProjectOverlay.Visibility = Visibility.Visible;
    }

    private void CloseDeleteProject_Click(object sender, RoutedEventArgs e)
    {
        DeleteProjectOverlay.Visibility = Visibility.Collapsed;
        projectBeingDeleted = null;
    }

    private void DeleteProjectOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, DeleteProjectOverlay))
        {
            DeleteProjectOverlay.Visibility = Visibility.Collapsed;
            projectBeingDeleted = null;
        }
    }

    private void ConfirmDeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (projectBeingDeleted is not { } project)
        {
            return;
        }

        if (!projectStore.TryDelete(project, out var error))
        {
            DeleteProjectError.Text = error ?? "Не удалось переместить проект в корзину.";
            DeleteProjectError.Visibility = Visibility.Visible;
            return;
        }

        projects.Remove(project);
        RefreshProjectViews();
        DeleteProjectOverlay.Visibility = Visibility.Collapsed;
        projectBeingDeleted = null;
    }

    private void RefreshProjectViews()
    {
        var hasProjects = projects.Count > 0;
        ProjectEmptyState.Visibility = hasProjects ? Visibility.Collapsed : Visibility.Visible;
        ProjectItems.Visibility = hasProjects ? Visibility.Visible : Visibility.Collapsed;

        var recentProject = projects
            .Where(project => project.LastOpenedAt.HasValue)
            .OrderByDescending(project => project.LastOpenedAt)
            .FirstOrDefault();

        RecentEmptyState.Visibility = recentProject is null ? Visibility.Visible : Visibility.Collapsed;
        RecentProjectButton.Visibility = recentProject is null ? Visibility.Collapsed : Visibility.Visible;
        RecentProjectButton.Tag = recentProject;
        RecentProjectName.Text = recentProject?.Name ?? string.Empty;
    }

    private void ProjectRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: IskraProject project })
        {
            OpenProject(project);
        }
    }

    private void RecentProject_Click(object sender, RoutedEventArgs e)
    {
        if (RecentProjectButton.Tag is IskraProject project)
        {
            OpenProject(project);
        }
    }

    private void OpenProject(IskraProject project)
    {
        try
        {
            projectStore.MarkOpened(project);
        }
        catch (IOException)
        {
            MessageBox.Show(this, "Не удалось сохранить состояние проекта.", "Искра Студио", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Нет доступа к папке проекта.", "Искра Студио", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        activeProject = project;
        RefreshProjectViews();
        LibraryView.Visibility = Visibility.Collapsed;
        ProjectListView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        WorkspaceView.Visibility = Visibility.Visible;
        ShowSceneListPage();
    }

    private void BackToProjects_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceView.Visibility == Visibility.Visible && showingObjects)
        {
            ShowSceneListPage();
            return;
        }

        WorkspaceView.Visibility = Visibility.Collapsed;
        ProjectListView.Visibility = Visibility.Visible;
        SettingsView.Visibility = Visibility.Collapsed;
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        AppMenuPopup.IsOpen = false;
        WorkspaceToolsPopup.IsOpen = false;
        MessageBox.Show(this, "Создавай проекты и собирай сцены в мастерской Искры. Дополнительные подсказки появятся по мере развития редактора.", "Справка Искры", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        AppMenuPopup.IsOpen = false;
        WorkspaceToolsPopup.IsOpen = false;
        MessageBox.Show(this, "Искра Студио\nСреда для создания интерактивных проектов.", "О программе", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        AppMenuPopup.IsOpen = false;
        try
        {
            AppLog.Open();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"Не удалось открыть журнал.\n{AppLog.FilePath}", "Журнал Искры", MessageBoxButton.OK, MessageBoxImage.Warning);
            AppLog.Write("Не удалось открыть log.txt.", exception);
        }
    }

    private void SaveAppSettings()
    {
        try
        {
            appSettingsStore.Save(appSettings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Не удалось сохранить настройки приложения.", exception);
        }
    }

    private void ApplyWindowMode(bool useCustomWindow)
    {
        WindowStyle = useCustomWindow ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        WindowChrome.SetWindowChrome(this, useCustomWindow
            ? new WindowChrome
            {
                CaptionHeight = 52,
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false
            }
            : null);
        WindowControlsOverlay.Visibility = useCustomWindow ? Visibility.Visible : Visibility.Collapsed;
        var chromeMargin = useCustomWindow ? new Thickness(0, 0, 140, 0) : new Thickness(0);
        LibraryMenuButton.Margin = chromeMargin;
        ProjectListMenuButton.Margin = chromeMargin;
        WorkspaceMenuButton.Margin = chromeMargin;
        SettingsBackButton.Margin = chromeMargin;
        UpdateCustomWindowButtons();
    }

    private void UpdateCustomWindowButtons()
    {
        if (IsLoaded)
        {
            ToggleWindowStateButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
            ToggleWindowStateButton.ToolTip = WindowState == WindowState.Maximized ? "Восстановить" : "Развернуть";
        }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void ToggleWindowState_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private static bool DetectSystemLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    private static void ApplyTheme(bool isLight)
    {
        var palette = isLight
            ? new Dictionary<string, string>
            {
                ["CanvasBrush"] = "#FAF9F5", ["PanelBrush"] = "#FFFFFF", ["SoftBrush"] = "#F5F0E8",
                ["SelectedBrush"] = "#EFE9DE", ["CodeBrush"] = "#181715", ["InkBrush"] = "#141413",
                ["BodyBrush"] = "#3D3D3A", ["MutedBrush"] = "#6C6A64", ["HairlineBrush"] = "#E6DFD8",
                ["HairlineSoftBrush"] = "#EBE6DF", ["AccentBrush"] = "#A9583E", ["AccentHoverBrush"] = "#8F4430",
                ["AccentSoftBrush"] = "#F3E3DC", ["OnAccentBrush"] = "#FFFFFF", ["SuccessBrush"] = "#2F7947",
                ["ErrorBrush"] = "#AE3A3A", ["OnDarkBrush"] = "#FAF9F5", ["OnDarkMutedBrush"] = "#A09D96"
            }
            : new Dictionary<string, string>
            {
                ["CanvasBrush"] = "#171614", ["PanelBrush"] = "#211F1C", ["SoftBrush"] = "#292723",
                ["SelectedBrush"] = "#39352F", ["CodeBrush"] = "#11100F", ["InkBrush"] = "#F7F4EE",
                ["BodyBrush"] = "#DFDBD3", ["MutedBrush"] = "#AAA59C", ["HairlineBrush"] = "#403C36",
                ["HairlineSoftBrush"] = "#35322D", ["AccentBrush"] = "#A9583E", ["AccentHoverBrush"] = "#8F4430",
                ["AccentSoftBrush"] = "#3D2A23", ["OnAccentBrush"] = "#FFFFFF", ["SuccessBrush"] = "#79C28D",
                ["ErrorBrush"] = "#F08080", ["OnDarkBrush"] = "#F7F4EE", ["OnDarkMutedBrush"] = "#AAA59C"
            };

        foreach (var (key, color) in palette)
        {
            Application.Current.Resources[key] = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(color));
        }
    }
}
