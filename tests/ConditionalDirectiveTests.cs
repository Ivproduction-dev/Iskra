using IskraStudio;
using Xunit;

namespace IskraStudio.Tests;

public sealed class ConditionalDirectiveTests
{
    [Theory]
    [InlineData("Если ready = истина то", "ready = истина")]
    [InlineData("ЕСЛЕ ready = true то", "ready = true")]
    [InlineData("Эсли ready = true то", "ready = true")]
    public void IfHeaderAcceptsRussianAliases(string source, string expectedCondition)
    {
        Assert.True(ConditionalDirectiveParser.TryParseIf(source, out var condition));
        Assert.Equal(expectedCondition, condition);
    }

    [Theory]
    [InlineData("Пока жизни > 0 то", "жизни > 0")]
    [InlineData("ПОКА готово = истина ТО", "готово = истина")]
    public void WhileHeaderParsesCondition(string source, string expectedCondition)
    {
        Assert.True(ConditionalDirectiveParser.TryParseWhile(source, out var condition));
        Assert.Equal(expectedCondition, condition);
    }

    [Fact]
    public void ForeverHeaderIsRecognizedAsACompleteLine()
    {
        Assert.True(ConditionalDirectiveParser.IsForever("Вечно"));
        Assert.False(ConditionalDirectiveParser.IsForever("Вечно печать 1"));
    }

    [Theory]
    [InlineData("Иначе если lives > 0 то", "lives > 0")]
    [InlineData("Иначеесли lives > 0 то", "lives > 0")]
    [InlineData("Иначеесле lives > 0 то", "lives > 0")]
    [InlineData("Иначеэсли lives > 0 то", "lives > 0")]
    public void ElseIfHeaderAcceptsAliasesAndSpacing(string source, string expectedCondition)
    {
        Assert.True(ConditionalDirectiveParser.TryParseElseIf(source, out var condition));
        Assert.Equal(expectedCondition, condition);
    }

    [Fact]
    public void DirectivesAreRecognizedOnlyAsCompleteLines()
    {
        Assert.True(ConditionalDirectiveParser.IsElse("Иначе"));
        Assert.True(ConditionalDirectiveParser.IsEnd("конец"));
        Assert.False(ConditionalDirectiveParser.IsElse("Иначеесли lives > 0 то"));
        Assert.False(ConditionalDirectiveParser.IsEnd("Конец если"));
    }

    [Theory]
    [InlineData("При. запуске")]
    [InlineData("при.запуске")]
    [InlineData("ПРИ . ЗАПУСКЕ")]
    public void StartEventAcceptsExpectedSpacingAndCase(string source)
    {
        Assert.True(ConditionalDirectiveParser.IsStartEvent(source));
    }

    [Theory]
    [InlineData("При. запуске", "start")]
    [InlineData("При. нажатии", "press")]
    [InlineData("При. отпускании", "release")]
    [InlineData("При. отпускани", "release")]
    [InlineData("При. наведении", "hover")]
    public void SupportedEventHeadersMapToRuntimeEvents(string source, string expectedKind)
    {
        Assert.True(ConditionalDirectiveParser.TryParseEvent(source, out var eventKind));
        Assert.Equal(expectedKind, eventKind);
        Assert.Equal(ScriptEventLineKind.Start,
            ConditionalDirectiveParser.ClassifyEventLine(source, eventStarted: true));
    }

    [Theory]
    [InlineData("При запуске")]
    [InlineData("При. запуск")]
    [InlineData("При. столкновении")]
    public void UnsupportedOrMalformedEventIsNotStartEvent(string source)
    {
        Assert.False(ConditionalDirectiveParser.IsStartEvent(source));
        Assert.True(ConditionalDirectiveParser.IsEventPrefix(source));
    }

    [Fact]
    public void CommandsRequireStartEventAndFollowingEventHeadersBeginNewBlocks()
    {
        Assert.Equal(ScriptEventLineKind.OutsideEvent,
            ConditionalDirectiveParser.ClassifyEventLine("Печать \"до события\"", eventStarted: false));
        Assert.Equal(ScriptEventLineKind.Start,
            ConditionalDirectiveParser.ClassifyEventLine("При. запуске", eventStarted: false));
        Assert.Equal(ScriptEventLineKind.InsideEvent,
            ConditionalDirectiveParser.ClassifyEventLine("Печать \"внутри\"", eventStarted: true));
        Assert.Equal(ScriptEventLineKind.Start,
            ConditionalDirectiveParser.ClassifyEventLine("При. запуске", eventStarted: true));
    }

    [Fact]
    public void EventBlocksKeepCommandRangesSeparated()
    {
        var commands = new List<RuntimeCommand>
        {
            new("event", "start", 1),
            new("print", "started", 2),
            new("event", "press", 3),
            new("print", "pressed", 4),
            new("event", "release", 5),
            new("print", "released", 6)
        };

        Assert.Equal(new[]
        {
            new ScriptEventBlock("start", 1, 2),
            new ScriptEventBlock("press", 3, 4),
            new ScriptEventBlock("release", 5, 6)
        }, ScriptEventBlockParser.Parse(commands));
    }

    [Theory]
    [InlineData("Эсли готово = истина то Печать \"готово\" конец", "Эсли готово = истина то", "Печать \"готово\"", "Конец")]
    [InlineData("Иначеесли жизни > 0 то Печать \"жив\"", "Иначеесли жизни > 0 то", "Печать \"жив\"")]
    [InlineData("Иначе Печать \"нет\" конец", "Иначе", "Печать \"нет\"", "Конец")]
    public void InlineConditionalCommandsExpandAndKeepOriginalLine(string source, params string[] expected)
    {
        var expanded = ConditionalDirectiveParser.ExpandInline(source).ToArray();

        Assert.Equal(expected, expanded.Select(line => line.Text));
        Assert.All(expanded, line => Assert.Equal(1, line.LineNumber));
    }

    [Theory]
    [InlineData("ready = истина", true)]
    [InlineData("ready = true", true)]
    [InlineData("ready != ложь", true)]
    [InlineData("ready = нет", false)]
    [InlineData("ready = false", false)]
    [InlineData("ready", true)]
    [InlineData("не ready", false)]
    [InlineData("нет ready", false)]
    [InlineData("lives > 0 и ready", true)]
    [InlineData("lives < 0 или ready", true)]
    [InlineData("(lives < 0 или ready) и не lives = 0", true)]
    [InlineData("ready или lives = 0 и lives > 0", true)]
    [InlineData("lives > 0", true)]
    [InlineData("lives <= 2", false)]
    [InlineData("name = \"Пилот\"", true)]
    public void ConditionsCompareBooleanNumericAndTextValues(string expression, bool expected)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ready"] = "истина",
            ["lives"] = "3",
            ["name"] = "Пилот"
        };

        Assert.Equal(expected, ConditionalDirectiveParser.Evaluate(expression, variables));
    }

    [Theory]
    [InlineData("ready = нет")]
    [InlineData("ready")]
    [InlineData("не ready")]
    [InlineData("нет ready")]
    [InlineData("ready и lives > 0")]
    [InlineData("ready или lives > 0")]
    [InlineData("(ready или lives > 0) и не ready = нет")]
    public void LogicalExpressionsAreAccepted(string expression) =>
        Assert.True(ConditionalDirectiveParser.IsValidCondition(expression));

    [Theory]
    [InlineData("ready = и")]
    [InlineData("ready или")]
    [InlineData("(ready")]
    [InlineData("ready and notReady")]
    public void MalformedLogicalExpressionsAreRejected(string expression) =>
        Assert.False(ConditionalDirectiveParser.IsValidCondition(expression));

    [Fact]
    public void WhenEventEndDoesNotBecomeAnExecutableCommand()
    {
        var commands = new[]
        {
            new RuntimeCommand("event", "when", 1, Condition: "ready = истина"),
            new RuntimeCommand("print", "start", 2),
            new RuntimeCommand("event-end", string.Empty, 3),
            new RuntimeCommand("event", "press", 4),
            new RuntimeCommand("print", "clicked", 5)
        };

        var blocks = ScriptEventBlockParser.Parse(commands);

        Assert.Equal((1, 2), (blocks[0].Start, blocks[0].End));
        Assert.Equal((4, 5), (blocks[1].Start, blocks[1].End));
    }

    [Fact]
    public void PressedAndHoveredPropertiesTrackInteractionState()
    {
        var properties = new RuntimeObjectProperties();

        Assert.False(ConditionalDirectiveParser.Evaluate("Свойство.нажат = истина", properties.Values));
        Assert.False(ConditionalDirectiveParser.Evaluate("Свойство.наведен = истина", properties.Values));

        Assert.True(properties.SetPressed(true));
        Assert.True(properties.SetHovered(true));
        Assert.True(ConditionalDirectiveParser.Evaluate("сВОЙСТВО.НАЖАТ = истина", properties.Values));
        Assert.True(ConditionalDirectiveParser.Evaluate("Свойство.наведен = истина", properties.Values));

        Assert.True(properties.SetPressed(false));
        Assert.True(properties.SetHovered(false));
        Assert.True(ConditionalDirectiveParser.Evaluate("Свойство.нажат = нет", properties.Values));
        Assert.True(ConditionalDirectiveParser.Evaluate("Свойство.наведен = ложь", properties.Values));
    }

    [Theory]
    [InlineData("ложь", "ложь")]
    [InlineData("нет", "ложь")]
    [InlineData("false", "ложь")]
    [InlineData("true", "истина")]
    [InlineData("не булево", "не булево")]
    public void BooleanAliasesUseRussianCanonicalValues(string value, string expected) =>
        Assert.Equal(expected, ConditionalDirectiveParser.NormalizeBooleanValue(value));

    [Fact]
    public void ElseIfChainCompilesToCorrectBranchTargets()
    {
        var commands = new List<RuntimeCommand>();
        var flow = new ScriptFlowBuilder(commands);

        Assert.True(flow.BeginIf("lives > 1", 1, out _));
        commands.Add(new RuntimeCommand("print", "many", 2));
        Assert.True(flow.ElseIf("lives = 1", 3, out _));
        commands.Add(new RuntimeCommand("print", "last", 4));
        Assert.True(flow.Else(5, out _));
        commands.Add(new RuntimeCommand("print", "none", 6));
        Assert.True(flow.End(out _, out _));

        Assert.Equal(("branch-false", 3), (commands[0].Kind, commands[0].Target));
        Assert.Equal(("jump", 7), (commands[2].Kind, commands[2].Target));
        Assert.Equal(("branch-false", 6), (commands[3].Kind, commands[3].Target));
        Assert.Equal(("jump", 7), (commands[5].Kind, commands[5].Target));
        Assert.Empty(flow.OpenBlockLines);
    }

    [Fact]
    public void NestedConditionsKeepIndependentBranchTargets()
    {
        var commands = new List<RuntimeCommand>();
        var flow = new ScriptFlowBuilder(commands);

        Assert.True(flow.BeginIf("lives > 0", 1, out _));
        Assert.True(flow.BeginIf("ready = true", 2, out _));
        commands.Add(new RuntimeCommand("print", "ready", 3));
        Assert.True(flow.End(out _, out _));
        commands.Add(new RuntimeCommand("print", "alive", 4));
        Assert.True(flow.Else(5, out _));
        commands.Add(new RuntimeCommand("print", "game-over", 6));
        Assert.True(flow.End(out _, out _));

        Assert.Equal(5, commands[0].Target);
        Assert.Equal(3, commands[1].Target);
        Assert.Equal(6, commands[4].Target);
        Assert.Empty(flow.OpenBlockLines);
    }

    [Fact]
    public void UnclosedConditionalReportsItsOpeningLine()
    {
        var flow = new ScriptFlowBuilder([]);
        Assert.True(flow.BeginIf("ready = true", 12, out _));
        Assert.Equal(new[] { 12 }, flow.OpenBlockLines);
    }

    [Fact]
    public void ElseWithoutIfAndDuplicateElseAreRejected()
    {
        var commands = new List<RuntimeCommand>();
        var flow = new ScriptFlowBuilder(commands);
        Assert.False(flow.Else(1, out _));

        Assert.True(flow.BeginIf("ready = true", 2, out _));
        Assert.True(flow.Else(3, out _));
        Assert.False(flow.Else(4, out _));
    }

    [Fact]
    public void WhileLoopBranchesBackToItsConditionAndExitsAfterEnd()
    {
        var commands = new List<RuntimeCommand>();
        var flow = new ScriptFlowBuilder(commands);

        Assert.True(flow.BeginWhile("жизни > 0", 1, out _));
        commands.Add(new RuntimeCommand("change-value", "жизни -1", 2));
        Assert.True(flow.End(out _, out _));

        Assert.Equal(("branch-false", 3), (commands[0].Kind, commands[0].Target));
        Assert.Equal(("loop-jump", 0), (commands[2].Kind, commands[2].Target));
        Assert.Empty(flow.OpenBlocks);
    }

    [Fact]
    public void NestedWhileAndIfEndInStackOrder()
    {
        var commands = new List<RuntimeCommand>();
        var flow = new ScriptFlowBuilder(commands);

        Assert.True(flow.BeginWhile("жизни > 0", 1, out _));
        Assert.True(flow.BeginIf("готово = истина", 2, out _));
        commands.Add(new RuntimeCommand("print", "готово", 3));
        Assert.True(flow.End(out _, out _));
        commands.Add(new RuntimeCommand("change-value", "жизни -1", 4));
        Assert.True(flow.End(out _, out _));

        Assert.Equal(5, commands[0].Target);
        Assert.Equal(3, commands[1].Target);
        Assert.Equal(("loop-jump", 0), (commands[4].Kind, commands[4].Target));
    }

    [Fact]
    public void ForeverLoopJumpsBackToItsFirstCommand()
    {
        var commands = new List<RuntimeCommand>();
        var flow = new ScriptFlowBuilder(commands);

        flow.BeginForever(5);
        commands.Add(new RuntimeCommand("wait", "10", 6));
        Assert.True(flow.End(out _, out _));

        Assert.Equal(("loop-jump", 0), (commands[1].Kind, commands[1].Target));
    }

    [Fact]
    public void BreakExitsNearestLoopFromInsideAnIf()
    {
        var commands = new List<RuntimeCommand>();
        var flow = new ScriptFlowBuilder(commands);

        Assert.True(flow.BeginWhile("outer > 0", 1, out _));
        flow.BeginForever(2);
        Assert.True(flow.BeginIf("ready = true", 3, out _));
        Assert.True(flow.Break(4, out _));
        Assert.True(flow.End(out _, out _));
        Assert.True(flow.End(out _, out _));
        commands.Add(new RuntimeCommand("print", "after inner loop", 5));
        Assert.True(flow.End(out _, out _));

        Assert.Equal(("jump", 4), (commands[2].Kind, commands[2].Target));
        Assert.Equal(("loop-jump", 1), (commands[3].Kind, commands[3].Target));
        Assert.Equal("print", commands[4].Kind);
    }

    [Fact]
    public void BreakOutsideLoopIsRejected()
    {
        var flow = new ScriptFlowBuilder([]);

        Assert.False(flow.Break(7, out var error));
        Assert.Contains("только внутри цикла", error, StringComparison.OrdinalIgnoreCase);
    }
}
