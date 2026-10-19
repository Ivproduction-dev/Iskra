using System.Windows.Input;

namespace IskraStudio;

internal static class KeyboardKeySyntax
{
    private const string CyrillicKeys = "йцукенгшщзхъфывапролджэячсмитьбю.ё";
    private const string LatinKeys = "qwertyuiop[]asdfghjkl;'zxcvbnm,./`";

    private static readonly Dictionary<string, string> SpecialKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["право"] = "право",
        ["лево"] = "лево",
        ["верх"] = "верх",
        ["вниз"] = "вниз",
        ["пробел"] = "пробел",
        ["ввод"] = "ввод",
        ["отмена"] = "отмена",
        ["забой"] = "забой"
    };

    public const string AllowedKeysHint =
        "право, лево, верх, вниз, пробел, ввод, отмена, забой, буквы и цифры";

    public static bool TryNormalize(string token, out string keyId)
    {
        keyId = string.Empty;
        var trimmed = token.Trim();
        if (trimmed.Length > 1 && trimmed.EndsWith('.'))
        {
            trimmed = trimmed[..^1];
        }
        if (trimmed.Length == 0)
        {
            return false;
        }
        if (trimmed.Length > 1)
        {
            if (!SpecialKeys.TryGetValue(trimmed, out var special))
            {
                return false;
            }
            keyId = special;
            return true;
        }

        var symbol = char.ToLowerInvariant(trimmed[0]);
        if (char.IsDigit(symbol))
        {
            keyId = symbol.ToString();
            return true;
        }
        var mappedIndex = CyrillicKeys.IndexOf(symbol);
        if (mappedIndex >= 0)
        {
            keyId = LatinKeys[mappedIndex].ToString();
            return true;
        }
        if (symbol is >= 'a' and <= 'z')
        {
            keyId = symbol.ToString();
            return true;
        }
        return false;
    }

    public static string? NormalizeKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return ((char)('a' + (key - Key.A))).ToString();
        }
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return ((char)('0' + (key - Key.NumPad0))).ToString();
        }
        return key switch
        {
            Key.Left => "лево",
            Key.Right => "право",
            Key.Up => "верх",
            Key.Down => "вниз",
            Key.Space => "пробел",
            Key.Enter => "ввод",
            Key.Escape => "отмена",
            Key.Back => "забой",
            _ => null
        };
    }
}
