namespace JGlancer.Core.Model;

/// <summary>Index helpers for the viewer's pager (macOS Pager, ARCHITECTURE §8).</summary>
public static class Pager
{
    /// <summary>
    /// Indices to keep decoded around <paramref name="index"/>: itself first, then the nearest neighbors alternating
    /// forward and backward (<paramref name="radius"/> on each side), clamped to 0 ..&lt; count. Order is decode priority.
    /// </summary>
    public static IReadOnlyList<int> Window(int index, int count, int radius = 2)
    {
        if (count <= 0 || index < 0 || index >= count)
        {
            return [];
        }
        var result = new List<int> { index };
        for (var distance = 1; distance <= radius; distance++)
        {
            if (index + distance < count)
            {
                result.Add(index + distance);
            }
            if (index - distance >= 0)
            {
                result.Add(index - distance);
            }
        }
        return result;
    }

    /// <summary><paramref name="index"/> + <paramref name="offset"/> clamped to 0 ..&lt; count (no wrap-around).</summary>
    public static int Move(int index, int offset, int count) => count <= 0 ? 0 : Math.Clamp(index + offset, 0, count - 1);
}
