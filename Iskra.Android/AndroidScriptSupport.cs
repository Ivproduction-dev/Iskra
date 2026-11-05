using IskraStudio;

namespace Iskra.Android;

internal static class AndroidScriptSupport
{
    private static readonly HashSet<string> SupportedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "set-image", "step-image", "place", "move-by", "move-to", "set-transparency", "change-transparency",
        "set-size", "change-size", "set-brightness", "change-brightness", "set-direction", "change-direction",
        "set-position", "change-position", "set-layer", "change-layer", "set-visibility", "toggle-visibility",
        "show", "hide", "flip", "set-orientation", "wait", "print", "play-sound", "play-sound-wait",
        "loop-sound", "stop-sound", "set-volume", "set-sound-volume", "pause-sound", "resume-sound",
        "play-image", "stop-image", "set-value", "change-value", "branch-false", "jump", "loop-jump", "event-end",
        "stop-movement", "set-scene-gravity", "set-motion-type", "launch-scene", "resume-scene",
        "change-sound-volume", "set-sound-repeat", "ask"
    };

    private static readonly HashSet<string> SupportedEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "start", "press", "release", "hover", "when", "every", "frame", "sound-ended"
    };

    public static IReadOnlyList<string> FindUnsupported(CompiledProject project)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scene in project.Scenes)
        {
            foreach (var item in scene.Objects)
            {
                foreach (var command in item.Commands)
                {
                    if (command.Kind == "event")
                    {
                        if (!SupportedEvents.Contains(command.Argument))
                        {
                            Add(result, seen, EventLabel(command.Argument));
                        }
                        continue;
                    }
                    if (!SupportedCommands.Contains(command.Kind))
                    {
                        Add(result, seen, CommandLabel(command.Kind));
                    }
                }
            }
        }
        return result;
    }

    private static void Add(List<string> result, HashSet<string> seen, string label)
    {
        if (seen.Add(label))
        {
            result.Add(label);
        }
    }

    private static string CommandLabel(string kind) => kind switch
    {
        "move-by" => "Подвинуть",
        "move-to" => "Двигать.до",
        "stop-movement" => "Остановить движение",
        "set-position" => "Задать Свойство.х/у",
        "change-position" => "Изменить Свойство.х/у",
        "set-direction" => "Задать направление",
        "change-direction" => "Изменить направление",
        "set-layer" => "Задать слой",
        "change-layer" => "Изменить слой",
        "set-visibility" => "Задать видимость",
        "toggle-visibility" => "Переключить видимость",
        "flip" => "Отразить",
        "set-scene-gravity" => "Задать гравитацию",
        "set-motion-type" => "Задать тип движения",
        "launch-scene" => "Запустить.сцену",
        "resume-scene" => "Продолжить.сцену",
        "set-orientation" => "Сменить.ориентацию",
        "loop-sound" => "Зациклить звук",
        "stop-sound" => "Остановить звук",
        "set-volume" => "Задать громкость",
        "set-sound-volume" => "Задать громкость звука",
        "change-sound-volume" => "Изменить громкость звука",
        "pause-sound" => "Приостановить звук",
        "resume-sound" => "Продолжить звук",
        "set-sound-repeat" => "Задать повтор звука",
        "play-image" => "Проиграть изображение",
        "stop-image" => "Остановить изображение",
        "ask" => "Спросить",
        _ => kind
    };

    private static string EventLabel(string kind) => kind switch
    {
        "key" => "При. нажатии клавиши",
        "every" => "Каждые.N.секунды",
        "frame" => "Каждый кадр",
        "sound-ended" => "При. окончании звука",
        _ => kind
    };
}
