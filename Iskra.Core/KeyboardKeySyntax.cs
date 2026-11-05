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
}
