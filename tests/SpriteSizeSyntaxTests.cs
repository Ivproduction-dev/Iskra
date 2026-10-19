using IskraStudio;
using Xunit;

namespace IskraStudio.Tests;

public sealed class SpriteSizeSyntaxTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(500, true)]
    [InlineData(1000, true)]
    [InlineData(100000, true)]
    [InlineData(0.000001, true)]
    [InlineData(99999.999999, true)]
    [InlineData(-1, false)]
    [InlineData(double.NegativeInfinity, false)]
    [InlineData(100001, false)]
    [InlineData(double.PositiveInfinity, false)]
    [InlineData(double.NaN, false)]
    public void SetSizeAcceptsOnlyValuesFromZeroThroughOneHundredThousand(double amount, bool expected)
    {
        Assert.Equal(expected, SpriteSizeSyntax.IsValidSetSize(amount));
    }

    [Theory]
    [InlineData(100, 400, 500)]
    [InlineData(100, 900, 1000)]
    [InlineData(100, 100000, 100000)]
    [InlineData(99990, 20, 100000)]
    [InlineData(10, -100, 0)]
    [InlineData(0, -double.Epsilon, 0)]
    [InlineData(100, double.PositiveInfinity, 100000)]
    [InlineData(100, double.NegativeInfinity, 0)]
    public void ChangeSizeClampsTheResultToSupportedRange(double current, double change, double expected)
    {
        Assert.Equal(expected, SpriteSizeSyntax.ApplyChange(current, change));
    }
}
