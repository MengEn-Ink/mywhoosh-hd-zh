using System.Drawing;
using System.Drawing.Text;

namespace Whoosh.Gui;

/// <summary>03 竞速座舱的配色与字体。全部用系统已装字体，不引入外部字体文件。</summary>
internal static class Palette
{
    public static readonly Color Bg = Color.FromArgb(7, 18, 28);
    public static readonly Color Rail = Color.FromArgb(4, 12, 20);
    public static readonly Color Panel = Color.FromArgb(15, 33, 49);
    public static readonly Color PanelAlt = Color.FromArgb(10, 25, 38);
    public static readonly Color Border = Color.FromArgb(36, 69, 94);
    public static readonly Color Line = Color.FromArgb(27, 53, 74);
    public static readonly Color Ink = Color.FromArgb(243, 248, 251);
    public static readonly Color Muted = Color.FromArgb(141, 165, 185);
    public static readonly Color Dim = Color.FromArgb(92, 123, 146);
    public static readonly Color Accent = Color.FromArgb(216, 255, 0);
    public static readonly Color AccentInk = Color.FromArgb(9, 18, 12);
    public static readonly Color Ok = Color.FromArgb(126, 224, 158);
    public static readonly Color Warn = Color.FromArgb(255, 193, 96);

    public static readonly string Family = PickFamily();

    private static string PickFamily()
    {
        using var installed = new InstalledFontCollection();
        var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" })
            if (names.Contains(candidate)) return candidate;
        return "Segoe UI";
    }

    public static Font F(float pixels, bool bold = false) =>
        new(Family, pixels, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    public static Color Blend(Color from, Color to, float t) => Color.FromArgb(
        (int)(from.A + (to.A - from.A) * t),
        (int)(from.R + (to.R - from.R) * t),
        (int)(from.G + (to.G - from.G) * t),
        (int)(from.B + (to.B - from.B) * t));
}
