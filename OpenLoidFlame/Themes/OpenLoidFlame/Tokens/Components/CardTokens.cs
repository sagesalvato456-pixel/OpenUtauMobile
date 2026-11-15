using Avalonia;
using OpenLoidFlame.Themes.OpenLoidFlame.Tokens.Foundation;

namespace OpenLoidFlame.Themes.OpenLoidFlame.Tokens.Components;

/// <summary>共享卡片规格。</summary>
public static class CardTokens
{
    public static Thickness BorderThickness => new(1);
    public static Thickness Padding => LayoutTokens.InsetSM;
    public static CornerRadius CornerRadius => ShapeTokens.CornerM;
}
