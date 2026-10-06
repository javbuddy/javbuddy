using System.Globalization;

namespace Javbuddy.Services.Scenes;

/// <summary>Formats and parses scene times: <c>m:ss</c> / <c>h:mm:ss</c>, optionally
/// with a fractional second, or plain seconds.</summary>
public static class SceneTimeFormat
{
    /// <summary>Whole-second display for scene lists, e.g. <c>4:05</c> or <c>1:02:03</c>.</summary>
    public static string Format(double seconds)
    {
        var total = (long)Math.Floor(Math.Max(seconds, 0));
        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    /// <summary>Like <see cref="Format"/>, but keeps up to millisecond precision (trailing zeros
    /// trimmed) so a time taken from the player survives a round-trip through an edit form.</summary>
    public static string FormatPrecise(double seconds)
    {
        var rounded = Math.Round(Math.Max(seconds, 0), 3);
        var fraction = rounded - Math.Floor(rounded);
        if (fraction < 0.0005)
        {
            return Format(rounded);
        }
        var digits = fraction.ToString("0.###", CultureInfo.InvariantCulture).TrimStart('0');
        return Format(rounded) + digits;
    }

    /// <summary>Parses <c>ss</c>, <c>m:ss</c> or <c>h:mm:ss</c> (seconds may have a fraction).
    /// Non-leading components must be below 60.</summary>
    public static bool TryParse(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split(':');
        if (parts.Length > 3)
        {
            return false;
        }

        double total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var isLast = i == parts.Length - 1;
            var part = parts[i].Trim();
            if (part.Length == 0 || part.StartsWith('-') || part.StartsWith('+'))
            {
                return false;
            }

            double value;
            if (isLast)
            {
                if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
                {
                    return false;
                }
            }
            else if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
            {
                value = whole;
            }
            else
            {
                return false;
            }

            if (i > 0 && value >= 60)
            {
                return false;
            }
            total = total * 60 + value;
        }

        seconds = total;
        return true;
    }
}
