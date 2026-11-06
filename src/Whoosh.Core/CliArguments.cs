using System.Globalization;

namespace Whoosh.Core;

public static class CliArguments
{
    public static (int Width, int Height, bool Topmost) Window(string[] args)
    {
        if (args.Length is not (6 or 7) || args[0] != "window" || args[1] != "--width" ||
            args[3] != "--height" || args[5] != "--yes" || (args.Length == 7 && args[6] != "--topmost") ||
            !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int height))
            throw new ArgumentException("window --width 1280 --height 720 --yes [--topmost]");
        if (width is < 800 or > 3840 || height is < 450 or > 2160)
            throw new ArgumentException("窗口范围：宽800–3840，高450–2160。");
        return (width, height, args.Length == 7);
    }
}
