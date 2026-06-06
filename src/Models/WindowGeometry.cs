using System.Text.RegularExpressions;

namespace Tfx;

/// <summary>
/// X11-style window geometry: <c>[WIDTHxHEIGHT][±X±Y]</c>. Each half is
/// optional, but at least one must be present. A leading <c>-</c> on an offset
/// anchors it to the right / bottom edge of the work area (X11 convention).
/// Numbers are WPF logical units (DIPs) — equal to physical pixels at 100% scale.
/// Examples: <c>1200x800</c>, <c>1200x800+100+50</c>, <c>+100+50</c>, <c>1200x800-0-0</c>.
/// </summary>
public sealed class WindowGeometry
{
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? OffsetX { get; init; }
    public int? OffsetY { get; init; }
    public bool FromRight { get; init; }
    public bool FromBottom { get; init; }

    public bool HasSize => Width.HasValue && Height.HasValue;
    public bool HasPosition => OffsetX.HasValue && OffsetY.HasValue;

    private static readonly Regex Pattern = new(
        @"^(?:(\d+)x(\d+))?(?:([+-])(\d+)([+-])(\d+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string? text, out WindowGeometry geometry)
    {
        geometry = new WindowGeometry();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = Pattern.Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        var hasSize = match.Groups[1].Success;
        var hasPos = match.Groups[3].Success;
        if (!hasSize && !hasPos)
        {
            return false;
        }

        int? width = null, height = null, x = null, y = null;
        var fromRight = false;
        var fromBottom = false;

        if (hasSize)
        {
            if (!int.TryParse(match.Groups[1].Value, out var w) ||
                !int.TryParse(match.Groups[2].Value, out var h) ||
                w <= 0 || h <= 0)
            {
                return false;
            }
            width = w;
            height = h;
        }

        if (hasPos)
        {
            if (!int.TryParse(match.Groups[4].Value, out var px) ||
                !int.TryParse(match.Groups[6].Value, out var py))
            {
                return false;
            }
            x = px;
            y = py;
            fromRight = match.Groups[3].Value == "-";
            fromBottom = match.Groups[5].Value == "-";
        }

        geometry = new WindowGeometry
        {
            Width = width,
            Height = height,
            OffsetX = x,
            OffsetY = y,
            FromRight = fromRight,
            FromBottom = fromBottom,
        };
        return true;
    }
}
