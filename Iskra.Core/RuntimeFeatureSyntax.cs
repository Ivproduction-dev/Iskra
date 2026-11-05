using System.Globalization;
using System.Text.RegularExpressions;

namespace IskraStudio;

internal static class RuntimeFeatureSyntax
{
    private static readonly Regex MoveToPattern = new(
        "^Двигать\\s*\\.\\s*до\\s*\\.\\s*(?<x>[^\\s]+)\\s+(?<y>[^\\s]+)\\s+за\\s*\\.\\s*(?<duration>[+]?(?:\\d+(?:\\.\\d*)?|\\.\\d+))\\s*(?<unit>секунд(?:а|ы|у|е|ой|ами|ах)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SetLayerPattern = new(
        "^Задать\\s+слой\\s*\\.\\s*(?<value>[+-]?\\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ChangeLayerPattern = new(
        "^Изменить\\s+слой\\s*\\.\\s*(?<value>[+-]?\\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SetVisibilityPattern = new(
        "^Задать\\s+видимость\\s*\\.\\s*(?<value>истина|ложь|да|нет|true|false)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SetMotionTypePattern = new(
        "^Задать\\s+тип\\s*\\.\\s*движения\\s*\\.\\s*(?<value>статичный|статичн(?:ый|ая|ое)|динамичный|динамичн(?:ый|ая|ое)|без)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SetGravityPattern = new(
        "^Задать\\s+гравитацию\\s*\\.\\s*сцены\\s*\\.\\s*(?<x>[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+))\\s+(?<y>[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FlipPattern = new(
        "^Отразить\\s*\\.\\s*по\\s*\\.\\s*(?<axis>х|x|у|y)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SoundControlPattern = new(
        "^(?<verb>Приостановить|Продолжить)\\s+звук\\s*\\.\\s*(?<file>.+?)\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ChangeSoundVolumePattern = new(
        "^Изменить\\s+громкость\\s*\\.\\s*(?<rest>.+?)\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RepeatSoundPattern = new(
        "^Задать\\s+повтор\\s*\\.\\s*звука\\s*\\.\\s*(?<rest>.+?)\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SoundEventPattern = new(
        "^При\\s*\\.\\s*окончании\\s*\\.\\s*звука\\s*\\.\\s*(?<file>.+?)\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryParseMoveTo(string line, out string argument, out string? error)
    {
        argument = string.Empty;
        error = null;
        var match = MoveToPattern.Match(line);
        if (match.Success && TryFinite(match.Groups["duration"].Value, out var seconds) && seconds > 0d && seconds <= 86_400d)
        {
            var milliseconds = seconds * 1000d;
            argument = $"{match.Groups["x"].Value} {match.Groups["y"].Value} {Format(milliseconds)}";
            return true;
        }
        if (line.StartsWith("Двигать", StringComparison.OrdinalIgnoreCase))
        {
            error = "Формат команды: «Двигать.до. 200 160 за. 1 секунд»; укажи координаты и длительность.";
            return true;
        }
        return false;
    }

    public static bool TryParseStopMovement(string line) =>
        Regex.IsMatch(line, "^Остановить\\s+движение\\s*\\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryParseLayer(string line, out string kind, out string argument, out string? error)
    {
        kind = string.Empty;
        argument = string.Empty;
        error = null;
        var set = SetLayerPattern.Match(line);
        var change = ChangeLayerPattern.Match(line);
        if (set.Success || change.Success)
        {
            var match = set.Success ? set : change;
            kind = set.Success ? "set-layer" : "change-layer";
            argument = match.Groups["value"].Value;
            return true;
        }
        if (Regex.IsMatch(line, "^(?:Задать|Изменить)\\s+слой(?:\\s|\\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            error = "Формат команды: «Задать слой. 3» или «Изменить слой. 1».";
            return true;
        }
        return false;
    }

    public static bool TryParseVisibility(string line, out bool visible, out bool toggle, out string? error)
    {
        visible = false;
        toggle = false;
        error = null;
        if (Regex.IsMatch(line, "^Переключить\\s+видимость\\s*\\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            toggle = true;
            return true;
        }
        var match = SetVisibilityPattern.Match(line);
        if (match.Success)
        {
            visible = match.Groups["value"].Value.ToLowerInvariant() is "истина" or "да" or "true";
            return true;
        }
        if (Regex.IsMatch(line, "^(?:Задать\\s+видимость|Переключить\\s+видимость)(?:\\s|\\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            error = "Формат команды: «Задать видимость. истина/ложь» или «Переключить видимость».";
            return true;
        }
        return false;
    }

    public static bool TryParseMotionType(string line, out string type, out string? error)
    {
        type = string.Empty;
        error = null;
        var match = SetMotionTypePattern.Match(line);
        if (match.Success)
        {
            var value = match.Groups["value"].Value.ToLowerInvariant();
            type = value.StartsWith("динамич", StringComparison.Ordinal) ? "dynamic" :
                value == "без" ? "none" : "static";
            return true;
        }
        if (Regex.IsMatch(line, "^Задать\\s+тип(?:\\s|\\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            error = "Формат команды: «Задать тип.движения. статичный», «динамичный» или «без».";
            return true;
        }
        return false;
    }

    public static bool TryParseGravity(string line, out string argument, out string? error)
    {
        argument = string.Empty;
        error = null;
        var match = SetGravityPattern.Match(line);
        if (match.Success && TryFinite(match.Groups["x"].Value, out var x) && TryFinite(match.Groups["y"].Value, out var y))
        {
            argument = $"{Format(x)} {Format(y)}";
            return true;
        }
        if (Regex.IsMatch(line, "^Задать\\s+гравитацию(?:\\s|\\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            error = "Формат команды: «Задать гравитацию.сцены. 0 -10» — ускорение по X и Y.";
            return true;
        }
        return false;
    }

    public static bool TryParseFlip(string line, out string axis, out string? error)
    {
        axis = string.Empty;
        error = null;
        var match = FlipPattern.Match(line);
        if (match.Success)
        {
            axis = match.Groups["axis"].Value.ToLowerInvariant() is "х" or "x" ? "x" : "y";
            return true;
        }
        if (line.StartsWith("Отразить", StringComparison.OrdinalIgnoreCase))
        {
            error = "Формат команды: «Отразить. по. х» или «Отразить. по. у».";
            return true;
        }
        return false;
    }

    public static bool TryParseSoundControl(string line, out string kind, out string argument, out string? error)
    {
        kind = string.Empty;
        argument = string.Empty;
        error = null;
        var control = SoundControlPattern.Match(line);
        if (control.Success)
        {
            kind = control.Groups["verb"].Value.Equals("Приостановить", StringComparison.OrdinalIgnoreCase) ? "pause-sound" : "resume-sound";
            argument = control.Groups["file"].Value.Trim();
            return true;
        }
        var repeat = RepeatSoundPattern.Match(line);
        if (repeat.Success)
        {
            var rest = repeat.Groups["rest"].Value.Trim();
            var split = rest.LastIndexOf(' ');
            if (split > 0 && TryBoolean(rest[(split + 1)..], out var enabled))
            {
                kind = "set-sound-repeat";
                argument = $"{rest[..split].Trim()} {enabled.ToString()}";
                return true;
            }
            error = "Формат команды: «Задать повтор.звука. тема.mp3 истина/ложь».";
            return true;
        }
        var volume = ChangeSoundVolumePattern.Match(line);
        if (volume.Success)
        {
            if (TryParseFileAmount(volume.Groups["rest"].Value, out var file, out var amount))
            {
                kind = "change-sound-volume";
                argument = $"{Format(amount)} {file}";
                return true;
            }
            error = "Формат команды: «Изменить громкость. тема.mp3 -10».";
            return true;
        }
        if (Regex.IsMatch(line, "^(?:Приостановить|Продолжить)\\s+звук(?:\\s|\\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            error = "После команды укажи файл: «Приостановить звук. тема.mp3» или «Продолжить звук. тема.mp3».";
            return true;
        }
        if (Regex.IsMatch(line, "^Задать\\s+повтор(?:\\s|\\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            error = "Формат команды: «Задать повтор.звука. тема.mp3 истина/ложь».";
            return true;
        }
        if (Regex.IsMatch(line, "^Изменить\\s+громкость(?:\\s|\\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            error = "Формат команды: «Изменить громкость. тема.mp3 -10».";
            return true;
        }
        return false;
    }

    public static bool TryParseSoundEvent(string line, out string file)
    {
        var match = SoundEventPattern.Match(line);
        file = match.Success ? match.Groups["file"].Value.Trim() : string.Empty;
        return match.Success && file.Length > 0;
    }

    public static bool IsSoundEventPrefix(string line) =>
        Regex.IsMatch(line, "^При\\s*\\.\\s*окончании\\s*\\.\\s*звука(?:\\s|\\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool TryParseFileAmount(string raw, out string file, out double amount)
    {
        file = string.Empty;
        amount = 0;
        var rest = raw.Trim();
        var split = rest.LastIndexOf(' ');
        if (split <= 0 || !TryFinite(rest[(split + 1)..], out amount)) return false;
        file = rest[..split].Trim();
        return file.Length > 0;
    }

    private static bool TryBoolean(string value, out bool result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "истина": case "да": case "true": result = true; return true;
            case "ложь": case "нет": case "false": result = false; return true;
            default: result = false; return false;
        }
    }

    private static bool TryFinite(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
