using System.Globalization;
using System.Text.RegularExpressions;

namespace IskraStudio;

internal static class ArithmeticExpressionSyntax
{
    private static readonly Regex RandomValue = new(
        "\\GСлучайное\\.значение\\.от\\.[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+)\\.до\\.[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryEvaluate(string expression, IReadOnlyDictionary<string, string> variables, out double value)
    {
        var parser = new Parser(expression, variables);
        return parser.TryParse(out value);
    }

    private sealed class Parser(string expression, IReadOnlyDictionary<string, string> variables)
    {
        private int position;
        private int operationCount;

        public bool TryParse(out double value)
        {
            value = 0;
            if (!ParseExpression(out value)) return false;
            SkipWhitespace();
            return position == expression.Length && operationCount > 0 && double.IsFinite(value);
        }

        private bool ParseExpression(out double value)
        {
            if (!ParseTerm(out value)) return false;
            while (true)
            {
                SkipWhitespace();
                if (!Take('+') && !Take('-')) return double.IsFinite(value);
                var operation = expression[position - 1];
                if (!ParseTerm(out var right)) return false;
                value = operation == '+' ? value + right : value - right;
                operationCount++;
                if (!double.IsFinite(value)) return false;
            }
        }

        private bool ParseTerm(out double value)
        {
            if (!ParseUnary(out value)) return false;
            while (true)
            {
                SkipWhitespace();
                if (!Take('*') && !Take('/')) return double.IsFinite(value);
                var operation = expression[position - 1];
                if (!ParseUnary(out var right) || operation == '/' && right == 0) return false;
                value = operation == '*' ? value * right : value / right;
                operationCount++;
                if (!double.IsFinite(value)) return false;
            }
        }

        private bool ParseUnary(out double value)
        {
            SkipWhitespace();
            if (Take('+')) return ParseUnary(out value);
            if (Take('-'))
            {
                if (!ParseUnary(out value)) return false;
                value = -value;
                return double.IsFinite(value);
            }
            return ParsePrimary(out value);
        }

        private bool ParsePrimary(out double value)
        {
            value = 0;
            SkipWhitespace();
            if (Take('('))
            {
                if (!ParseExpression(out value)) return false;
                SkipWhitespace();
                return Take(')') && double.IsFinite(value);
            }

            var start = position;
            var randomMatch = RandomValue.Match(expression, position);
            if (randomMatch.Success && randomMatch.Index == position)
            {
                position += randomMatch.Length;
            }
            else
            {
                while (position < expression.Length && !char.IsWhiteSpace(expression[position]) &&
                    expression[position] is not ('+' or '-' or '*' or '/' or '(' or ')'))
                {
                    position++;
                }
            }

            if (position == start) return false;
            var token = expression[start..position];
            var resolved = double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? token
                : ConditionalDirectiveParser.ResolveVariable(token, variables);
            return double.TryParse(resolved, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                double.IsFinite(value);
        }

        private bool Take(char character)
        {
            if (position >= expression.Length || expression[position] != character) return false;
            position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (position < expression.Length && char.IsWhiteSpace(expression[position])) position++;
        }
    }
}
