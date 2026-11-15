using System.Collections.Generic;
using Avalonia.Media;

namespace OpenLoidFlame.Themes.OpenLoidFlame.Runtime.Generation;

public sealed class ThemeScheme
{
    public ThemeScheme(ThemePalette palette, IReadOnlyDictionary<string, Color> semanticColors)
    {
        Palette = palette;
        SemanticColors = semanticColors;
    }

    public ThemePalette Palette { get; }
    public IReadOnlyDictionary<string, Color> SemanticColors { get; }
}