namespace Iskra.Android;

internal static class MobileTheme
{
    private static bool Dark => Application.Current?.RequestedTheme == AppTheme.Dark;

    public static Color SurfacePrimary => Color(Dark ? "#171614" : "#FAF9F5");
    public static Color SurfacePanel => Color(Dark ? "#211F1C" : "#FFFFFF");
    public static Color SurfaceSoft => Color(Dark ? "#292723" : "#F5F0E8");
    public static Color SurfaceSelected => Color(Dark ? "#39352F" : "#EFE9DE");
    public static Color TextPrimary => Color(Dark ? "#F7F4EE" : "#141413");
    public static Color TextBody => Color(Dark ? "#DFDBD3" : "#3D3D3A");
    public static Color TextMuted => Color(Dark ? "#AAA59C" : "#6C6A64");
    public static Color Border => Color(Dark ? "#403C36" : "#E6DFD8");
    public static Color Accent => Color("#A9583E");
    public static Color AccentSoft => Color(Dark ? "#3D2A23" : "#F3E3DC");
    public static Color Error => Color(Dark ? "#F08080" : "#AE3A3A");

    private static Color Color(string value) => Microsoft.Maui.Graphics.Color.FromArgb(value);
}
