namespace JGlancer.Core.Model;

/// <summary>
/// Turns horizontal mouse drags (and touchpad swipes) into paging between files (macOS SwipeTracker, FEATURES §4.1).
/// Travel is how far the content moved: negative = to the left, which brings the next file.
/// <para>
/// Instant mode pages once the travel passes <see cref="InstantThreshold"/> (once per gesture). Animated mode lets the
/// content follow the pointer and decides on release: past <see cref="CommitFraction"/> of the width, or a flick,
/// completes the page; otherwise the content springs back. Without a file in that direction the content moves with
/// resistance.
/// </para>
/// </summary>
public sealed class SwipeTracker(bool isAnimated)
{
    /// <summary>Travel that turns the page at once (instant mode), in device-independent pixels.</summary>
    public const double InstantThreshold = 40;

    /// <summary>Share of the width past which a released swipe completes (animated mode).</summary>
    public const double CommitFraction = 0.2;

    /// <summary>Speed, in pixels per second, of a flick that completes a short swipe.</summary>
    public const double FlickSpeed = 600;

    /// <summary>Share of the travel shown when there is no file in that direction.</summary>
    public const double EdgeResistance = 0.25;

    private bool _didPage;

    public bool IsAnimated { get; } = isAnimated;

    public double Travel { get; private set; }

    /// <summary>+1 for the next file (content moved left), -1 for the previous one.</summary>
    public int Direction => Travel < 0 ? 1 : -1;

    /// <summary>Adds travel. Instant mode returns the direction to page to, once per gesture, when allowed.</summary>
    public int? Move(double delta, Func<int, bool> canPage)
    {
        ArgumentNullException.ThrowIfNull(canPage);
        Travel += delta;
        if (IsAnimated || _didPage || Math.Abs(Travel) <= InstantThreshold)
        {
            return null;
        }
        _didPage = true;
        return canPage(Direction) ? Direction : null;
    }

    /// <summary>Where the content sits while it follows the pointer (animated mode).</summary>
    public double Offset(Func<int, bool> canPage)
    {
        ArgumentNullException.ThrowIfNull(canPage);
        return canPage(Direction) ? Travel : Travel * EdgeResistance;
    }

    /// <summary>
    /// The release: the direction to complete the page to, or null to spring back (always null when instant).
    /// <paramref name="velocity"/> is the content's speed in pixels per second, negative to the left.
    /// </summary>
    public int? End(double width, double velocity, Func<int, bool> canPage)
    {
        ArgumentNullException.ThrowIfNull(canPage);
        try
        {
            if (!IsAnimated || Travel == 0)
            {
                return null;
            }
            var isFar = Math.Abs(Travel) > width * CommitFraction;
            var isFlick = Math.Abs(velocity) > FlickSpeed && (velocity < 0) == (Travel < 0);
            return (isFar || isFlick) && canPage(Direction) ? Direction : null;
        }
        finally
        {
            Travel = 0;
            _didPage = false;
        }
    }
}
