using IskraStudio;
using Xunit;

namespace IskraStudio.Tests;

public sealed class ConditionalParserBoundaryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ready =")]
    [InlineData("= true")]
    [InlineData("ready = true = false")]
    [InlineData("(ready = true")]
    [InlineData("ready = true)")]
    [InlineData("name = \"unterminated")]
    [InlineData("ready = true trailing")]
    public void ConditionParserRejectsEmptyOrIncompleteExpressions(string expression)
    {
        Assert.False(ConditionalDirectiveParser.IsValidCondition(expression));
    }

    [Theory]
    [InlineData("(ready = true)", true)]
    [InlineData("ready = true и lives >= 0", true)]
    [InlineData("name = \"Пилот 7\"", true)]
    public void ConditionParserAcceptsCompleteOperandsAtBoundaries(string expression, bool expected)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ready"] = "true",
            ["lives"] = "0",
            ["name"] = "Пилот 7"
        };

        Assert.Equal(expected, ConditionalDirectiveParser.Evaluate(expression, variables));
    }
}
