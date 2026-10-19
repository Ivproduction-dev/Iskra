using System.Globalization;
using System.Text.RegularExpressions;

namespace IskraStudio;

internal enum PositionAxis
{
    None,
    X,
    Y
}

internal static class ScriptCommandSyntax
{
    private static readonly Regex VisibilityTargetPattern = new(
        "^(?<verb>спрятать|скрыть|показать)\\s*\\.\\s*(?<name>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex VisibilitySelfPattern = new(
        "^(?<verb>спрятать|скрыть|показать)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex VisibilityWithoutDotPattern = new(
        "^(?<verb>спрятать|скрыть|показать)\\s+\\S",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MoveByPattern = new(
        "^Подвинуть\\s*\\.\\s*(?<dx>-?\\d+)\\s+(?<dy>-?\\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MoveByFamilyPattern = new(
        "^подвинуть(?:\\s|\\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LaunchScenePattern = new(
        "^Запустить\\s*\\.\\s*сцену\\s*\\.\\s*(?<name>.+?)\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ResumeScenePattern = new(
        "^Продолжить\\s*\\.\\s*сцену\\s*\\.\\s*(?<name>.+?)\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LaunchFamilyPattern = new(
        "^запустить(?:\\s|\\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ResumeFamilyPattern = new(
        "^продолжить(?:\\s|\\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OrientationPattern = new(
        "^Сменить\\s*\\.\\s*ориентацию(?:\\s*\\.\\s*(?<mode>горизонтальная|вертикальная))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OrientationFamilyPattern = new(
        "^сменить(?:\\s|\\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LoopSoundPattern = new(
        "^Зациклить\\s+звук\\s*\\.\\s*(?<file>.+?)\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LoopSoundFamilyPattern = new(
        "^зациклить(?:\\s|\\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex StopSoundPattern = new(
        "^Остановить\\s+звук\\s*\\.\\s*(?<file>.+?)\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex StopSoundFamilyPattern = new(
        "^остановить\\s+звук(?:\\s|\\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex VolumePattern = new(
        "^Задать\\s+громкость\\s*\\.\\s*(?<rest>.+?)\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex VolumeFamilyPattern = new(
        "^задать\\s+громкость(?:\\s|\\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PlayImagePattern = new(
        "^Проиграть\\s+изображение\\s*\\.\\s*(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PlayImageFamilyPattern = new(
        "^проиграть(?:\\s|\\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex StopImagePattern = new(
        "^Остановить\\s+изображение\\s*\\.?\\s*(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex StopImageFamilyPattern = new(
        "^остановить\\s+изображение(?:\\s|\\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AskPattern = new(
        "^Спросить\\s*\\.\\s*(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AskFamilyPattern = new(
        "^спросить\\s*\\.",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex KeyboardEventPattern = new(
        "^при\\s*\\.\\s*нажатии\\s+клавиш(?:и|а)?\\s*\\.\\s*(?<key>\\S+)\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex KeyboardEventFamilyPattern = new(
        "^при\\s*\\.\\s*нажатии\\s+клавиш",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EveryEventPattern = new(
        "^каждые\\s*\\.\\s*(?<amount>\\d+)\\s*\\.\\s*(?<unit>секунд(?:а|ы|у|е|ой|ами|ах)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EveryEventFamilyPattern = new(
        "^каждые(?:\\s|\\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FrameEventPattern = new(
        "^каждый\\s*\\.?\\s*кадр$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FrameEventFamilyPattern = new(
        "^каждый(?:\\s|\\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public const string PropertyWordError =
        "Имя «Свойство» зарезервировано: так называются свойства объекта — например, «Свойство.х».";

    public static bool TryParseVisibility(string line, out bool visible, out string? targetName, out string? error)
    {
        visible = false;
        targetName = null;
        error = null;
        var target = VisibilityTargetPattern.Match(line);
        if (target.Success)
        {
            var verb = target.Groups["verb"].Value;
            visible = IsVisibleVerb(verb);
            targetName = target.Groups["name"].Value.Trim();
            if (targetName.Length == 0)
            {
                targetName = null;
                error = $"После «{verb}.» нужно имя объекта — например: «{verb}.монета».";
            }
            return true;
        }
        var self = VisibilitySelfPattern.Match(line);
        if (self.Success)
        {
            visible = IsVisibleVerb(self.Groups["verb"].Value);
            return true;
        }
        var withoutDot = VisibilityWithoutDotPattern.Match(line);
        if (withoutDot.Success)
        {
            var verb = withoutDot.Groups["verb"].Value;
            error = $"Имя чужого объекта пишется с точкой: «{verb}.монета». Свой объект — просто «{verb}».";
            return true;
        }
        return false;
    }

    public static bool TryParseMoveBy(string line, out string offset, out string? error)
    {
        offset = string.Empty;
        error = null;
        var match = MoveByPattern.Match(line);
        if (match.Success)
        {
            offset = $"{match.Groups["dx"].Value} {match.Groups["dy"].Value}";
            return true;
        }
        if (MoveByFamilyPattern.IsMatch(line))
        {
            error = "Формат команды: «Подвинуть. 10 -5» — сдвиг по X и по Y двумя целыми числами.";
            return true;
        }
        return false;
    }

    public static bool TryParseSceneCommand(string line, out bool resume, out string sceneName, out string? error)
    {
        resume = false;
        sceneName = string.Empty;
        error = null;
        var launch = LaunchScenePattern.Match(line);
        if (launch.Success)
        {
            sceneName = launch.Groups["name"].Value.Trim();
            return true;
        }
        var resumeMatch = ResumeScenePattern.Match(line);
        if (resumeMatch.Success)
        {
            resume = true;
            sceneName = resumeMatch.Groups["name"].Value.Trim();
            return true;
        }
        if (LaunchFamilyPattern.IsMatch(line))
        {
            error = "Формат команды: «Запустить.сцену.имя сцены» — например, «Запустить.сцену.главное меню».";
            return true;
        }
        if (ResumeFamilyPattern.IsMatch(line))
        {
            error = "Формат команды: «Продолжить.сцену.имя сцены» — например, «Продолжить.сцену.главное меню».";
            return true;
        }
        return false;
    }

    public static bool TryParseOrientationCommand(string line, out string mode, out string? error)
    {
        mode = string.Empty;
        error = null;
        var match = OrientationPattern.Match(line);
        if (match.Success)
        {
            mode = match.Groups["mode"].Success
                ? match.Groups["mode"].Value.ToLowerInvariant() == "горизонтальная" ? "horizontal" : "vertical"
                : "toggle";
            return true;
        }
        if (OrientationFamilyPattern.IsMatch(line))
        {
            error = "Формат команды: «Сменить.ориентацию», «Сменить.ориентацию.горизонтальная» или «Сменить.ориентацию.вертикальная».";
            return true;
        }
        return false;
    }

    public static bool TryParseLoopSound(string line, out string file, out string? error)
    {
        file = string.Empty;
        error = null;
        var match = LoopSoundPattern.Match(line);
        if (match.Success)
        {
            file = match.Groups["file"].Value.Trim();
            return true;
        }
        if (LoopSoundFamilyPattern.IsMatch(line))
        {
            error = "Формат команды: «Зациклить звук.тема.mp3».";
            return true;
        }
        return false;
    }

    public static bool TryParseStopSound(string line, out string file, out string? error)
    {
        file = string.Empty;
        error = null;
        var match = StopSoundPattern.Match(line);
        if (match.Success)
        {
            file = match.Groups["file"].Value.Trim();
            return true;
        }
        if (StopSoundFamilyPattern.IsMatch(line))
        {
            error = "Формат команды: «Остановить звук.тема.mp3».";
            return true;
        }
        return false;
    }

    public static bool TryParseVolume(string line, out string? file, out string amount, out string? error)
    {
        file = null;
        amount = string.Empty;
        error = null;
        var match = VolumePattern.Match(line);
        if (!match.Success)
        {
            if (VolumeFamilyPattern.IsMatch(line))
            {
                error = "Формат команды: «Задать громкость. 50» (вся игра) или «Задать громкость.тема.mp3 50» (один звук).";
                return true;
            }
            return false;
        }

        var rest = match.Groups["rest"].Value.Trim();
        if (IsNumber(rest))
        {
            amount = rest;
            return true;
        }
        var separator = rest.LastIndexOf(' ');
        if (separator <= 0)
        {
            error = "Формат команды: «Задать громкость. 50» (вся игра) или «Задать громкость.тема.mp3 50» (один звук).";
            return true;
        }
        var lastToken = rest[(separator + 1)..].Trim();
        var fileName = rest[..separator].Trim();
        if (!IsNumber(lastToken) || fileName.Length == 0)
        {
            error = "Формат команды: «Задать громкость. 50» (вся игра) или «Задать громкость.тема.mp3 50» (один звук).";
            return true;
        }
        file = fileName;
        amount = lastToken;
        return true;
    }

    public static bool TryParsePlayImage(string line, out int speed, out string? objectName, out string? error)
    {
        speed = 0;
        objectName = null;
        error = null;
        var match = PlayImagePattern.Match(line);
        if (!match.Success)
        {
            if (PlayImageFamilyPattern.IsMatch(line))
            {
                error = "Формат команды: «Проиграть изображение.8» (свой объект) или «Проиграть изображение.монстр 8» (чужой объект).";
                return true;
            }
            return false;
        }

        var rest = match.Groups["rest"].Value.Trim();
        if (rest.Length == 0)
        {
            error = "Формат команды: «Проиграть изображение.8» (свой объект) или «Проиграть изображение.монстр 8» (чужой объект).";
            return true;
        }

        if (!int.TryParse(rest, out speed))
        {
            var separator = rest.LastIndexOf(' ');
            if (separator <= 0 || !int.TryParse(rest[(separator + 1)..].Trim(), out speed))
            {
                error = "Скорость листания — целое число кадров в секунду: например, «Проиграть изображение.8».";
                return true;
            }
            objectName = rest[..separator].Trim();
            if (objectName.Length == 0)
            {
                objectName = null;
                error = "Формат команды: «Проиграть изображение.8» (свой объект) или «Проиграть изображение.монстр 8» (чужой объект).";
                return true;
            }
        }

        if (speed < 1)
        {
            error = "Скорость листания — целое число от 1 кадра в секунду: например, «Проиграть изображение.8».";
            return true;
        }
        return true;
    }

    public static bool TryParseStopImage(string line, out string? error)
    {
        error = null;
        var match = StopImagePattern.Match(line);
        if (match.Success)
        {
            if (match.Groups["rest"].Value.Trim().Length > 0)
            {
                error = "Команда «Остановить изображение.» останавливает листание своего объекта.";
            }
            return true;
        }
        if (StopImageFamilyPattern.IsMatch(line))
        {
            error = "Формат команды: «Остановить изображение.» — останавливает листание своего объекта.";
            return true;
        }
        return false;
    }

    public static bool TryParseAsk(string line, out string question, out string variableName, out string? error)
    {
        question = string.Empty;
        variableName = string.Empty;
        error = null;
        var match = AskPattern.Match(line);
        if (!match.Success)
        {
            if (AskFamilyPattern.IsMatch(line))
            {
                error = "Формат команды: «Спросить. \"Как тебя зовут?\" имя» — вопрос и имя переменной.";
                return true;
            }
            return false;
        }

        var rest = match.Groups["rest"].Value.Trim();
        var separator = rest.LastIndexOf(' ');
        if (separator <= 0)
        {
            error = "Формат команды: «Спросить. \"Как тебя зовут?\" имя» — вопрос и имя переменной.";
            return true;
        }
        question = rest[..separator].Trim();
        variableName = rest[(separator + 1)..].Trim();
        if (question.Length == 0 || variableName.Length == 0)
        {
            error = "Формат команды: «Спросить. \"Как тебя зовут?\" имя» — вопрос и имя переменной.";
            return true;
        }
        return true;
    }

    public static bool TryParseKeyboardEvent(string line, out string keyToken, out string keyId)
    {
        keyToken = string.Empty;
        keyId = string.Empty;
        var match = KeyboardEventPattern.Match(line);
        if (!match.Success)
        {
            return false;
        }
        keyToken = match.Groups["key"].Value;
        KeyboardKeySyntax.TryNormalize(keyToken, out keyId);
        return true;
    }

    public static bool IsKeyboardEventPrefix(string line) => KeyboardEventFamilyPattern.IsMatch(line);

    public static bool TryParseEveryEvent(string line, out string seconds)
    {
        seconds = string.Empty;
        var match = EveryEventPattern.Match(line);
        if (!match.Success || !int.TryParse(match.Groups["amount"].Value, out var amount) ||
            amount < 1 || amount > int.MaxValue / 1000)
        {
            return false;
        }
        seconds = amount.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    public static bool IsEveryEventPrefix(string line) => EveryEventFamilyPattern.IsMatch(line);

    public static bool IsFrameEvent(string line) => FrameEventPattern.IsMatch(line);

    public static bool IsFrameEventPrefix(string line) => FrameEventFamilyPattern.IsMatch(line);

    public static bool TryReadPositionAxis(string name, out PositionAxis axis)
    {
        axis = PositionAxis.None;
        var trimmed = name.Trim().TrimEnd('.');
        if (trimmed.Equals("Свойство.х", StringComparison.OrdinalIgnoreCase))
        {
            axis = PositionAxis.X;
        }
        else if (trimmed.Equals("Свойство.у", StringComparison.OrdinalIgnoreCase))
        {
            axis = PositionAxis.Y;
        }
        return axis != PositionAxis.None;
    }

    public static string AxisId(PositionAxis axis) => axis == PositionAxis.Y ? "y" : "x";

    public static bool ValidateVariableName(string name, out string? error)
    {
        error = null;
        var trimmed = name.Trim();
        if (trimmed.StartsWith("Свойство.", StringComparison.OrdinalIgnoreCase))
        {
            var propertyName = trimmed["Свойство.".Length..].Trim().TrimEnd('.');
            if (propertyName.Length == 0)
            {
                error = "После «Свойство.» укажи имя свойства: «Свойство.х» или «Свойство.у».";
                return false;
            }
            var knownCommand = propertyName.ToLowerInvariant() switch
            {
                "прозрачность" => "«Задать прозрачность. 50» или «Изменить прозрачность. 10»",
                "размер" => "«Задать размер. 100» или «Изменить размер. 10»",
                "яркость" => "«Задать яркость. 100» или «Изменить яркость. 10»",
                "громкость" => "«Задать громкость. 50»",
                "изображение" => "«Задать изображение. файл.png»",
                _ => null
            };
            error = knownCommand is null
                ? $"Свойство «{propertyName}» так не записывается — доступны только «Свойство.х» и «Свойство.у»."
                : $"Свойство «{propertyName}» так не записывается — используй {knownCommand}.";
            return false;
        }
        if (trimmed.Equals("Свойство", StringComparison.OrdinalIgnoreCase))
        {
            error = PropertyWordError;
            return false;
        }
        if (trimmed.EndsWith('.'))
        {
            error = "Имя переменной не может заканчиваться точкой. Координаты записываются как «Свойство.х» или «Свойство.у».";
            return false;
        }
        return true;
    }

    private static bool IsVisibleVerb(string verb) =>
        !verb.Trim().Equals("спрятать", StringComparison.OrdinalIgnoreCase) &&
        !verb.Trim().Equals("скрыть", StringComparison.OrdinalIgnoreCase);

    private static bool IsNumber(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number);
}
