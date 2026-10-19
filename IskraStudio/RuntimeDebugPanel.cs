using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IskraStudio;

internal sealed class RuntimeDebugPanel : UserControl
{
    private readonly StackPanel variableRows = new();
    private readonly StackPanel outputRows = new();
    private readonly TextBlock variableEmptyState;
    private readonly TextBlock outputEmptyState;

    public RuntimeDebugPanel()
    {
        variableEmptyState = CreateEmptyState("В проекте пока нет переменных.");
        outputEmptyState = CreateEmptyState("Здесь появится текст из команды «Печать».");
        variableRows.Children.Add(variableEmptyState);
        outputRows.Children.Add(outputEmptyState);
        Content = CreateDebugPanel();
    }

    public void BeginRun()
    {
        Visibility = Visibility.Visible;
        variableRows.Children.Clear();
        variableRows.Children.Add(variableEmptyState);
        outputRows.Children.Clear();
        outputRows.Children.Add(outputEmptyState);
    }

    public void ToggleVisibility() => Visibility = Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    public void UpdateVariables(IReadOnlyList<RuntimeDebugVariable> snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => UpdateVariables(snapshot));
            return;
        }

        variableRows.Children.Clear();
        if (snapshot.Count == 0)
        {
            variableRows.Children.Add(variableEmptyState);
            return;
        }

        foreach (var variable in snapshot.OrderBy(item => item.ObjectName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock
            {
                Text = $"{variable.SceneName} · {variable.ObjectName} · {variable.Name}",
                Foreground = DebugBrush("OnDarkMutedBrush", Color.FromRgb(184, 179, 170)),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"{variable.SceneName} · {variable.ObjectName} · {variable.Name}"
            };
            var value = new TextBlock
            {
                Text = variable.Value,
                Foreground = DebugBrush("OnDarkBrush", Color.FromRgb(250, 249, 245)),
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 12,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(value, 1);
            row.Children.Add(name);
            row.Children.Add(value);
            variableRows.Children.Add(row);
        }
    }

    public void AddOutput(string objectName, string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => AddOutput(objectName, text));
            return;
        }

        if (outputRows.Children.Count > 0 && ReferenceEquals(outputRows.Children[0], outputEmptyState))
        {
            outputRows.Children.Clear();
        }

        var entry = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
        entry.Children.Add(new TextBlock
        {
            Text = objectName,
            Foreground = DebugBrush("OnDarkMutedBrush", Color.FromRgb(184, 179, 170)),
            FontSize = 11
        });
        entry.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = DebugBrush("OnDarkBrush", Color.FromRgb(250, 249, 245)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });
        outputRows.Children.Add(entry);
        while (outputRows.Children.Count > 250)
        {
            outputRows.Children.RemoveAt(0);
        }
    }

    private Border CreateDebugPanel()
    {
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new DockPanel { LastChildFill = false };
        var closeButton = CreateDebugButton("×", 19);
        closeButton.ToolTip = "Скрыть отладку · F3";
        closeButton.Click += (_, _) => Visibility = Visibility.Collapsed;
        DockPanel.SetDock(closeButton, Dock.Right);
        header.Children.Add(closeButton);
        header.Children.Add(new TextBlock
        {
            Text = "ОТЛАДКА",
            Foreground = DebugBrush("OnDarkMutedBrush", Color.FromRgb(184, 179, 170)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(header);

        var tabs = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 8) };
        var clearButton = CreateDebugButton("Очистить", 11);
        clearButton.Click += (_, _) =>
        {
            outputRows.Children.Clear();
            outputRows.Children.Add(outputEmptyState);
        };
        DockPanel.SetDock(clearButton, Dock.Right);
        tabs.Children.Add(clearButton);
        var variablesTab = CreateDebugButton("Переменные", 12);
        var outputTab = CreateDebugButton("Вывод", 12);
        tabs.Children.Add(variablesTab);
        tabs.Children.Add(outputTab);
        Grid.SetRow(tabs, 1);
        content.Children.Add(tabs);

        var views = new Grid();
        var variableView = CreateDebugScroller(variableRows);
        var outputView = CreateDebugScroller(outputRows);
        views.Children.Add(variableView);
        views.Children.Add(outputView);
        outputView.Visibility = Visibility.Collapsed;
        Grid.SetRow(views, 2);
        content.Children.Add(views);

        void SelectTab(bool showVariables)
        {
            variableView.Visibility = showVariables ? Visibility.Visible : Visibility.Collapsed;
            outputView.Visibility = showVariables ? Visibility.Collapsed : Visibility.Visible;
            clearButton.Visibility = showVariables ? Visibility.Collapsed : Visibility.Visible;
            variablesTab.Foreground = DebugBrush(showVariables ? "OnDarkBrush" : "OnDarkMutedBrush", Color.FromRgb(250, 249, 245));
            outputTab.Foreground = DebugBrush(showVariables ? "OnDarkMutedBrush" : "OnDarkBrush", Color.FromRgb(184, 179, 170));
            variablesTab.Background = showVariables
                ? DebugBrush("AccentBrush", Color.FromRgb(169, 88, 62))
                : Brushes.Transparent;
            outputTab.Background = showVariables
                ? Brushes.Transparent
                : DebugBrush("AccentBrush", Color.FromRgb(169, 88, 62));
        }

        variablesTab.Click += (_, _) => SelectTab(true);
        outputTab.Click += (_, _) => SelectTab(false);
        SelectTab(true);

        return new Border
        {
            Width = 360,
            Height = 260,
            MaxHeight = 360,
            Margin = new Thickness(16),
            Padding = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(244, 27, 25, 23)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 74, 68, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = content
        };
    }

    private static ScrollViewer CreateDebugScroller(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };

    private static TextBlock CreateEmptyState(string text) => new()
    {
        Text = text,
        Foreground = DebugBrush("OnDarkMutedBrush", Color.FromRgb(184, 179, 170)),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 8, 2, 8)
    };

    private static Button CreateDebugButton(string label, double fontSize) => new()
    {
        Content = label,
        FontSize = fontSize,
        Foreground = DebugBrush("OnDarkBrush", Color.FromRgb(250, 249, 245)),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(8, 5, 8, 5),
        Cursor = Cursors.Hand,
        Focusable = false
    };

    private static Brush DebugBrush(string resourceKey, Color fallback) =>
        Application.Current.TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallback);
}

