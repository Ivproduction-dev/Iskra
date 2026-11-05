using System.Diagnostics;

namespace IskraStudio;

public static class CoreLog
{
    public static Action<string, Exception?>? Sink { get; set; }

    public static void Write(string message, Exception? exception = null)
    {
        var sink = Sink;
        if (sink is null)
        {
            Debug.WriteLine(exception is null ? message : $"{message}{Environment.NewLine}{exception}");
            return;
        }

        sink(message, exception);
    }
}
