using System.Windows;
using System.Windows.Media.Effects;

namespace PaperTodo;

public static class AppUi
{
    public const double RadiusSmall = 4;
    public const double RadiusControl = 8;
    public const double RadiusBlock = 12;
    public const double RadiusPanel = 14;
    public const double RadiusShell = 18;

    public static CornerRadius SmallRadius => new(RadiusSmall);
    public static CornerRadius ControlRadius => new(RadiusControl);
    public static CornerRadius BlockRadius => new(RadiusBlock);
    public static CornerRadius PanelRadius => new(RadiusPanel);
    public static CornerRadius ShellRadius => new(RadiusShell);

    public static Thickness PaperChromePadding => new(0);
    public static Thickness SettingsPadding => new(16, 14, 16, 16);
    public static Thickness TodoRowPadding => new(3, 2, 3, 2);

    public static DropShadowEffect PaperShadow()
    {
        return new DropShadowEffect
        {
            BlurRadius = Theme.IsDark ? 24 : 22,
            ShadowDepth = 0,
            Opacity = Theme.IsDark ? 0.34 : 0.18
        };
    }

    public static DropShadowEffect FloatingShadow()
    {
        return new DropShadowEffect
        {
            BlurRadius = Theme.IsDark ? 26 : 24,
            ShadowDepth = 2,
            Opacity = Theme.IsDark ? 0.36 : 0.22
        };
    }

    public static DropShadowEffect SettingsShadow()
    {
        return new DropShadowEffect
        {
            BlurRadius = Theme.IsDark ? 30 : 28,
            ShadowDepth = 0,
            Opacity = Theme.IsDark ? 0.36 : 0.2
        };
    }

    public static DropShadowEffect NoteCanvasElementShadow(int layerRank, int layerCount, bool active)
    {
        var normalized = Math.Clamp((double)Math.Max(1, layerRank) / Math.Max(1, layerCount), 0.0, 1.0);
        return new DropShadowEffect
        {
            BlurRadius = active ? 24 : 10 + (normalized * 10),
            ShadowDepth = active ? 5 : 1.5 + (normalized * 3.5),
            Direction = 315,
            Opacity = active
                ? (Theme.IsDark ? 0.52 : 0.30)
                : (Theme.IsDark ? 0.24 + (normalized * 0.14) : 0.14 + (normalized * 0.12))
        };
    }
}
