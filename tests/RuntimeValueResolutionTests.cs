using IskraStudio;
using Xunit;

namespace IskraStudio.Tests;

public sealed class RuntimeValueResolutionTests
{
    [Theory]
    [InlineData("свойство.х", "Свойство.х", "240")]
    [InlineData("свойство.у", "Свойство.у", "-160")]
    public void AssignmentResolvesObjectCoordinatePropertiesRegardlessOfCase(
        string expression,
        string propertyKey,
        string expected)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [propertyKey] = expected
        };

        Assert.Equal(expected, ProjectRuntime.ResolveValue(expression, properties));
    }

    [Fact]
    public void QuotedCoordinatePropertyRemainsText()
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Свойство.х"] = "240"
        };

        Assert.Equal("свойство.х", ProjectRuntime.ResolveValue("\"свойство.х\"", properties));
    }

    [Fact]
    public void PlacementUpdatesCoordinatePropertiesForLaterAssignments()
    {
        var properties = new RuntimeObjectProperties();

        Assert.Equal("0", ProjectRuntime.ResolveValue("свойство.х", properties.Values));
        Assert.Equal("0", ProjectRuntime.ResolveValue("свойство.у", properties.Values));

        properties.SetPosition(200, -160);

        Assert.Equal("200", ProjectRuntime.ResolveValue("свойство.х", properties.Values));
        Assert.Equal("-160", ProjectRuntime.ResolveValue("свойство.у", properties.Values));
    }

    [Fact]
    public void SizeAndTransparencyPropertiesExposeCurrentPercentValues()
    {
        var properties = new RuntimeObjectProperties();

        Assert.Equal("100", ProjectRuntime.ResolveValue("Свойство.размер", properties.Values));
        Assert.Equal("0", ProjectRuntime.ResolveValue("свойство.прозрачность", properties.Values));

        properties.SetSize(500);
        properties.SetTransparency(12.5);

        Assert.Equal("500", ProjectRuntime.ResolveValue("свойство.РАЗМЕР", properties.Values));
        Assert.Equal("12.5", ProjectRuntime.ResolveValue("Свойство.ПРОЗРАЧНОСТЬ", properties.Values));
    }

    [Fact]
    public void RandomIntegerValueStaysWithinInclusiveBounds()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var value = ProjectRuntime.ResolveValue(
                "Случайное.значение.от.5.до.10",
                new Dictionary<string, string>());

            Assert.True(int.TryParse(value, out var number));
            Assert.InRange(number, 5, 10);
        }
    }

    [Fact]
    public void RandomDecimalValueCanBeUsedInTextAndConditions()
    {
        const string fixedValue = "Случайное.значение.от.1.5.до.1.5";

        Assert.Equal("Значение:1.5", ProjectRuntime.ResolveValue($"Значение:..{fixedValue}", new Dictionary<string, string>()));
        Assert.True(ConditionalDirectiveParser.Evaluate($"{fixedValue} = 1.5", new Dictionary<string, string>()));
    }
}
