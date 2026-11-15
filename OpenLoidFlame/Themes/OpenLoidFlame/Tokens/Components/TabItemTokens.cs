using Avalonia;
using Avalonia.Media;
using OpenLoidFlame.Themes.OpenLoidFlame.Tokens.Semantic;

namespace OpenLoidFlame.Themes.OpenLoidFlame.Tokens.Components;

/// <summary>共享页签规格。</summary>
public static class TabItemTokens
{
    public static double ContainerHeight => 48d;
    public static double ActiveIndicatorHeight => 3d;
    public static double LabelSize => TypographyTokens.TitleSSize;
    public static Thickness HeaderInset => new(16, 0);
    public static CornerRadius ActiveIndicatorCorner => new(3, 3, 0, 0);
    public static FontWeight LabelWeight => FontWeight.Medium;
}
