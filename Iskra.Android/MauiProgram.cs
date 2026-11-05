using IskraStudio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using Microsoft.Maui.Handlers;

namespace Iskra.Android;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.UseSkiaSharp();
        EditorHandler.Mapper.AppendToMapping("IskraScriptSyntax", (handler, view) =>
        {
            if (view is ScriptSyntaxEditor && handler.PlatformView is global::Android.Widget.EditText nativeEditor)
            {
                ScriptSyntaxHighlighting.Attach(nativeEditor);
            }
        });
        builder.Services.AddSingleton(_ => new ProjectStore(
            Path.Combine(FileSystem.AppDataDirectory, "IskraStudio"),
            directory => Directory.Delete(directory, recursive: true)));
        builder.Services.AddSingleton<ProjectContentStore>();
        builder.Services.AddSingleton<ProjectArchiveService>();
        builder.Services.AddTransient<ProjectLibraryPage>();

        CoreLog.Sink = MobileLog.Write;
#if DEBUG
        builder.Logging.AddDebug();
#endif
        var app = builder.Build();
        app.Services.GetRequiredService<ProjectStore>().SweepLeftoverOperations();
        return app;
    }
}
