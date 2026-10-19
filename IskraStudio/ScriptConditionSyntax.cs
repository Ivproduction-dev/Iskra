using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IskraStudio;

internal enum ConditionalDirectiveKind
{
    If,
    ElseIf,
    Else,
    End
}

internal readonly record struct ConditionalSourceLine(string Text, int LineNumber);

internal enum ScriptEventLineKind
{
    Start,
    Unsupported,
    OutsideEvent,
    InsideEvent
}

internal static class ConditionalDirectiveParser
{
    private static readonly Regex IfHeader = new(
        "^(?:если|есле|эсли)\\s+(?<condition>.+?)\\s+то$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ElseIfHeader = new(
        "^иначе\\s*(?:если|есле|эсли)\\s+(?<condition>.+?)\\s+то$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex IfPrefix = new(
        "^(?:если|есле|эсли)(?:\\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WhileHeader = new(
        "^пока\\s+(?<condition>.+?)\\s+то$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WhilePrefix = new(
        "^пока(?:\\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex InlineBlock = new(
        "^(?<header>(?:(?:если|есле|эсли)\\s+.+?\\s+то|иначе\\s*(?:если|есле|эсли)\\s+.+?\\s+то|иначе))\\s+(?<body>.+?)(?:\\s+(?<end>конец))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex StartEvent = new(
        "^при\\s*\\.\\s*запуске$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PressEvent = new(
        "^при\\s*\\.\\s*нажатии$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ReleaseEvent = new(
        "^при\\s*\\.\\s*отпускани(?:и)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HoverEvent = new(
        "^при\\s*\\.\\s*наведении$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ConditionEvent = new(
        "^(?:когда|как\\s*\\.\\s*только)(?:\\s*\\.\\s*|\\s+)(?<condition>.+?)(?:\\s+то)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IEnumerable<ConditionalSourceLine> ExpandInline(string source)
    {
        var sourceLines = source.Split('\n');
        for (var index = 0; index < sourceLines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = sourceLines[index].Trim();
            var match = InlineBlock.Match(line);
            if (!match.Success)
            {
                yield return new ConditionalSourceLine(line, lineNumber);
                continue;
            }

            yield return new ConditionalSourceLine(match.Groups["header"].Value, lineNumber);
            yield return new ConditionalSourceLine(match.Groups["body"].Value, lineNumber);
            if (match.Groups["end"].Success)
            {
                yield return new ConditionalSourceLine("Конец", lineNumber);
            }
        }
    }

    public static bool IsIfPrefix(string line) => IfPrefix.IsMatch(line);
    public static bool IsWhilePrefix(string line) => WhilePrefix.IsMatch(line);
    public static bool IsForever(string line) => line.Equals("вечно", StringComparison.OrdinalIgnoreCase);
    public static bool IsBreak(string line) => line.Equals("прервать", StringComparison.OrdinalIgnoreCase);
    public static bool IsStartEvent(string line) => TryParseEvent(line, out var eventKind) && eventKind == "start";
    public static bool TryParseEvent(string line, out string eventKind) => TryParseEventDetail(line, out eventKind, out _);

    public static bool TryParseEventDetail(string line, out string eventKind, out string? eventArgument)
    {
        eventArgument = null;
        if (StartEvent.IsMatch(line)) eventKind = "start";
        else if (PressEvent.IsMatch(line)) eventKind = "press";
        else if (ReleaseEvent.IsMatch(line)) eventKind = "release";
        else if (HoverEvent.IsMatch(line)) eventKind = "hover";
        else if (ScriptCommandSyntax.TryParseKeyboardEvent(line, out _, out var keyId) && keyId.Length > 0)
        {
            eventKind = "key";
            eventArgument = keyId;
        }
        else if (ScriptCommandSyntax.TryParseEveryEvent(line, out var seconds))
        {
            eventKind = "every";
            eventArgument = seconds;
        }
        else if (ScriptCommandSyntax.IsFrameEvent(line)) eventKind = "frame";
        else
        {
            eventKind = string.Empty;
            return false;
        }
        return true;
    }
    public static bool TryParseConditionEvent(string line, out string condition)
    {
        var match = ConditionEvent.Match(line);
        condition = match.Success ? match.Groups["condition"].Value.Trim() : string.Empty;
        return match.Success;
    }

    public static bool IsEventPrefix(string line) =>
        Regex.IsMatch(line, "^(?:при|когда)(?:\\s|\\.|$)|^как\\s*\\.|^каждые(?:\\s|\\.|$)|^каждый(?:\\s|\\.|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static ScriptEventLineKind ClassifyEventLine(string line, bool eventStarted) =>
        TryParseEvent(line, out _) || TryParseConditionEvent(line, out _) ? ScriptEventLineKind.Start :
        IsEventPrefix(line) ? ScriptEventLineKind.Unsupported :
        eventStarted ? ScriptEventLineKind.InsideEvent : ScriptEventLineKind.OutsideEvent;

    public static bool TryParseIf(string line, out string condition)
    {
        var match = IfHeader.Match(line);
        condition = match.Success ? match.Groups["condition"].Value.Trim() : string.Empty;
        return match.Success;
    }

    public static bool TryParseWhile(string line, out string condition)
    {
        var match = WhileHeader.Match(line);
        condition = match.Success ? match.Groups["condition"].Value.Trim() : string.Empty;
        return match.Success;
    }

    public static bool TryParseElseIf(string line, out string condition)
    {
        var match = ElseIfHeader.Match(line);
        condition = match.Success ? match.Groups["condition"].Value.Trim() : string.Empty;
        return match.Success;
    }

    public static bool IsElse(string line) => line.Equals("иначе", StringComparison.OrdinalIgnoreCase);
    public static bool IsElsePrefix(string line) => line.StartsWith("иначе", StringComparison.OrdinalIgnoreCase);
    public static bool IsEnd(string line) => line.Equals("конец", StringComparison.OrdinalIgnoreCase);

    public static bool IsValidCondition(string expression)
    {
        try
        {
            _ = new ConditionExpressionParser(expression).Parse();
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool Evaluate(string expression, IReadOnlyDictionary<string, string> variables)
    {
        try
        {
            return new ConditionExpressionParser(expression).Parse().Evaluate(variables);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"Некорректное условие: {expression}", exception);
        }
    }

    internal static (List<string> TouchedObjectNames, List<string> KeyboardKeys) ExtractPropertyReferences(string expression)
    {
        var touched = new List<string>();
        var keys = new List<string>();
        foreach (var token in ConditionExpressionParser.Tokenize(expression))
        {
            if (token.StartsWith(RuntimeObjectProperties.TouchedObjectPropertyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var objectName = token[RuntimeObjectProperties.TouchedObjectPropertyPrefix.Length..].Trim();
                if (objectName.Length > 0) touched.Add(objectName);
            }
            else if (token.StartsWith(RuntimeObjectProperties.KeyboardKeyPropertyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var key = token[RuntimeObjectProperties.KeyboardKeyPropertyPrefix.Length..].Trim();
                if (key.Length > 0) keys.Add(key);
            }
        }
        return (touched, keys);
    }

    private static string ResolveOperand(string value, IReadOnlyDictionary<string, string> variables)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') return value[1..^1];
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return value;
        return ResolveVariable(value, variables);
    }

    internal static string ResolveVariable(string name, IReadOnlyDictionary<string, string> variables)
    {
        if (RandomValueSyntax.TryParse(name, out var minimum, out var maximum))
            return RandomValueSyntax.Generate(minimum, maximum);

        if (name.StartsWith(RuntimeObjectProperties.KeyboardKeyPropertyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var keyToken = name[RuntimeObjectProperties.KeyboardKeyPropertyPrefix.Length..].Trim();
            return KeyboardKeySyntax.TryNormalize(keyToken, out var keyId) &&
                variables.TryGetValue(RuntimeObjectProperties.KeyboardKeyPropertyPrefix + keyId, out var keyState)
                ? NormalizeBooleanValue(keyState)
                : "ложь";
        }

        if (name.StartsWith(RuntimeObjectProperties.TouchedObjectPropertyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var objectName = name[RuntimeObjectProperties.TouchedObjectPropertyPrefix.Length..].Trim();
            var touchedKey = RuntimeObjectProperties.TouchedObjectPropertyPrefix + objectName.ToLowerInvariant();
            return variables.TryGetValue(touchedKey, out var touchedState)
                ? NormalizeBooleanValue(touchedState)
                : "ложь";
        }

        var key = name.Equals(RuntimeObjectProperties.PressedProperty, StringComparison.OrdinalIgnoreCase)
            ? RuntimeObjectProperties.PressedProperty
            : name.Equals(RuntimeObjectProperties.HoveredProperty, StringComparison.OrdinalIgnoreCase)
                ? RuntimeObjectProperties.HoveredProperty
                : name.Equals(RuntimeObjectProperties.XProperty, StringComparison.OrdinalIgnoreCase)
                    ? RuntimeObjectProperties.XProperty
                    : name.Equals(RuntimeObjectProperties.YProperty, StringComparison.OrdinalIgnoreCase)
                        ? RuntimeObjectProperties.YProperty
                        : name.Equals(RuntimeObjectProperties.SizeProperty, StringComparison.OrdinalIgnoreCase)
                            ? RuntimeObjectProperties.SizeProperty
                            : name.Equals(RuntimeObjectProperties.TransparencyProperty, StringComparison.OrdinalIgnoreCase)
                                ? RuntimeObjectProperties.TransparencyProperty
                                : name;
        return NormalizeBooleanValue(variables.GetValueOrDefault(key, variables.GetValueOrDefault(name, name)));
    }

    internal static string NormalizeBooleanValue(string value) =>
        TryParseBoolean(value, out var boolean) ? boolean ? "истина" : "ложь" : value;

    private static int Compare(string left, string right)
    {
        if (TryParseBoolean(left, out var leftBoolean) && TryParseBoolean(right, out var rightBoolean))
            return leftBoolean.CompareTo(rightBoolean);
        if (long.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var leftNumber) &&
            long.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rightNumber))
            return leftNumber.CompareTo(rightNumber);
        if (double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftDecimal) &&
            double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightDecimal))
            return leftDecimal.CompareTo(rightDecimal);
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseBoolean(string value, out bool result)
    {
        if (value.Equals("истина", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }
        if (value.Equals("ложь", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("нет", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }
        result = false;
        return false;
    }

    private sealed class ConditionExpressionParser
    {
        private readonly List<string> tokens;
        private int position;

        public ConditionExpressionParser(string expression) => tokens = Tokenize(expression);

        public ConditionNode Parse()
        {
            if (tokens.Count == 0) throw new FormatException("Пустое условие.");
            var result = ParseOr();
            if (position != tokens.Count) throw new FormatException("Лишняя часть условия.");
            return result;
        }

        private ConditionNode ParseOr()
        {
            var left = ParseAnd();
            while (TakeWord("или")) left = new LogicalNode("или", left, ParseAnd());
            return left;
        }

        private ConditionNode ParseAnd()
        {
            var left = ParseNot();
            while (TakeWord("и")) left = new LogicalNode("и", left, ParseNot());
            return left;
        }

        private ConditionNode ParseNot()
        {
            if (TakeWord("не")) return new NotNode(ParseNot());
            if (IsWord("нет") && position + 1 < tokens.Count && CanStartOperand(tokens[position + 1]))
            {
                position++;
                return new NotNode(ParseNot());
            }
            return ParseComparison();
        }

        private ConditionNode ParseComparison()
        {
            var left = ParseOperand();
            if (position < tokens.Count && IsComparison(tokens[position]))
            {
                var operation = tokens[position++];
                return new ComparisonNode(left, operation, ParseOperand());
            }
            return new BooleanNode(left);
        }

        private ConditionNode ParseOperand()
        {
            if (position >= tokens.Count) throw new FormatException("Не хватает значения.");
            if (tokens[position] == "(")
            {
                position++;
                var nested = ParseOr();
                if (position >= tokens.Count || tokens[position] != ")") throw new FormatException("Не закрыта скобка.");
                position++;
                return new GroupNode(nested);
            }
            var token = tokens[position++];
            if (token is ")" or "=" or "==" or "!=" or "<>" or ">" or "<" or ">=" or "<=" ||
                string.Equals(token, "и", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "или", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "не", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Ожидалось значение.");
            return new ValueNode(token);
        }

        private bool TakeWord(string word)
        {
            if (!IsWord(word)) return false;
            position++;
            return true;
        }

        private bool IsWord(string word) => position < tokens.Count &&
            string.Equals(tokens[position], word, StringComparison.OrdinalIgnoreCase);

        private static bool CanStartOperand(string token) => token is not ("и" or "или" or ")" or "=" or "==" or "!=" or "<>" or ">" or "<" or ">=" or "<=");
        private static bool IsComparison(string token) => token is "=" or "==" or "!=" or "<>" or ">" or "<" or ">=" or "<=";

        internal static List<string> Tokenize(string expression)
        {
            var result = new List<string>();
            for (var index = 0; index < expression.Length;)
            {
                if (char.IsWhiteSpace(expression[index])) { index++; continue; }
                if (expression[index] == '"')
                {
                    var end = expression.IndexOf('"', index + 1);
                    if (end < 0) throw new FormatException("Не закрыта строка.");
                    result.Add(expression[index..(end + 1)]);
                    index = end + 1;
                    continue;
                }
                if (expression[index] is '(' or ')')
                {
                    result.Add(expression[index++].ToString());
                    continue;
                }
                if (IsComparisonCharacter(expression[index]))
                {
                    var end = index + 1;
                    if (end < expression.Length && expression[end] == '=') end++;
                    else if (expression[index] == '<' && end < expression.Length && expression[end] == '>') end++;
                    result.Add(expression[index..end]);
                    index = end;
                    continue;
                }
                var start = index++;
                while (index < expression.Length && !char.IsWhiteSpace(expression[index]) &&
                    expression[index] is not ('(' or ')' or '"') && !IsComparisonCharacter(expression[index])) index++;
                result.Add(expression[start..index]);
            }
            return GlueTouchedObjectNames(result);
        }

        private static List<string> GlueTouchedObjectNames(List<string> tokens)
        {
            var glued = new List<string>(tokens.Count);
            for (var index = 0; index < tokens.Count; index++)
            {
                var token = tokens[index];
                if (!token.StartsWith(RuntimeObjectProperties.TouchedObjectPropertyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    glued.Add(token);
                    continue;
                }

                var builder = new StringBuilder(token);
                while (index + 1 < tokens.Count && CanBePartOfObjectName(tokens[index + 1]))
                {
                    builder.Append(' ').Append(tokens[++index]);
                }
                glued.Add(builder.ToString());
            }
            return glued;
        }

        private static bool CanBePartOfObjectName(string token) =>
            token.Length > 0 && token[0] != '"' && !IsComparison(token) && token is not ("(" or ")") &&
            !token.Equals("и", StringComparison.OrdinalIgnoreCase) &&
            !token.Equals("или", StringComparison.OrdinalIgnoreCase) &&
            !token.Equals("не", StringComparison.OrdinalIgnoreCase);

        private static bool IsComparisonCharacter(char value) => value is '=' or '!' or '>' or '<';
    }

    private abstract record ConditionNode
    {
        public abstract bool Evaluate(IReadOnlyDictionary<string, string> variables);
    }

    private sealed record ValueNode(string Value) : ConditionNode
    {
        public override bool Evaluate(IReadOnlyDictionary<string, string> variables) =>
            TryParseBoolean(ResolveOperand(Value, variables), out var result)
                ? result
                : throw new InvalidOperationException($"Значение «{Value}» не является логическим.");

        public string Resolve(IReadOnlyDictionary<string, string> variables) => ResolveOperand(Value, variables);
    }

    private sealed record BooleanNode(ConditionNode Operand) : ConditionNode
    {
        public override bool Evaluate(IReadOnlyDictionary<string, string> variables) => Operand.Evaluate(variables);
    }

    private sealed record GroupNode(ConditionNode Inner) : ConditionNode
    {
        public override bool Evaluate(IReadOnlyDictionary<string, string> variables) => Inner.Evaluate(variables);
    }

    private sealed record NotNode(ConditionNode Inner) : ConditionNode
    {
        public override bool Evaluate(IReadOnlyDictionary<string, string> variables) => !Inner.Evaluate(variables);
    }

    private sealed record LogicalNode(string Operation, ConditionNode Left, ConditionNode Right) : ConditionNode
    {
        public override bool Evaluate(IReadOnlyDictionary<string, string> variables) => Operation == "и"
            ? Left.Evaluate(variables) && Right.Evaluate(variables)
            : Left.Evaluate(variables) || Right.Evaluate(variables);
    }

    private sealed record ComparisonNode(ConditionNode Left, string Operation, ConditionNode Right) : ConditionNode
    {
        public override bool Evaluate(IReadOnlyDictionary<string, string> variables)
        {
            var left = ResolveValue(Left, variables);
            var right = ResolveValue(Right, variables);
            var comparison = Compare(left, right);
            return Operation switch
            {
                "=" or "==" => comparison == 0,
                "!=" or "<>" => comparison != 0,
                ">" => comparison > 0,
                "<" => comparison < 0,
                ">=" => comparison >= 0,
                "<=" => comparison <= 0,
                _ => false
            };
        }

        private static string ResolveValue(ConditionNode node, IReadOnlyDictionary<string, string> variables) => node switch
        {
            ValueNode value => value.Resolve(variables),
            _ => node.Evaluate(variables) ? "истина" : "ложь"
        };
    }
}

internal static class RandomValueSyntax
{
    private const string Prefix = "Случайное.значение.от.";
    private const string UpperBoundSeparator = ".до.";

    public static bool TryParse(string expression, out double minimum, out double maximum)
    {
        minimum = 0;
        maximum = 0;
        if (!expression.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var bounds = expression[Prefix.Length..];
        var separatorIndex = bounds.IndexOf(UpperBoundSeparator, StringComparison.OrdinalIgnoreCase);
        if (separatorIndex <= 0 || separatorIndex + UpperBoundSeparator.Length >= bounds.Length) return false;

        return double.TryParse(bounds[..separatorIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out minimum) &&
            double.TryParse(bounds[(separatorIndex + UpperBoundSeparator.Length)..], NumberStyles.Float, CultureInfo.InvariantCulture, out maximum) &&
            double.IsFinite(minimum) && double.IsFinite(maximum) && minimum <= maximum;
    }

    public static string Generate(double minimum, double maximum)
    {
        if (minimum == maximum) return minimum.ToString("R", CultureInfo.InvariantCulture);

        if (minimum >= long.MinValue && maximum < (double)long.MaxValue &&
            Math.Truncate(minimum) == minimum && Math.Truncate(maximum) == maximum)
        {
            return Random.Shared.NextInt64((long)minimum, (long)maximum + 1).ToString(CultureInfo.InvariantCulture);
        }

        const long fractionSteps = 1L << 53;
        var fraction = Random.Shared.NextInt64(0, fractionSteps + 1) / (double)fractionSteps;
        var value = Math.Clamp(minimum * (1d - fraction) + maximum * fraction, minimum, maximum);
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}

internal readonly record struct ScriptEventBlock(string EventKind, int Start, int End, string? Condition = null);
internal readonly record struct OpenScriptBlock(int Line, string Name);

internal sealed class RuntimeObjectProperties
{
    public const string PressedProperty = "Свойство.нажат";
    public const string HoveredProperty = "Свойство.наведен";
    public const string XProperty = "Свойство.х";
    public const string YProperty = "Свойство.у";
    public const string SizeProperty = "Свойство.размер";
    public const string TransparencyProperty = "Свойство.прозрачность";
    public const string TouchedObjectPropertyPrefix = "Свойство.коснулся.";
    public const string KeyboardKeyPropertyPrefix = "Свойство.клавиша.";

    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal)
    {
        [PressedProperty] = "ложь",
        [HoveredProperty] = "ложь",
        [XProperty] = "0",
        [YProperty] = "0",
        [SizeProperty] = "100",
        [TransparencyProperty] = "0"
    };

    public IReadOnlyDictionary<string, string> Values => values;

    public bool SetPressed(bool value) => Set(PressedProperty, value);
    public bool SetHovered(bool value) => Set(HoveredProperty, value);

    public bool SetPosition(int x, int y)
    {
        var xChanged = SetValue(XProperty, x.ToString(CultureInfo.InvariantCulture));
        var yChanged = SetValue(YProperty, y.ToString(CultureInfo.InvariantCulture));
        return xChanged || yChanged;
    }

    public bool SetAxisPosition(string axis, string value) =>
        SetValue(axis == "y" ? YProperty : XProperty, value);

    public bool SetKeyboardKey(string keyId, bool pressed) => Set(KeyboardKeyPropertyPrefix + keyId, pressed);

    public bool SetTouched(string objectName, bool touched) =>
        SetValue(TouchedObjectPropertyPrefix + objectName.ToLowerInvariant(), touched ? "истина" : "ложь");

    public bool SetSize(double percent) => SetNumber(SizeProperty, percent);
    public bool SetTransparency(double percent) => SetNumber(TransparencyProperty, percent);

    private bool Set(string property, bool value) => SetValue(property, value ? "истина" : "ложь");

    private bool SetNumber(string property, double value) =>
        SetValue(property, value.ToString("R", CultureInfo.InvariantCulture));

    private bool SetValue(string property, string value)
    {
        if (values.TryGetValue(property, out var current) && current == value) return false;
        values[property] = value;
        return true;
    }
}

internal static class ScriptEventBlockParser
{
    public static IReadOnlyList<ScriptEventBlock> Parse(IReadOnlyList<RuntimeCommand> commands)
    {
        var blocks = new List<ScriptEventBlock>();
        for (var index = 0; index < commands.Count; index++)
        {
            if (commands[index].Kind != "event") continue;
            var end = index + 1;
            while (end < commands.Count && commands[end].Kind is not ("event" or "event-end")) end++;
            blocks.Add(new ScriptEventBlock(commands[index].Argument, index + 1, end, commands[index].Condition));
            index = end - 1;
        }
        return blocks;
    }
}

internal sealed class ScriptFlowBuilder(List<RuntimeCommand> commands)
{
    private readonly Stack<FlowFrame> frames = new();

    public IReadOnlyList<int> OpenBlockLines => frames.Select(frame => frame.OpenLine).ToArray();
    public IReadOnlyList<OpenScriptBlock> OpenBlocks => frames
        .Select(frame => new OpenScriptBlock(frame.OpenLine, frame.DisplayName)).ToArray();

    public bool BeginIf(string condition, int line, out string error)
    {
        if (!ConditionalDirectiveParser.IsValidCondition(condition))
        {
            error = "Условие должно сравнивать два значения, например «готово = истина».";
            return false;
        }

        var branchIndex = commands.Count;
        commands.Add(new RuntimeCommand("branch-false", condition, line));
        frames.Push(new FlowFrame(FlowBlockKind.If, line, branchIndex, branchIndex));
        error = string.Empty;
        return true;
    }

    public void BeginForever(int line) => frames.Push(new FlowFrame(FlowBlockKind.Forever, line, commands.Count));

    public bool BeginWhile(string condition, int line, out string error)
    {
        if (!ConditionalDirectiveParser.IsValidCondition(condition))
        {
            error = "Условие цикла должно сравнивать два значения, например «готово = истина».";
            return false;
        }

        var branchIndex = commands.Count;
        commands.Add(new RuntimeCommand("branch-false", condition, line));
        frames.Push(new FlowFrame(FlowBlockKind.While, line, branchIndex, branchIndex));
        error = string.Empty;
        return true;
    }

    public bool Break(int line, out string error)
    {
        var loop = frames.FirstOrDefault(frame => frame.Kind is FlowBlockKind.Forever or FlowBlockKind.While);
        if (loop is null)
        {
            error = "Команду «Прервать» можно использовать только внутри цикла «Вечно» или «Пока».";
            return false;
        }

        loop.BreakJumps.Add(commands.Count);
        commands.Add(new RuntimeCommand("jump", string.Empty, line));
        error = string.Empty;
        return true;
    }

    public bool ElseIf(string condition, int line, out string error)
    {
        if (!TryGetFrame(out var frame, out error)) return false;
        if (frame.HasElse)
        {
            error = "После команды «Иначе» нельзя добавлять «Иначеесли».";
            return false;
        }
        if (!ConditionalDirectiveParser.IsValidCondition(condition))
        {
            error = "Условие должно сравнивать два значения, например «жизни > 0».";
            return false;
        }

        AddEndJump(frame, line);
        PatchPendingFalseBranch(frame, commands.Count);
        frame.PendingFalseBranch = commands.Count;
        commands.Add(new RuntimeCommand("branch-false", condition, line));
        error = string.Empty;
        return true;
    }

    public bool Else(int line, out string error)
    {
        if (!TryGetFrame(out var frame, out error)) return false;
        if (frame.HasElse)
        {
            error = "В одном условии может быть только одна команда «Иначе».";
            return false;
        }

        AddEndJump(frame, line);
        PatchPendingFalseBranch(frame, commands.Count);
        frame.PendingFalseBranch = null;
        frame.HasElse = true;
        error = string.Empty;
        return true;
    }

    public bool End(out int openingLine, out string error)
    {
        if (!frames.TryPeek(out var frame))
        {
            openingLine = 0;
            error = "Команда «Конец» находится вне блока.";
            return false;
        }

        frames.Pop();
        openingLine = frame.OpenLine;
        if (frame.Kind is FlowBlockKind.Forever or FlowBlockKind.While)
        {
            commands.Add(new RuntimeCommand("loop-jump", string.Empty, frame.OpenLine, frame.LoopStart));
            if (frame.PendingFalseBranch is { } loopBranch)
            {
                commands[loopBranch] = commands[loopBranch] with { Target = commands.Count };
            }
            foreach (var jump in frame.BreakJumps)
            {
                commands[jump] = commands[jump] with { Target = commands.Count };
            }
            error = string.Empty;
            return true;
        }

        PatchPendingFalseBranch(frame, commands.Count);
        foreach (var jump in frame.EndJumps)
        {
            commands[jump] = commands[jump] with { Target = commands.Count };
        }
        error = string.Empty;
        return true;
    }

    private bool TryGetFrame(out FlowFrame frame, out string error)
    {
        if (frames.TryPeek(out frame!) && frame.Kind == FlowBlockKind.If)
        {
            error = string.Empty;
            return true;
        }
        error = "Команда «Иначе» или «Иначе если» находится вне блока «Если».";
        return false;
    }

    private void AddEndJump(FlowFrame frame, int line)
    {
        frame.EndJumps.Add(commands.Count);
        commands.Add(new RuntimeCommand("jump", string.Empty, line));
    }

    private void PatchPendingFalseBranch(FlowFrame frame, int target)
    {
        if (frame.PendingFalseBranch is { } branch)
        {
            commands[branch] = commands[branch] with { Target = target };
        }
    }

    private enum FlowBlockKind
    {
        If,
        Forever,
        While
    }

    private sealed class FlowFrame(FlowBlockKind kind, int openLine, int loopStart, int? pendingFalseBranch = null)
    {
        public FlowBlockKind Kind { get; } = kind;
        public int OpenLine { get; } = openLine;
        public int LoopStart { get; } = loopStart;
        public string DisplayName => Kind switch
        {
            FlowBlockKind.If => "Если",
            FlowBlockKind.Forever => "Вечно",
            FlowBlockKind.While => "Пока",
            _ => "Блок"
        };
        public int? PendingFalseBranch { get; set; } = pendingFalseBranch;
        public bool HasElse { get; set; }
        public List<int> EndJumps { get; } = [];
        public List<int> BreakJumps { get; } = [];
    }
}
