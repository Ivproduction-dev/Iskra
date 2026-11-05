using Android.Views;

namespace Iskra.Android;

internal static class KeyboardBridge
{
    private static MobileGameRuntime? runtime;
    private static readonly HashSet<Keycode> pressed = [];

    public static void Attach(MobileGameRuntime gameRuntime)
    {
        runtime = gameRuntime;
        pressed.Clear();
    }

    public static void Detach(MobileGameRuntime gameRuntime)
    {
        if (!ReferenceEquals(runtime, gameRuntime)) return;
        runtime = null;
        pressed.Clear();
    }

    public static bool Dispatch(Keycode code, bool down)
    {
        var target = runtime;
        if (target is null) return false;
        var keyId = MapKey(code);
        if (keyId is null) return false;
        if (down)
        {
            if (!pressed.Add(code)) return true;
            target.Engine.KeyDown(keyId);
        }
        else
        {
            if (pressed.Remove(code)) target.Engine.KeyUp(keyId);
        }
        return true;
    }

    private static string? MapKey(Keycode code)
    {
        if (code is >= Keycode.A and <= Keycode.Z)
        {
            return ((char)('a' + (code - Keycode.A))).ToString();
        }
        if (code is >= Keycode.Num0 and <= Keycode.Num9)
        {
            return ((char)('0' + (code - Keycode.Num0))).ToString();
        }
        if (code is >= Keycode.Numpad0 and <= Keycode.Numpad9)
        {
            return ((char)('0' + (code - Keycode.Numpad0))).ToString();
        }
        return code switch
        {
            Keycode.DpadRight => "право",
            Keycode.DpadLeft => "лево",
            Keycode.DpadUp => "верх",
            Keycode.DpadDown => "вниз",
            Keycode.Space => "пробел",
            Keycode.Enter => "ввод",
            Keycode.Escape => "отмена",
            Keycode.Del => "забой",
            _ => null
        };
    }
}
