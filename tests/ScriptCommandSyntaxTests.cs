using IskraStudio;
using Xunit;

namespace IskraStudio.Tests;

public sealed class ScriptCommandSyntaxTests
{
    [Theory]
    [InlineData("Спрятать")]
    [InlineData("скрыть")]
    [InlineData("СПРЯТАТЬ")]
    [InlineData("СпРяТаТь")]
    public void SelfHideCommandsParse(string source)
    {
        Assert.True(ScriptCommandSyntax.TryParseVisibility(source, out var visible, out var target, out var error));
        Assert.False(visible);
        Assert.Null(target);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Показать")]
    [InlineData("показать")]
    [InlineData("ПОКАЗАТЬ")]
    public void SelfShowCommandParses(string source)
    {
        Assert.True(ScriptCommandSyntax.TryParseVisibility(source, out var visible, out var target, out var error));
        Assert.True(visible);
        Assert.Null(target);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Спрятать.монета", "монета", false)]
    [InlineData("Скрыть. монета", "монета", false)]
    [InlineData("скрыть.монета", "монета", false)]
    [InlineData("Показать.красная шапка", "красная шапка", true)]
    [InlineData("Показать . монета", "монета", true)]
    [InlineData("Показать.  монета  ", "монета", true)]
    [InlineData("ПОКАЗАТЬ.Монета", "Монета", true)]
    public void TargetVisibilityParses(string source, string expectedTarget, bool expectedVisible)
    {
        Assert.True(ScriptCommandSyntax.TryParseVisibility(source, out var visible, out var target, out var error));
        Assert.Equal(expectedVisible, visible);
        Assert.Equal(expectedTarget, target);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Показать монета")]
    [InlineData("Спрятать монета")]
    [InlineData("Скрыть монета")]
    [InlineData("Скрыть.")]
    [InlineData("Спрятать. ")]
    [InlineData("Показать.  ")]
    public void MalformedVisibilityProducesError(string source)
    {
        Assert.True(ScriptCommandSyntax.TryParseVisibility(source, out _, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("Прыгнуть вверх")]
    [InlineData("Показатель")]
    [InlineData("Скрыться")]
    [InlineData("Показательный")]
    [InlineData("Спрятатьмонета")]
    public void UnrelatedLineIsNotVisibility(string source) =>
        Assert.False(ScriptCommandSyntax.TryParseVisibility(source, out _, out _, out _));

    [Theory]
    [InlineData("Подвинуть. 10 -5", "10 -5")]
    [InlineData("подвинуть . -3 7", "-3 7")]
    [InlineData("Подвинуть. -10 5", "-10 5")]
    [InlineData("Подвинуть. 0 0", "0 0")]
    [InlineData("Подвинуть .10 -5", "10 -5")]
    [InlineData("Подвинуть.10 -5", "10 -5")]
    [InlineData("Подвинуть. 999999999999 -5", "999999999999 -5")]
    public void MoveByParses(string source, string expectedOffset)
    {
        Assert.True(ScriptCommandSyntax.TryParseMoveBy(source, out var offset, out var error));
        Assert.Equal(expectedOffset, offset);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Подвинуть 10 -5")]
    [InlineData("Подвинуть. 10.5 -5")]
    [InlineData("Подвинуть. +5 -5")]
    [InlineData("Подвинуть. 10")]
    [InlineData("Подвинуть.")]
    [InlineData("Подвинуть. 10 -5 3")]
    public void MalformedMoveByProducesError(string source)
    {
        Assert.True(ScriptCommandSyntax.TryParseMoveBy(source, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void UnrelatedLineIsNotMoveBy() =>
        Assert.False(ScriptCommandSyntax.TryParseMoveBy("Подвинуться 10 -5", out _, out _));

    [Theory]
    [InlineData("Запустить.сцену.главное меню", false, "главное меню")]
    [InlineData("Запустить.сцену.главное.меню", false, "главное.меню")]
    [InlineData("Продолжить.сцену.урок", true, "урок")]
    [InlineData("запустить . сцену . Главное меню", false, "Главное меню")]
    [InlineData("Запустить.СЦЕНУ.урок.1", false, "урок.1")]
    [InlineData("Продолжить . сцену.урок", true, "урок")]
    public void SceneCommandsParse(string source, bool expectedResume, string expectedName)
    {
        Assert.True(ScriptCommandSyntax.TryParseSceneCommand(source, out var resume, out var name, out var error));
        Assert.Equal(expectedResume, resume);
        Assert.Equal(expectedName, name);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Запустить сцену урок")]
    [InlineData("Запустить.сцену.")]
    [InlineData("Продолжить сцену")]
    [InlineData("Запустить.сцену")]
    [InlineData("Продолжить.сцену.")]
    [InlineData("Продолжить.сцену")]
    [InlineData("Запустить.сцены.урок")]
    public void MalformedSceneCommandsProduceError(string source)
    {
        Assert.True(ScriptCommandSyntax.TryParseSceneCommand(source, out _, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("Зациклить звук.урок")]
    [InlineData("Запуститьсцену.урок")]
    public void UnrelatedLineIsNotSceneCommand(string source) =>
        Assert.False(ScriptCommandSyntax.TryParseSceneCommand(source, out _, out _, out _));

    [Theory]
    [InlineData("Сменить.ориентацию", "toggle")]
    [InlineData("сменить.ориентацию.Горизонтальная", "horizontal")]
    [InlineData("Сменить.ориентацию.вертикальная", "vertical")]
    [InlineData("Сменить . ориентацию . ГОРИЗОНТАЛЬНАЯ", "horizontal")]
    public void OrientationCommandsParse(string source, string expectedMode)
    {
        Assert.True(ScriptCommandSyntax.TryParseOrientationCommand(source, out var mode, out var error));
        Assert.Equal(expectedMode, mode);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Сменить ориентацию")]
    [InlineData("Сменить.ориентацию.")]
    [InlineData("Сменить.ориентацию.диагональная")]
    [InlineData("Сменить.ориентация")]
    public void MalformedOrientationProducesError(string source)
    {
        Assert.True(ScriptCommandSyntax.TryParseOrientationCommand(source, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void UnrelatedLineIsNotOrientation() =>
        Assert.False(ScriptCommandSyntax.TryParseOrientationCommand("Смена состава", out _, out _));

    [Theory]
    [InlineData("Зациклить звук.тема.mp3", "тема.mp3")]
    [InlineData("зациклить звук. тема игр.mp3", "тема игр.mp3")]
    [InlineData("Зациклить звук. файл.WAV", "файл.WAV")]
    public void LoopSoundParses(string source, string expectedFile)
    {
        Assert.True(ScriptCommandSyntax.TryParseLoopSound(source, out var file, out var error));
        Assert.Equal(expectedFile, file);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Зациклить звук.")]
    [InlineData("Зациклить звук")]
    [InlineData("Зациклить.звук.тема.mp3")]
    public void MalformedLoopSoundProducesError(string source)
    {
        Assert.True(ScriptCommandSyntax.TryParseLoopSound(source, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void UnrelatedLineIsNotLoopSound() =>
        Assert.False(ScriptCommandSyntax.TryParseLoopSound("Зациклитьзвук.тема.mp3", out _, out _));

    [Theory]
    [InlineData("Остановить звук.тревога.wav", "тревога.wav")]
    [InlineData("остановить звук. тревога.wav", "тревога.wav")]
    public void StopSoundParses(string source, string expectedFile)
    {
        Assert.True(ScriptCommandSyntax.TryParseStopSound(source, out var file, out var error));
        Assert.Equal(expectedFile, file);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Остановить звук")]
    [InlineData("Остановить звук.")]
    public void MalformedStopSoundProducesError(string source)
    {
        Assert.True(ScriptCommandSyntax.TryParseStopSound(source, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("Остановить.звук.тема.mp3")]
    [InlineData("Остановитьзвук.тема.mp3")]
    public void UnrelatedLineIsNotStopSound(string source) =>
        Assert.False(ScriptCommandSyntax.TryParseStopSound(source, out _, out _));

    [Theory]
    [InlineData("Задать громкость. 40", null, "40")]
    [InlineData("Задать громкость. 0", null, "0")]
    [InlineData("Задать громкость. -5", null, "-5")]
    [InlineData("задать громкость . 40.5", null, "40.5")]
    public void GlobalVolumeParses(string source, string? expectedFile, string expectedAmount)
    {
        Assert.True(ScriptCommandSyntax.TryParseVolume(source, out var file, out var amount, out var error));
        Assert.Equal(expectedFile, file);
        Assert.Equal(expectedAmount, amount);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Задать громкость.тема.mp3 40", "тема.mp3", "40")]
    [InlineData("Задать громкость.тема игр.mp3 40", "тема игр.mp3", "40")]
    [InlineData("Задать громкость.тема 40 50", "тема 40", "50")]
    [InlineData("Задать громкость. 50 60", "50", "60")]
    public void SingleSoundVolumeParses(string source, string expectedFile, string expectedAmount)
    {
        Assert.True(ScriptCommandSyntax.TryParseVolume(source, out var file, out var amount, out var error));
        Assert.Equal(expectedFile, file);
        Assert.Equal(expectedAmount, amount);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Задать громкость")]
    [InlineData("Задать громкость.")]
    [InlineData("Задать громкость.тема.mp3")]
    [InlineData("Задать громкость.тема.mp3 пять")]
    public void MalformedVolumeProducesError(string source)
    {
        Assert.True(ScriptCommandSyntax.TryParseVolume(source, out _, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void UnrelatedLineIsNotVolume() =>
        Assert.False(ScriptCommandSyntax.TryParseVolume("Задать громкостьx.", out _, out _, out _));

    [Theory]
    [InlineData("Проиграть изображение.8", 8, null)]
    [InlineData("проиграть изображение. 4", 4, null)]
    [InlineData("Проиграть изображение.1", 1, null)]
    [InlineData("Проиграть изображение.+8", 8, null)]
    [InlineData("Проиграть изображение.монстр 8", 8, "монстр")]
    [InlineData("Проиграть изображение.монстр.злой 4", 4, "монстр.злой")]
    [InlineData("Проиграть изображение.монстр злой 8", 8, "монстр злой")]
    public void PlayImageParses(string source, int expectedSpeed, string? expectedObject)
    {
        Assert.True(ScriptCommandSyntax.TryParsePlayImage(source, out var speed, out var objectName, out var error));
        Assert.Equal(expectedSpeed, speed);
        Assert.Equal(expectedObject, objectName);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Проиграть изображение.")]
    [InlineData("Проиграть изображение.0")]
    [InlineData("Проиграть изображение.-8")]
    [InlineData("Проиграть изображение.1.5")]
    [InlineData("Проиграть изображение.999999999999")]
    [InlineData("Проиграть изображение.монстр")]
    [InlineData("Проиграть изображение.монстр медленно")]
    [InlineData("Проиграть.изображение.8")]
    [InlineData("Проиграть изображению.8")]
    [InlineData("Проиграть мелодию")]
    public void MalformedPlayImageProducesError(string source)
    {
        Assert.True(ScriptCommandSyntax.TryParsePlayImage(source, out _, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void UnrelatedLineIsNotPlayImage() =>
        Assert.False(ScriptCommandSyntax.TryParsePlayImage("Проигранный матч", out _, out _, out _));

    [Theory]
    [InlineData("Остановить изображение.")]
    [InlineData("остановить изображение")]
    [InlineData("Остановить  изображение .")]
    public void StopImageParses(string source)
    {
        Assert.True(ScriptCommandSyntax.TryParseStopImage(source, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Остановить изображение.монстр")]
    [InlineData("Остановить изображение монстр")]
    public void StopImageWithTargetProducesError(string source)
    {
        Assert.True(ScriptCommandSyntax.TryParseStopImage(source, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("Остановить.изображение.")]
    [InlineData("Остановитьзвук.тема.mp3")]
    public void UnrelatedLineIsNotStopImage(string source) =>
        Assert.False(ScriptCommandSyntax.TryParseStopImage(source, out _));

    [Theory]
    [InlineData("Спросить. \"Как тебя зовут?\" имя", "\"Как тебя зовут?\"", "имя")]
    [InlineData("Спросить.Как дела? ответ", "Как дела?", "ответ")]
    [InlineData("Спросить. Ты готов? ответ", "Ты готов?", "ответ")]
    [InlineData("Спросить . да или нет? выбор", "да или нет?", "выбор")]
    [InlineData("Спросить.\"Сколько лет?\" возраст", "\"Сколько лет?\"", "возраст")]
    public void AskParses(string source, string expectedQuestion, string expectedName)
    {
        Assert.True(ScriptCommandSyntax.TryParseAsk(source, out var question, out var name, out var error));
        Assert.Equal(expectedQuestion, question);
        Assert.Equal(expectedName, name);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Спросить.")]
    [InlineData("Спросить.имя")]
    [InlineData("Спросить. вопрос?")]
    public void MalformedAskProducesError(string source)
    {
        Assert.True(ScriptCommandSyntax.TryParseAsk(source, out _, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void UnrelatedLineIsNotAsk() =>
        Assert.False(ScriptCommandSyntax.TryParseAsk("Спросить вопрос? ответ", out _, out _, out _));

    [Theory]
    [InlineData("Свойство.х", "X")]
    [InlineData("свойство.у", "Y")]
    [InlineData("Свойство.У.", "Y")]
    [InlineData("Свойство.заяц", "None")]
    [InlineData("счёт", "None")]
    [InlineData("Свойство", "None")]
    [InlineData("  Свойство.х  ", "X")]
    [InlineData("Свойство.х..", "X")]
    [InlineData("свойство.Х", "X")]
    [InlineData("Свойство.уу", "None")]
    [InlineData("Свойство. х", "None")]
    [InlineData("", "None")]
    public void PositionAxisRecognized(string source, string expected)
    {
        ScriptCommandSyntax.TryReadPositionAxis(source, out var axis);
        Assert.Equal(expected, axis.ToString());
    }

    [Fact]
    public void AxisIdsAreAscii()
    {
        Assert.Equal("x", ScriptCommandSyntax.AxisId(PositionAxis.X));
        Assert.Equal("y", ScriptCommandSyntax.AxisId(PositionAxis.Y));
    }

    [Theory]
    [InlineData("счёт")]
    [InlineData("Жизни")]
    [InlineData("х2")]
    [InlineData("настроение")]
    public void ValidVariableNamePasses(string name)
    {
        Assert.True(ScriptCommandSyntax.ValidateVariableName(name, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("Свойство.заяц", "заяц", "Свойство.х")]
    [InlineData("Свойство.прозрачность", "прозрачность", "прозрачность. 10")]
    [InlineData("свойство.размер", "размер", "размер. 100")]
    [InlineData("Свойство.яркость", "яркость", "яркость. 100")]
    [InlineData("Свойство.громкость", "громкость", "громкость. 50")]
    [InlineData("Свойство.изображение", "изображение", "изображение. файл.png")]
    public void ReservedPropertyNamesReportHints(string name, string propertyName, string hint)
    {
        Assert.False(ScriptCommandSyntax.ValidateVariableName(name, out var error));
        Assert.Contains(propertyName, error);
        Assert.Contains(hint, error);
    }

    [Fact]
    public void BarePropertyWordIsRejected()
    {
        Assert.False(ScriptCommandSyntax.ValidateVariableName("Свойство", out var error));
        Assert.Contains("зарезервировано", error);
        Assert.Equal(ScriptCommandSyntax.PropertyWordError, error);
    }

    [Fact]
    public void EmptyPropertyNameIsRejected()
    {
        Assert.False(ScriptCommandSyntax.ValidateVariableName("свойство. ", out var error));
        Assert.Contains("укажи имя свойства", error);
    }

    [Theory]
    [InlineData("счёт.")]
    [InlineData("имя. ")]
    [InlineData("переменная.")]
    public void DottedVariableNamesAreRejected(string name)
    {
        Assert.False(ScriptCommandSyntax.ValidateVariableName(name, out var error));
        Assert.Contains("заканчиваться точкой", error);
    }
}
