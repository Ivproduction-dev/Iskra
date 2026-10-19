using System.Reflection;
using IskraStudio;
using Xunit;

namespace IskraStudio.Tests;

public sealed class ScriptCompileIntegrationTests
{
    [Fact]
    public void NewCommandsCompileInOrder()
    {
        var compiled = Compile("""
            При. запуске
            Задать счёт = 5
            Задать Свойство.х = 5
            Изменить Свойство.у -3
            Изменить счёт 1
            Спрятать
            Показать.монета
            Подвинуть. 10 -5
            Запустить.сцену.Вторая
            Продолжить.сцену.Вторая
            Сменить.ориентацию.вертикальная
            Спросить. "Как дела?" ответ
            Печать счёт
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(
            new[] { "event", "set-value", "set-position", "change-position", "change-value", "hide", "show",
                "move-by", "launch-scene", "resume-scene", "set-orientation", "ask", "print" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal("start", compiled.Commands[0].Argument);
        Assert.Equal("x 5", compiled.Commands[2].Argument);
        Assert.Equal("y -3", compiled.Commands[3].Argument);
        Assert.Equal("10 -5", compiled.Commands[7].Argument);
        Assert.Equal("Вторая", compiled.Commands[8].Argument);
        Assert.Equal("vertical", compiled.Commands[10].Argument);
        Assert.Equal("\"Как дела?\" ответ", compiled.Commands[11].Argument);
        Assert.Equal(compiled.Item.Id, compiled.Commands[5].TargetObjectId);
        Assert.Equal(compiled.Target.Id, compiled.Commands[6].TargetObjectId);
    }

    [Fact]
    public void NewEventsCompileWithEndMarkers()
    {
        var compiled = Compile("""
            При. нажатии клавиши.ф
            Печать нажал
            Конец
            Каждые.2.секунды
            Печать тик
            Конец
            Каждый кадр
            Конец
            При. запуске
            Печать старт
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(
            new[] { "event", "print", "event-end", "event", "print", "event-end", "event", "event-end", "event", "print" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal("key", compiled.Commands[0].Argument);
        Assert.Equal("a", compiled.Commands[0].Condition);
        Assert.Equal("every", compiled.Commands[3].Argument);
        Assert.Equal("2", compiled.Commands[3].Condition);
        Assert.Equal("frame", compiled.Commands[6].Argument);
        Assert.Null(compiled.Commands[6].Condition);
    }

    [Fact]
    public void MalformedCommandsProduceRussianErrors()
    {
        var compiled = Compile("""
            При. запуске
            Показать монета
            Задать х. = 1
            Задать Свойство.размер = 10
            Подвинуть 10 -5
            """);

        var errors = Errors(compiled);
        Assert.Equal(4, errors.Count);
        Assert.Contains(errors, message => message.Contains("Показать.монета"));
        Assert.Contains(errors, message => message.Contains("заканчиваться точкой"));
        Assert.Contains(errors, message => message.Contains("Задать размер. 100"));
        Assert.Contains(errors, message => message.Contains("Подвинуть. 10 -5"));
    }

    [Fact]
    public void UnknownSceneIsReported()
    {
        var compiled = Compile("""
            При. запуске
            Запустить.сцену.Луна
            """);

        var errors = Errors(compiled);
        Assert.Single(errors);
        Assert.Contains("Луна", errors[0]);
        Assert.Contains("не найдена", errors[0]);
    }

    [Fact]
    public void TimedEventWithoutEndIsReported()
    {
        var compiled = Compile("""
            Каждые.2.секунды
            Печать тик
            """);

        var errors = Errors(compiled);
        Assert.Single(errors);
        Assert.Contains("Каждые.2.секунды", errors[0]);
        Assert.Contains("Конец", errors[0]);
    }

    [Fact]
    public void CommandsAfterEventEndAreSkipped()
    {
        var compiled = Compile("""
            При. запуске
            Печать раз
            Конец
            Печать два
            """);

        Assert.Empty(Errors(compiled));
        Assert.Equal(new[] { "event", "print", "event-end" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Contains(compiled.Diagnostics, item =>
            item.Severity == RuntimeDiagnosticSeverity.Warning && item.Message.Contains("вне событий"));
    }

    [Fact]
    public void ConditionPropertiesAreValidated()
    {
        var compiled = Compile("""
            Когда. Свойство.коснулся.монета = истина
            Конец
            Когда. Свойство.клавиша.пробел
            Конец
            Когда. Свойство.коснулся.призрак
            Конец
            Когда. Свойство.клавиша.прыжок
            Конец
            """);

        var errors = Errors(compiled);
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, message => message.Contains("призрак"));
        Assert.Contains(errors, message => message.Contains("прыжок"));
    }

    [Fact]
    public void SoundsAndImagesReportMissingResources()
    {
        var compiled = Compile("""
            При. запуске
            Зациклить звук.тема.mp3
            Остановить звук.тема.mp3
            Задать громкость. 50
            Задать громкость.тема.mp3 40
            Проиграть изображение. 8
            Проиграть изображение.призрак 8
            """);

        var errors = Errors(compiled);
        Assert.Equal(4, errors.Count);
        Assert.Equal(3, errors.Count(message => message.Contains("тема.mp3")));
        Assert.Contains(errors, message => message.Contains("призрак"));
        Assert.Equal(new[] { "event", "set-volume" }, compiled.Commands.Select(command => command.Kind));
        Assert.Equal("50", compiled.Commands[1].Argument);
        Assert.Contains(compiled.Diagnostics, item =>
            item.Severity == RuntimeDiagnosticSeverity.Warning && item.Message.Contains("изображений"));
    }

    [Fact]
    public void VisibilityTargetsCompileForNamedObjects()
    {
        var compiled = Compile("""
            При. запуске
            Скрыть
            Спрятать. красная шапка
            Показать.красная шапка
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "hide", "hide", "show" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal(compiled.Item.Id, compiled.Commands[1].TargetObjectId);
        Assert.Equal(compiled.Third.Id, compiled.Commands[2].TargetObjectId);
        Assert.Equal(compiled.Third.Id, compiled.Commands[3].TargetObjectId);
        Assert.Equal("красная шапка", compiled.Commands[3].Argument);
    }

    [Fact]
    public void UnknownVisibilityTargetIsReported()
    {
        var compiled = Compile("""
            При. запуске
            Показать.призрак
            """);

        var errors = Errors(compiled);
        Assert.Single(errors);
        Assert.Contains("призрак", errors[0]);
        Assert.Contains("не найден", errors[0]);
    }

    [Fact]
    public void PositionWritesCompile()
    {
        var compiled = Compile("""
            При. запуске
            Задать Свойство.х = 100
            Задать Свойство.У = 0
            Изменить Свойство.х -10
            Изменить Свойство.у. 25
            Задать Свойство.х = счёт
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(
            new[] { "event", "set-position", "set-position", "change-position", "change-position", "set-position" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal(new[] { "x 100", "y 0", "x -10", "y 25", "x счёт" },
            compiled.Commands.Skip(1).Select(command => command.Argument));
    }

    [Fact]
    public void MoveByCompiles()
    {
        var compiled = Compile("""
            При. запуске
            Подвинуть. 0 0
            Подвинуть. -40 15
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "move-by", "move-by" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal(new[] { "0 0", "-40 15" },
            compiled.Commands.Skip(1).Select(command => command.Argument));
    }

    [Fact]
    public void SceneCommandsUseActualNameCasing()
    {
        var compiled = Compile("""
            При. запуске
            Запустить.сцену.вторая
            Продолжить.сцену.ПЛАНЕТА.МАРС
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "launch-scene", "resume-scene" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal("Вторая", compiled.Commands[1].Argument);
        Assert.Equal("Планета.Марс", compiled.Commands[2].Argument);
    }

    [Fact]
    public void OrientationWithoutModeToggles()
    {
        var compiled = Compile("""
            При. запуске
            Сменить.ориентацию
            Сменить.ориентацию.горизонтальная
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "set-orientation", "set-orientation" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal("toggle", compiled.Commands[1].Argument);
        Assert.Equal("horizontal", compiled.Commands[2].Argument);
    }

    [Fact]
    public void VolumeWithinRangeCompiles()
    {
        var compiled = Compile("""
            При. запуске
            Задать громкость. 0
            Задать громкость. 100
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "set-volume", "set-volume" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal("0", compiled.Commands[1].Argument);
        Assert.Equal("100", compiled.Commands[2].Argument);
    }

    [Fact]
    public void VolumeOutOfRangeIsWarned()
    {
        var compiled = Compile("""
            При. запуске
            Задать громкость. 101
            Задать громкость. -1
            """);

        Assert.Empty(Errors(compiled));
        Assert.Equal(new[] { "event" }, compiled.Commands.Select(command => command.Kind));
        Assert.Equal(2, compiled.Diagnostics.Count(item =>
            item.Severity == RuntimeDiagnosticSeverity.Warning && item.Message.Contains("диапазона")));
    }

    [Fact]
    public void VolumeWithoutDotIsReported()
    {
        var compiled = Compile("""
            При. запуске
            Задать громкость
            """);

        var errors = Errors(compiled);
        Assert.Single(errors);
        Assert.Contains("Задать громкость.", errors[0]);
    }

    [Fact]
    public void ImageSpeedBelowOneIsReported()
    {
        var compiled = Compile("""
            При. запуске
            Проиграть изображение.0
            """);

        var errors = Errors(compiled);
        Assert.Single(errors);
        Assert.Contains("Скорость листания", errors[0]);
    }

    [Fact]
    public void StopImageCompiles()
    {
        var compiled = Compile("""
            При. запуске
            Остановить изображение.
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "stop-image" },
            compiled.Commands.Select(command => command.Kind));
    }

    [Fact]
    public void AskCompilesWithQuotedQuestion()
    {
        var compiled = Compile("""
            При. запуске
            Спросить. "Сколько тебе лет?" возраст
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "ask" }, compiled.Commands.Select(command => command.Kind));
        Assert.Equal("\"Сколько тебе лет?\" возраст", compiled.Commands[1].Argument);
    }

    [Fact]
    public void AskReservedNameIsReported()
    {
        var compiled = Compile("""
            При. запуске
            Спросить. Что? Свойство
            """);

        var errors = Errors(compiled);
        Assert.Single(errors);
        Assert.Contains("зарезервировано", errors[0]);
    }

    [Fact]
    public void AskDottedNameIsReported()
    {
        var compiled = Compile("""
            При. запуске
            Спросить. Что? имя.
            """);

        var errors = Errors(compiled);
        Assert.Single(errors);
        Assert.Contains("заканчиваться точкой", errors[0]);
    }

    [Fact]
    public void OldEventsNeedNoEnd()
    {
        var compiled = Compile("""
            При. нажатии
            Печать щёлк
            При. наведении
            Печать навёл
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "print", "event", "print" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal("press", compiled.Commands[0].Argument);
        Assert.Equal("hover", compiled.Commands[2].Argument);
    }

    [Fact]
    public void DuplicateEndIsWarned()
    {
        var compiled = Compile("""
            При. запуске
            Печать раз
            Конец
            Конец
            """);

        Assert.Empty(Errors(compiled));
        Assert.Equal(new[] { "event", "print", "event-end" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Single(compiled.Diagnostics);
        Assert.Contains("вне событий", compiled.Diagnostics[0].Message);
    }

    [Fact]
    public void ConditionInsideKeyEventCompiles()
    {
        var compiled = Compile("""
            При. нажатии клавиши.ф
            Если Свойство.клавиша.ф = истина то
            Печать летит
            Конец
            Конец
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "branch-false", "print", "event-end" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal("a", compiled.Commands[0].Condition);
    }

    [Fact]
    public void IfElseInsideEventCompiles()
    {
        var compiled = Compile("""
            При. запуске
            Если счёт = 5 то
            Печать пять
            Иначе
            Печать другое
            Конец
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "branch-false", "print", "jump", "print" },
            compiled.Commands.Select(command => command.Kind));
    }

    [Fact]
    public void NewEventInterruptsOldEvent()
    {
        var compiled = Compile("""
            При. запуске
            Печать старт
            Каждые.2.секунды
            Печать тик
            Конец
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "print", "event", "print", "event-end" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal("start", compiled.Commands[0].Argument);
        Assert.Equal("every", compiled.Commands[2].Argument);
    }

    [Fact]
    public void TimedEventMissingEndBeforeNextEventIsReported()
    {
        var compiled = Compile("""
            Каждые.2.секунды
            Печать тик
            При. запуске
            Печать старт
            """);

        var errors = Errors(compiled);
        Assert.Single(errors);
        Assert.Contains("Каждые.2.секунды", errors[0]);
        Assert.Contains("Конец", errors[0]);
    }

    [Fact]
    public void KeyboardEventAcceptsClassicWording()
    {
        var compiled = Compile("""
            При. нажатии клавиша.пробел
            Конец
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "event-end" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal("пробел", compiled.Commands[0].Condition);
    }

    [Fact]
    public void TimerAndFrameEventsCompile()
    {
        var compiled = Compile("""
            Каждые . 2 . секунды
            Конец
            Каждый.кадр
            Конец
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "event-end", "event", "event-end" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal("2", compiled.Commands[0].Condition);
        Assert.Equal("frame", compiled.Commands[2].Argument);
    }

    [Fact]
    public void ConditionEventCompiles()
    {
        var compiled = Compile("""
            Как.Только счёт = 5
            Печать дождался
            Конец
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "print", "event-end" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal("when", compiled.Commands[0].Argument);
        Assert.Equal("счёт = 5", compiled.Commands[0].Condition);
    }

    [Fact]
    public void BadKeyConditionIsReported()
    {
        var compiled = Compile("""
            При. запуске
            Если Свойство.клавиша.прыжок = истина то
            Печать нет
            Конец
            """);

        var errors = Errors(compiled);
        Assert.Single(errors);
        Assert.Contains("прыжок", errors[0]);
    }

    [Fact]
    public void BadTouchedConditionIsReported()
    {
        var compiled = Compile("""
            При. запуске
            Если Свойство.коснулся.призрак = истина то
            Печать нет
            Конец
            """);

        var errors = Errors(compiled);
        Assert.Single(errors);
        Assert.Contains("призрак", errors[0]);
    }

    [Fact]
    public void UnknownCommandIsReported()
    {
        var compiled = Compile("""
            При. запуске
            Прыгнуть вверх
            """);

        var errors = Errors(compiled);
        Assert.Single(errors);
        Assert.Contains("Прыгнуть", errors[0]);
        Assert.Contains("Неизвестная", errors[0]);
    }

    [Fact]
    public void GluedVisibilityWordIsUnknownCommand()
    {
        var compiled = Compile("""
            При. запуске
            Спрятатьмонета
            """);

        var errors = Errors(compiled);
        Assert.Single(errors);
        Assert.Contains("Спрятатьмонета", errors[0]);
    }

    [Fact]
    public void LegacyEffectsStillCompile()
    {
        var compiled = Compile("""
            При. запуске
            Ждать 2 секунды
            Поместить 10 20
            Задать прозрачность. 50
            Изменить прозрачность. -10
            Задать размер. 150
            Изменить размер. -5
            Задать яркость. 80
            Изменить яркость. -20
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(
            new[] { "event", "wait", "place", "set-transparency", "change-transparency",
                "set-size", "change-size", "set-brightness", "change-brightness" },
            compiled.Commands.Select(command => command.Kind));
        Assert.Equal("2000", compiled.Commands[1].Argument);
        Assert.Equal("10 20", compiled.Commands[2].Argument);
    }

    [Fact]
    public void InlineIfCompiles()
    {
        var compiled = Compile("""
            При. запуске
            Если счёт = 5 то Печать пять конец
            """);

        Assert.Empty(compiled.Diagnostics);
        Assert.Equal(new[] { "event", "branch-false", "print" },
            compiled.Commands.Select(command => command.Kind));
    }

    private sealed record Compiled(
        List<RuntimeCommand> Commands,
        List<RuntimeDiagnostic> Diagnostics,
        IskraObject Item,
        IskraObject Target,
        IskraObject Third);

    private static Compiled Compile(string source)
    {
        var store = new ProjectStore();
        var runtime = new ProjectRuntime(store, new ProjectContentStore(store));

        var scene = new IskraScene { Name = "Главная" };
        var item = new IskraObject { Name = "Фон" };
        var target = new IskraObject { Name = "монета" };
        var third = new IskraObject { Name = "красная шапка" };
        scene.Objects.Add(item);
        scene.Objects.Add(target);
        scene.Objects.Add(third);

        var project = new IskraProject { Name = "Тест Компилятор" };
        project.Scenes.Add(scene);
        project.Scenes.Add(new IskraScene { Name = "Вторая" });
        project.Scenes.Add(new IskraScene { Name = "Планета.Марс" });

        var method = typeof(ProjectRuntime).GetMethod("CompileScript", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Метод CompileScript не найден.");
        var diagnostics = new List<RuntimeDiagnostic>();
        var commands = (List<RuntimeCommand>)method.Invoke(runtime,
            [project, scene, item, source, scene.Objects.ToList(), diagnostics])!;
        return new Compiled(commands, diagnostics, item, target, third);
    }

    private static List<string> Errors(Compiled compiled) => compiled.Diagnostics
        .Where(item => item.Severity == RuntimeDiagnosticSeverity.Error)
        .Select(item => item.Message)
        .ToList();
}
