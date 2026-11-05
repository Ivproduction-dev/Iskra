using System.Windows.Input;

namespace IskraStudio;

internal static class KeyboardKeyMapper
{
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
