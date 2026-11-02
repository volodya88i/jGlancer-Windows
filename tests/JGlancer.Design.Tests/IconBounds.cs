using System.Globalization;
using System.Text.RegularExpressions;

namespace JGlancer.Design.Tests;

/// <summary>
/// Bounds of the points an SVG path visits (end points and control points; arcs by their end points), enough to catch
/// path data that jumps off the icon canvas. A test helper, not a renderer.
/// </summary>
internal static partial class IconBounds
{
    internal readonly record struct Box(double Left, double Top, double Right, double Bottom);

    public static Box Of(string data)
    {
        var tokens = Token().Matches(data).Select(m => m.Value).ToList();
        double x = 0, y = 0, startX = 0, startY = 0;
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        void Visit(double px, double py)
        {
            left = Math.Min(left, px);
            top = Math.Min(top, py);
            right = Math.Max(right, px);
            bottom = Math.Max(bottom, py);
        }
        var i = 0;
        var command = 'M';
        double Next() => double.Parse(tokens[i++], CultureInfo.InvariantCulture);
        bool HasNumber() => i < tokens.Count && !char.IsLetter(tokens[i][0]);
        while (i < tokens.Count)
        {
            if (char.IsLetter(tokens[i][0]))
            {
                command = tokens[i++][0];
            }
            var relative = char.IsLower(command);
            double ox = relative ? x : 0, oy = relative ? y : 0;
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    x = ox + Next();
                    y = oy + Next();
                    startX = x;
                    startY = y;
                    Visit(x, y);
                    // Pairs after a move are lines of the same relativity.
                    command = relative ? 'l' : 'L';
                    break;
                case 'L':
                case 'T':
                    x = ox + Next();
                    y = oy + Next();
                    Visit(x, y);
                    break;
                case 'H':
                    x = ox + Next();
                    Visit(x, y);
                    break;
                case 'V':
                    y = oy + Next();
                    Visit(x, y);
                    break;
                case 'C':
                    Visit(ox + Next(), oy + Next());
                    Visit(ox + Next(), oy + Next());
                    x = ox + Next();
                    y = oy + Next();
                    Visit(x, y);
                    break;
                case 'S':
                case 'Q':
                    Visit(ox + Next(), oy + Next());
                    x = ox + Next();
                    y = oy + Next();
                    Visit(x, y);
                    break;
                case 'A':
                    i += 5;
                    x = ox + Next();
                    y = oy + Next();
                    Visit(x, y);
                    break;
                case 'Z':
                    x = startX;
                    y = startY;
                    if (HasNumber())
                    {
                        throw new FormatException("numbers after Z");
                    }
                    break;
                default:
                    throw new FormatException($"unknown command {command}");
            }
        }
        return new Box(left, top, right, bottom);
    }

    // Commands, and numbers (a sign, or a second '.', starts a new number: ".5.5" is two numbers).
    [GeneratedRegex(@"[A-Za-z]|-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")]
    private static partial Regex Token();
}
