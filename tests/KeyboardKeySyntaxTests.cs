using IskraStudio;
using System.Windows.Input;
using Xunit;

namespace IskraStudio.Tests;

public sealed class KeyboardKeySyntaxTests
{
    [Theory]
    [InlineData("й", "q")]
    [InlineData("ц", "w")]
    [InlineData("у", "e")]
    [InlineData("к", "r")]
    [InlineData("е", "t")]
    [InlineData("н", "y")]
    [InlineData("г", "u")]
    [InlineData("ш", "i")]
    [InlineData("щ", "o")]
    [InlineData("з", "p")]
    [InlineData("х", "[")]
    [InlineData("ъ", "]")]
    [InlineData("ф", "a")]
    [InlineData("ы", "s")]
    [InlineData("в", "d")]
    [InlineData("а", "f")]
    [InlineData("п", "g")]
    [InlineData("р", "h")]
    [InlineData("о", "j")]
    [InlineData("л", "k")]
    [InlineData("д", "l")]
    [InlineData("ж", ";")]
    [InlineData("э", "'")]
    [InlineData("я", "z")]
    [InlineData("ч", "x")]
    [InlineData("с", "c")]
    [InlineData("м", "v")]
    [InlineData("и", "b")]
    [InlineData("т", "n")]
    [InlineData("ь", "m")]
    [InlineData("б", ",")]
    [InlineData("ю", ".")]
    [InlineData(".", "/")]
    [InlineData("ё", "`")]
    public void CyrillicLettersNormalizeToLatin(string source, string expected)
    {
        Assert.True(KeyboardKeySyntax.TryNormalize(source, out var keyId));
        Assert.Equal(expected, keyId);
    }

    [Theory]
    [InlineData("Й", "q")]
    [InlineData("Ц", "w")]
    [InlineData("Е", "t")]
    [InlineData("Х", "[")]
    [InlineData("Ъ", "]")]
    [InlineData("Ф", "a")]
    [InlineData("Ж", ";")]
    [InlineData("Э", "'")]
    [InlineData("Б", ",")]
    [InlineData("Ю", ".")]
    [InlineData("Ё", "`")]
    public void UppercaseCyrillicNormalizesLikeLowercase(string source, string expected)
    {
        Assert.True(KeyboardKeySyntax.TryNormalize(source, out var keyId));
        Assert.Equal(expected, keyId);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("2", "2")]
    [InlineData("3", "3")]
    [InlineData("4", "4")]
    [InlineData("5", "5")]
    [InlineData("6", "6")]
    [InlineData("7", "7")]
    [InlineData("8", "8")]
    [InlineData("9", "9")]
    public void DigitsStayThemselves(string source, string expected)
    {
        Assert.True(KeyboardKeySyntax.TryNormalize(source, out var keyId));
        Assert.Equal(expected, keyId);
    }

    [Theory]
    [InlineData("право", "право")]
    [InlineData("лево", "лево")]
    [InlineData("верх", "верх")]
    [InlineData("вниз", "вниз")]
    [InlineData("пробел", "пробел")]
    [InlineData("ввод", "ввод")]
    [InlineData("отмена", "отмена")]
    [InlineData("забой", "забой")]
    public void SpecialKeysNormalizeToLowercase(string source, string expected)
    {
        Assert.True(KeyboardKeySyntax.TryNormalize(source, out var keyId));
        Assert.Equal(expected, keyId);
    }

    [Theory]
    [InlineData("ПРОБЕЛ", "пробел")]
    [InlineData("Право", "право")]
    [InlineData("Лево", "лево")]
    [InlineData("Верх", "верх")]
    [InlineData("Вниз", "вниз")]
    [InlineData("ВВОД", "ввод")]
    [InlineData("Отмена", "отмена")]
    [InlineData("ЗАБОЙ", "забой")]
    public void SpecialKeysIgnoreCase(string source, string expected)
    {
        Assert.True(KeyboardKeySyntax.TryNormalize(source, out var keyId));
        Assert.Equal(expected, keyId);
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("Z", "z")]
    [InlineData("q", "q")]
    public void LatinLettersPassThroughLowercase(string source, string expected)
    {
        Assert.True(KeyboardKeySyntax.TryNormalize(source, out var keyId));
        Assert.Equal(expected, keyId);
    }

    [Theory]
    [InlineData("ф.", "a")]
    [InlineData("5.", "5")]
    [InlineData("Ж.", ";")]
    [InlineData("Ё.", "`")]
    [InlineData("Право.", "право")]
    [InlineData("Забой.", "забой")]
    [InlineData("Ввод.", "ввод")]
    [InlineData("Отмена.", "отмена")]
    public void SingleTrailingDotIsIgnored(string source, string expected)
    {
        Assert.True(KeyboardKeySyntax.TryNormalize(source, out var keyId));
        Assert.Equal(expected, keyId);
    }

    [Theory]
    [InlineData("  ф  ", "a")]
    [InlineData(" пробел ", "пробел")]
    [InlineData("  5  ", "5")]
    public void SurroundingSpacesAreIgnored(string source, string expected)
    {
        Assert.True(KeyboardKeySyntax.TryNormalize(source, out var keyId));
        Assert.Equal(expected, keyId);
    }

    [Theory]
    [InlineData("прыжок")]
    [InlineData("ab")]
    [InlineData("ёё")]
    [InlineData("№")]
    [InlineData("向上")]
    [InlineData("-")]
    [InlineData("=")]
    [InlineData("?")]
    [InlineData("")]
    [InlineData("   ")]
    public void UnknownKeysAreRejected(string source) =>
        Assert.False(KeyboardKeySyntax.TryNormalize(source, out _));

    [Theory]
    [InlineData("ф..")]
    [InlineData("пробел..")]
    [InlineData("пробел двух слов")]
    [InlineData("Пробел .")]
    [InlineData("йй")]
    public void MultiCharacterNonWordsAreRejected(string source) =>
        Assert.False(KeyboardKeySyntax.TryNormalize(source, out _));

    [Fact]
    public void AllowedKeysHintMentionsEverySpecialWord()
    {
        foreach (var word in new[] { "право", "лево", "верх", "вниз", "пробел", "ввод", "отмена", "забой" })
        {
            Assert.Contains(word, KeyboardKeySyntax.AllowedKeysHint);
        }
        Assert.Contains("буквы и цифры", KeyboardKeySyntax.AllowedKeysHint);
    }

    [Theory]
    [InlineData(Key.A, "a")]
    [InlineData(Key.Z, "z")]
    [InlineData(Key.Q, "q")]
    [InlineData(Key.D0, "0")]
    [InlineData(Key.D9, "9")]
    [InlineData(Key.NumPad3, "3")]
    [InlineData(Key.Left, "лево")]
    [InlineData(Key.Right, "право")]
    [InlineData(Key.Up, "верх")]
    [InlineData(Key.Down, "вниз")]
    [InlineData(Key.Space, "пробел")]
    [InlineData(Key.Enter, "ввод")]
    [InlineData(Key.Escape, "отмена")]
    [InlineData(Key.Back, "забой")]
    public void PhysicalKeysMapToCanonicalIds(Key key, string expected) =>
        Assert.Equal(expected, KeyboardKeySyntax.NormalizeKey(key));

    [Theory]
    [InlineData(Key.F1)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.OemPlus)]
    [InlineData(Key.Tab)]
    [InlineData(Key.Delete)]
    public void UnsupportedPhysicalKeysMapToNothing(Key key) =>
        Assert.Null(KeyboardKeySyntax.NormalizeKey(key));
}
