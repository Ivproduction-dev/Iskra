using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace IskraStudio;

public partial class MainWindow
{
    private void ExportProject_Click(object sender, RoutedEventArgs e)
    {
        var project = projectBeingEdited;
        if (project is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".iskp",
            FileName = project.Name + ".iskp",
            Filter = "Архив проекта Искры (*.iskp)|*.iskp",
            OverwritePrompt = true,
            Title = "Экспортировать проект"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            archiveService.Export(project, dialog.FileName);
            MessageBox.Show(this, "Проект экспортирован.", "Искра Студио", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.SecurityException)
        {
            AppLog.Write($"Не удалось экспортировать проект «{project.Name}».", exception);
            MessageBox.Show(this, "Не удалось экспортировать проект. Подробности записаны в log.txt.", "Экспорт проекта", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportProject_Click(object sender, RoutedEventArgs e)
    {
        AppMenuPopup.IsOpen = false;
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            DefaultExt = ".iskp",
            Filter = "Архив проекта Искры (*.iskp)|*.iskp",
            Title = "Импортировать проект"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var archiveName = archiveService.ReadProjectName(dialog.FileName).Trim();
            if (!ProjectStore.IsValidProjectName(archiveName, out _))
            {
                archiveName = Path.GetFileNameWithoutExtension(dialog.FileName).Trim();
            }
            if (!ProjectStore.IsValidProjectName(archiveName, out var nameError))
            {
                throw new InvalidDataException(nameError ?? "В архиве указано недопустимое имя проекта.");
            }

            var name = archiveName;
            if (projects.Any(project => string.Equals(project.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = FindImportedCopyName(archiveName);
                var answer = MessageBox.Show(
                    this,
                    $"Проект «{archiveName}» уже есть в списке. Импортировать этот архив как «{name}»?",
                    "Проект уже существует",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var imported = archiveService.Import(dialog.FileName, name);
            projects.Add(imported);
            RefreshProjectViews();
            ProjectListScrollViewer.ScrollToTop();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Security.SecurityException or System.Text.Json.JsonException or OverflowException)
        {
            AppLog.Write($"Не удалось импортировать архив «{Path.GetFileName(dialog.FileName)}».", exception);
            MessageBox.Show(
                this,
                "Что-то пошло не так. Лог с ошибкой сохранён в log.txt",
                "Импорт проекта",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string FindImportedCopyName(string baseName)
    {
        var suffix = " (импорт)";
        var candidate = baseName + suffix;
        var number = 2;
        while (projects.Any(project => string.Equals(project.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName}{suffix} {number++}";
        }
        return candidate;
    }
}
