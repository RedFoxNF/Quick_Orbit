using System.Globalization;
using System.Windows.Media;

namespace QuickOrbit.Utilities;

public static class ColorUtil
{
    public static Color Parse(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var text = value.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        var hex = text[1..];
        try
        {
            return hex.Length switch
            {
                3 => Color.FromRgb(
                    (byte)(Convert.ToByte(new string(hex[0], 2), 16)),
                    (byte)(Convert.ToByte(new string(hex[1], 2), 16)),
                    (byte)(Convert.ToByte(new string(hex[2], 2), 16))),
                6 => Color.FromRgb(
                    (byte)(Convert.ToByte(hex[..2], 16)),
                    (byte)(Convert.ToByte(hex.Substring(2, 2), 16)),
                    (byte)(Convert.ToByte(hex.Substring(4, 2), 16))),
                8 => Color.FromArgb(
                    (byte)(Convert.ToByte(hex[..2], 16)),
                    (byte)(Convert.ToByte(hex.Substring(2, 2), 16)),
                    (byte)(Convert.ToByte(hex.Substring(4, 2), 16)),
                    (byte)(Convert.ToByte(hex.Substring(6, 2), 16))),
                _ => fallback
            };
        }
        catch (FormatException)
        {
            return fallback;
        }
        catch (OverflowException)
        {
            return fallback;
        }
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        var hex = text[1..];
        if (hex.Length is not (3 or 6 or 8))
        {
            return false;
        }

        foreach (var c in hex)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush Brush(Color color, byte alpha) => Brush(WithAlpha(color, alpha));

    public static Color FromHsv(double hue, double saturation, double value)
    {
        hue = (hue % 360.0 + 360.0) % 360.0;
        saturation = Math.Clamp(saturation, 0.0, 1.0);
        value = Math.Clamp(value, 0.0, 1.0);

        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60.0) % 2 - 1));
        var m = value - c;

        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    public static Color StableColor(string text, double saturation = 0.62, double value = 0.82)
    {
        uint hash = 2166136261;
        foreach (var c in text ?? string.Empty)
        {
            hash ^= c;
            hash *= 16777619;
        }

        var hue = hash % 360u;
        return FromHsv(hue, saturation, value);
    }

    public static string ToHex(Color color) =>
        string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);

    public static Color ContrastText(Color background)
    {
        var luminance = (0.2126 * Srgb(background.R)) + (0.7152 * Srgb(background.G)) + (0.0722 * Srgb(background.B));
        return luminance > 0.55 ? Color.FromRgb(9, 16, 22) : Colors.White;
    }

    private static double Srgb(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
