using IskraStudio;
using Xunit;

namespace IskraStudio.Tests;

public sealed class RuntimeObjectPropertiesTests
{
    [Fact]
    public void KeyboardKeyStateIsStoredUnderCanonicalName()
    {
        var properties = new RuntimeObjectProperties();

        Assert.True(properties.SetKeyboardKey("q", true));
        Assert.Equal("истина", properties.Values[RuntimeObjectProperties.KeyboardKeyPropertyPrefix + "q"]);
        Assert.False(properties.SetKeyboardKey("q", true));
        Assert.True(properties.SetKeyboardKey("q", false));
        Assert.Equal("ложь", properties.Values[RuntimeObjectProperties.KeyboardKeyPropertyPrefix + "q"]);
    }

    [Fact]
    public void KeyboardKeyResolvesThroughConditionParser()
    {
        var properties = new RuntimeObjectProperties();
        properties.SetKeyboardKey("пробел", true);

        Assert.Equal("истина", ConditionalDirectiveParser.ResolveVariable("Свойство.клавиша.Пробел", properties.Values));
        Assert.Equal("ложь", ConditionalDirectiveParser.ResolveVariable("Свойство.клавиша.ввод", properties.Values));
    }

    [Fact]
    public void TouchedStateIsStoredLowercaseWithoutPriorDeclaration()
    {
        var properties = new RuntimeObjectProperties();

        Assert.True(properties.SetTouched("Монета", true));
        Assert.Equal("истина", properties.Values[RuntimeObjectProperties.TouchedObjectPropertyPrefix + "монета"]);
        Assert.False(properties.SetTouched("монета", true));
        Assert.True(properties.SetTouched("Монета", false));
        Assert.Equal("ложь", properties.Values[RuntimeObjectProperties.TouchedObjectPropertyPrefix + "монета"]);
    }

    [Fact]
    public void TouchedValueResolvesThroughConditionParser()
    {
        var properties = new RuntimeObjectProperties();
        properties.SetTouched("Красная шапка", true);

        Assert.Equal("истина", ConditionalDirectiveParser.ResolveVariable("Свойство.коснулся.красная шапка", properties.Values));
        Assert.Equal("ложь", ConditionalDirectiveParser.ResolveVariable("Свойство.коснулся.монета", properties.Values));
    }

    [Fact]
    public void AxisPositionUpdatesSingleCoordinate()
    {
        var properties = new RuntimeObjectProperties();

        Assert.True(properties.SetAxisPosition("x", "120"));
        Assert.Equal("120", properties.Values[RuntimeObjectProperties.XProperty]);
        Assert.Equal("0", properties.Values[RuntimeObjectProperties.YProperty]);
        Assert.False(properties.SetAxisPosition("x", "120"));
        Assert.True(properties.SetAxisPosition("y", "-40"));
        Assert.Equal("-40", properties.Values[RuntimeObjectProperties.YProperty]);
        Assert.Equal("120", properties.Values[RuntimeObjectProperties.XProperty]);
    }
}
