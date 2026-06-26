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
}
