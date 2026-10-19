using IskraStudio;
using Xunit;

namespace IskraStudio.Tests;

public sealed class ScriptEventAndPropertyTests
{
    [Theory]
    [InlineData("При. запуске", "start", null)]
    [InlineData("при . запуске", "start", null)]
    [InlineData("При. нажатии", "press", null)]
    [InlineData("При. отпускании", "release", null)]
    [InlineData("При. отпускани", "release", null)]
    [InlineData("При. наведении", "hover", null)]
    [InlineData("при.НАВЕДЕНИИ", "hover", null)]
    public void OldEventHeadersParse(string source, string expectedKind, string? expectedArgument)
    {
        Assert.True(ConditionalDirectiveParser.TryParseEventDetail(source, out var kind, out var argument));
        Assert.Equal(expectedKind, kind);
        Assert.Equal(expectedArgument, argument);
    }

    [Theory]
    [InlineData("При. нажатии клавиши.ф", "key", "a")]
    [InlineData("При. нажатии клавиша.ф", "key", "a")]
    [InlineData("При. нажатии клавиш.ф", "key", "a")]
    [InlineData("при.нажатии клавиши.ПРОБЕЛ", "key", "пробел")]
    [InlineData("При. нажатии клавиши.5", "key", "5")]
    [InlineData("При. нажатии клавиши.ввод.", "key", "ввод")]
    [InlineData("При. нажатии клавиши.Ё", "key", "`")]
    [InlineData("При . нажатии клавиши . 5", "key", "5")]
    [InlineData("Каждые.2.секунды", "every", "2")]
    [InlineData("каждые . 5 . секунду", "every", "5")]
    [InlineData("Каждый кадр", "frame", null)]
    [InlineData("Каждый.кадр", "frame", null)]
    public void NewEventHeadersParse(string source, string expectedKind, string? expectedArgument)
    {
        Assert.True(ConditionalDirectiveParser.TryParseEventDetail(source, out var kind, out var argument));
        Assert.Equal(expectedKind, kind);
        Assert.Equal(expectedArgument, argument);
    }

    [Theory]
    [InlineData("Каждые.1.секунда", "1")]
    [InlineData("Каждые.3.секунды", "3")]
    [InlineData("Каждые.5.секунду", "5")]
    [InlineData("Каждые.10.секунд", "10")]
    [InlineData("Каждые.3.секунде", "3")]
    [InlineData("Каждые.4.секундой", "4")]
    [InlineData("Каждые.6.секундами", "6")]
    [InlineData("Каждые.7.секундах", "7")]
    [InlineData("каждые . 2 . СЕКУНДЫ", "2")]
    [InlineData("Каждые.01.секунды", "1")]
    [InlineData("Каждые.2147483.секунды", "2147483")]
    public void EveryEventWordsAndNumbersParse(string source, string expectedSeconds)
    {
        Assert.True(ConditionalDirectiveParser.TryParseEventDetail(source, out var kind, out var argument));
        Assert.Equal("every", kind);
        Assert.Equal(expectedSeconds, argument);
    }

    [Theory]
    [InlineData("При. нажатии клавиши.прыжок")]
    [InlineData("При. нажатии клавиши")]
    [InlineData("При. нажатии клавиша")]
    [InlineData("При. нажатии клавиш.прыжок")]
    [InlineData("Каждые.0.секунды")]
    [InlineData("Каждые.2147484.секунды")]
    [InlineData("Каждые.2.5.секунды")]
    [InlineData("Каждые.2")]
    [InlineData("Каждые.2.минуты")]
    [InlineData("Каждые 2 секунды")]
    [InlineData("Каждые.2.секунды.")]
    [InlineData("Каждые..2.секунды")]
    [InlineData("Каждый кадрик")]
    [InlineData("Каждый кадр.")]
    [InlineData("При. отпускании клавиши.ф")]
    public void MalformedNewEventsDoNotParse(string source)
    {
        Assert.False(ConditionalDirectiveParser.TryParseEventDetail(source, out _, out _));
        Assert.True(ConditionalDirectiveParser.IsEventPrefix(source));
    }

    [Theory]
    [InlineData("не каждый кадр")]
    [InlineData("КаждыйКадр")]
    [InlineData("Каждое срабатывание")]
    public void NonEventLinesAreNotEventPrefixes(string source) =>
        Assert.False(ConditionalDirectiveParser.IsEventPrefix(source));

    [Theory]
    [InlineData("При. запуске", true)]
    [InlineData("При. нажатии клавиши.ф", false)]
    [InlineData("Каждые.2.секунды", false)]
    [InlineData("Каждый кадр", false)]
    public void OnlyStartEventsAreStartEvents(string source, bool expectedStart) =>
        Assert.Equal(expectedStart, ConditionalDirectiveParser.IsStartEvent(source));

    [Theory]
    [InlineData("Когда. счёт = 5", "счёт = 5")]
    [InlineData("Когда счёт > 5", "счёт > 5")]
    [InlineData("Как.Только готово = истина", "готово = истина")]
    [InlineData("Как.Только.готово = истина", "готово = истина")]
    [InlineData("когда. счёт = 5 то", "счёт = 5")]
    public void ConditionEventsParse(string source, string expectedCondition)
    {
        Assert.True(ConditionalDirectiveParser.TryParseConditionEvent(source, out var condition));
        Assert.Equal(expectedCondition, condition);
    }

    [Theory]
    [InlineData("Когда")]
    [InlineData("Когда.")]
    [InlineData("Как.Только")]
    public void MalformedConditionEventsDoNotParse(string source) =>
        Assert.False(ConditionalDirectiveParser.TryParseConditionEvent(source, out _));

    [Theory]
    [InlineData("При. запуске", false, "Start")]
    [InlineData("При. нажатии клавиши.ф", false, "Start")]
    [InlineData("Каждые.2.секунды", false, "Start")]
    [InlineData("Каждый кадр", false, "Start")]
    [InlineData("Как.Только счёт = 5", false, "Start")]
    [InlineData("Когда. счёт = 5", false, "Start")]
    [InlineData("Каждые.2", false, "Unsupported")]
    [InlineData("При. отпускании клавиши.ф", true, "Unsupported")]
    [InlineData("Когда.", true, "Unsupported")]
    [InlineData("Печать 1", false, "OutsideEvent")]
    [InlineData("Конец", false, "OutsideEvent")]
    [InlineData("Печать 1", true, "InsideEvent")]
    [InlineData("Конец", true, "InsideEvent")]
    [InlineData("Прыгнуть", true, "InsideEvent")]
    public void EventLinesAreClassified(string source, bool eventStarted, string expectedKind) =>
        Assert.Equal(expectedKind, ConditionalDirectiveParser.ClassifyEventLine(source, eventStarted).ToString());

    [Fact]
    public void PressedKeyResolvesToTrue()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeObjectProperties.KeyboardKeyPropertyPrefix + "a"] = "истина"
        };

        Assert.Equal("истина", ProjectRuntime.ResolveValue("Свойство.клавиша.ф", variables));
        Assert.Equal("истина", ProjectRuntime.ResolveValue("свойство.КЛАВИША.Ф", variables));
        Assert.Equal("ложь", ProjectRuntime.ResolveValue("Свойство.клавиша.б", variables));
    }

    [Fact]
    public void SpecialKeyStateResolves()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeObjectProperties.KeyboardKeyPropertyPrefix + "пробел"] = "истина",
            [RuntimeObjectProperties.KeyboardKeyPropertyPrefix + "`"] = "ложь"
        };

        Assert.Equal("истина", ProjectRuntime.ResolveValue("Свойство.клавиша.пробел", variables));
        Assert.Equal("истина", ProjectRuntime.ResolveValue("Свойство.клавиша.ПРОБЕЛ", variables));
        Assert.Equal("ложь", ProjectRuntime.ResolveValue("Свойство.клавиша.ё", variables));
        Assert.Equal("ложь", ProjectRuntime.ResolveValue("Свойство.клавиша.прыжок", variables));
    }

    [Fact]
    public void MissingKeyReadsAsFalseInConditions() =>
        Assert.False(ConditionalDirectiveParser.Evaluate("Свойство.клавиша.пробел",
            new Dictionary<string, string>(StringComparer.Ordinal)));

    [Fact]
    public void KeyComparisonWithFalseWorks() =>
        Assert.True(ConditionalDirectiveParser.Evaluate("Свойство.клавиша.пробел = ложь",
            new Dictionary<string, string>(StringComparer.Ordinal)));

    [Fact]
    public void NegationOfMissingKeyIsTrue() =>
        Assert.True(ConditionalDirectiveParser.Evaluate("не Свойство.клавиша.пробел",
            new Dictionary<string, string>(StringComparer.Ordinal)));

    [Fact]
    public void EitherPressedKeyWins()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeObjectProperties.KeyboardKeyPropertyPrefix + "право"] = "истина"
        };

        Assert.True(ConditionalDirectiveParser.Evaluate("Свойство.клавиша.ф или Свойство.клавиша.право", variables));
    }

    [Fact]
    public void TouchedObjectPropertyIsCaseInsensitive()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Свойство.коснулся.монстр"] = "истина"
        };

        Assert.True(ConditionalDirectiveParser.Evaluate("свойство.КОСНУЛСЯ.МОНСТР", variables));
    }

    [Fact]
    public void MissingTouchedStateReadsAsFalse() =>
        Assert.False(ConditionalDirectiveParser.Evaluate("Свойство.коснулся.монстр",
            new Dictionary<string, string>(StringComparer.Ordinal)));

    [Fact]
    public void TouchedNameWithSpacesGluesIntoOneToken()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Свойство.коснулся.красная шапка"] = "истина"
        };

        Assert.True(ConditionalDirectiveParser.Evaluate("Свойство.коснулся.красная шапка = истина", variables));
    }

    [Fact]
    public void TouchedNameInsideParenthesesGlues()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Свойство.коснулся.красная шапка"] = "истина"
        };

        Assert.True(ConditionalDirectiveParser.Evaluate("(Свойство.коснулся.красная шапка)", variables));
    }

    [Fact]
    public void TouchedStateWithFalseValueIsFalse()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Свойство.коснулся.монстр"] = "ложь"
        };

        Assert.False(ConditionalDirectiveParser.Evaluate("Свойство.коснулся.монстр", variables));
    }

    [Fact]
    public void GluingStopsAtLogicalOperator()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Свойство.коснулся.монстр"] = "истина",
            ["Свойство.клавиша.a"] = "истина"
        };

        Assert.True(ConditionalDirectiveParser.Evaluate("Свойство.коснулся.монстр и Свойство.клавиша.ф", variables));
    }

    [Fact]
    public void GluingStopsAtComparison()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Свойство.коснулся.красная шапка"] = "истина"
        };

        Assert.False(ConditionalDirectiveParser.Evaluate("Свойство.коснулся.красная шапка = ложь", variables));
    }

    [Fact]
    public void CombinedKeyboardAndTouchCondition()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Свойство.коснулся.монета"] = "истина",
            [RuntimeObjectProperties.KeyboardKeyPropertyPrefix + "пробел"] = "истина"
        };

        Assert.True(ConditionalDirectiveParser.Evaluate(
            "Свойство.коснулся.монета = истина и Свойство.клавиша.пробел = истина", variables));
    }

    [Fact]
    public void NothingToExtractFromPlainCondition()
    {
        var (touched, keys) = ConditionalDirectiveParser.ExtractPropertyReferences("счёт + 5 > 3");
        Assert.Empty(touched);
        Assert.Empty(keys);
    }

    [Fact]
    public void MultipleReferencesAreExtracted()
    {
        var (touched, keys) = ConditionalDirectiveParser.ExtractPropertyReferences(
            "Свойство.коснулся.монета = истина или Свойство.коснулся.красная шапка = ложь и Свойство.клавиша.а = истина");

        Assert.Equal(new[] { "монета", "красная шапка" }, touched);
        Assert.Equal(new[] { "а" }, keys);
    }

    [Fact]
    public void KeyboardKeyTokenIsReportedAsWritten()
    {
        var (_, keys) = ConditionalDirectiveParser.ExtractPropertyReferences("Свойство.КЛАВИША.ПРОБЕЛ");
        Assert.Equal(new[] { "ПРОБЕЛ" }, keys);
    }

    [Fact]
    public void EmptyPropertyNamesAreSkipped()
    {
        var (touched, keys) = ConditionalDirectiveParser.ExtractPropertyReferences(
            "Свойство.коснулся. и Свойство.клавиша.");
        Assert.Empty(touched);
        Assert.Empty(keys);
    }

    [Fact]
    public void PropertyReferencesAreExtractedForDiagnostics()
    {
        var (touched, keys) = ConditionalDirectiveParser.ExtractPropertyReferences(
            "Свойство.коснулся.красная шапка и Свойство.клавиша.пробел");

        Assert.Equal(new[] { "красная шапка" }, touched);
        Assert.Equal(new[] { "пробел" }, keys);
    }

    [Fact]
    public void EventBlocksSupportNewKinds()
    {
        var commands = new List<RuntimeCommand>
        {
            new("event", "key", 1, Condition: "a"),
            new("print", "нажал", 2),
            new("event-end", string.Empty, 3),
            new("event", "every", 4, Condition: "2"),
            new("print", "тик", 5),
            new("event-end", string.Empty, 6)
        };

        var blocks = ScriptEventBlockParser.Parse(commands);

        Assert.Equal(new[] { "key", "every" }, blocks.Select(block => block.EventKind));
        Assert.Equal("a", blocks[0].Condition);
        Assert.Equal("2", blocks[1].Condition);
    }

    [Fact]
    public void EventBlocksWithoutEndRunToTheNextEvent()
    {
        var commands = new List<RuntimeCommand>
        {
            new("event", "start", 1),
            new("print", "раз", 2),
            new("event", "key", 3, Condition: "a"),
            new("print", "два", 4)
        };

        var blocks = ScriptEventBlockParser.Parse(commands);

        Assert.Equal(2, blocks.Count);
        Assert.Equal("start", blocks[0].EventKind);
        Assert.Equal("key", blocks[1].EventKind);
    }

    [Fact]
    public void EventBlocksOfEmptyScriptAreEmpty() =>
        Assert.Empty(ScriptEventBlockParser.Parse([]));
}
