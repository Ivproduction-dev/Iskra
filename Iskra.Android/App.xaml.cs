using Microsoft.Extensions.DependencyInjection;

namespace Iskra.Android;

public partial class App : Application
{
    private readonly IServiceProvider services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        this.services = services;
        UserAppTheme = Preferences.Get("theme", "auto") switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            MobileLog.Write("Необработанная ошибка приложения.", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            MobileLog.Write("Необработанная ошибка задачи.", e.Exception);
            e.SetObserved();
        };
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var root = services.GetRequiredService<ProjectLibraryPage>();
        var navigation = new NavigationPage(root)
        {
            BarBackgroundColor = MobileTheme.SurfacePrimary,
            BarTextColor = MobileTheme.TextPrimary
        };
        NavigationPage.SetHasNavigationBar(root, false);
        return new Window(navigation);
    }
}
