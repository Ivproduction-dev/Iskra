using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace Iskra.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, WindowSoftInputMode = SoftInput.AdjustResize, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density | ConfigChanges.FontScale | ConfigChanges.Keyboard)]
public class MainActivity : MauiAppCompatActivity
{
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is not null && KeyboardBridge.Dispatch(e.KeyCode, e.Action == KeyEventActions.Down && e.RepeatCount == 0))
        {
            return true;
        }
        return base.DispatchKeyEvent(e);
    }
}
