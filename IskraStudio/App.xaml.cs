using System.Windows;

namespace IskraStudio;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            AppLog.Write("Необработанная ошибка приложения.", e.Exception);
            MessageBox.Show("Что-то пошло не так. Лог с ошибкой сохранён в log.txt.", "Искра Студио", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
    }
}
