using IskraStudio;
using Xunit;

namespace IskraStudio.Tests;

public sealed class ImageEffectSyntaxTests
{
    [Theory]
    [InlineData("0", 0d)]
    [InlineData("0.1", 0.1d)]
    [InlineData("100", 100d)]
    public void PercentValueAcceptsFiniteValuesInRange(string source, double expected)
    {
        Assert.True(ImageEffectSyntax.TryParsePercent(source, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("100.1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("abc")]
    public void PercentValueRejectsInvalidOrOutOfRangeValues(string source)
    {
        Assert.False(ImageEffectSyntax.TryParsePercent(source, out _));
    }
}
