namespace JGlancer.Core.Model;

/// <summary>The library window's two places for side modules (macOS D-127): right of the files, or under them.</summary>
public enum ModuleSlot
{
    Right,
    Bottom,
}

/// <summary>The side modules: the selection's preview and the size map.</summary>
public enum LibraryModule
{
    Preview,
    SizeMap,
}

/// <summary>
/// The rules of the two places (macOS D-127): a module shown where the other one is takes the free place instead;
/// moving a module to the place of the other one swaps them.
/// </summary>
public static class ModuleDock
{
    public static ModuleSlot Other(this ModuleSlot slot) => slot == ModuleSlot.Right ? ModuleSlot.Bottom : ModuleSlot.Right;

    /// <summary>Where <paramref name="module"/> is; null while it is hidden.</summary>
    public static ModuleSlot? SlotOf(this LibraryViewState state, LibraryModule module)
    {
        ArgumentNullException.ThrowIfNull(state);
        return module switch
        {
            LibraryModule.Preview => state.PreviewVisible ? state.PreviewSlot : null,
            _ => state.SizeMapVisible ? state.SizeMapSlot : null,
        };
    }

    /// <summary>The module shown in <paramref name="slot"/>, if any.</summary>
    public static LibraryModule? ModuleIn(this LibraryViewState state, ModuleSlot slot) =>
        state.SlotOf(LibraryModule.Preview) == slot ? LibraryModule.Preview
        : state.SlotOf(LibraryModule.SizeMap) == slot ? LibraryModule.SizeMap
        : null;

    public static bool IsVisible(this LibraryViewState state, LibraryModule module) => state.SlotOf(module) is not null;

    /// <summary>Shows or hides <paramref name="module"/>; shown where the other one is, it takes the free place.</summary>
    public static void SetVisible(this LibraryViewState state, LibraryModule module, bool isVisible)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (module == LibraryModule.Preview)
        {
            state.PreviewVisible = isVisible;
            if (isVisible && state.SizeMapVisible && state.SizeMapSlot == state.PreviewSlot)
            {
                state.PreviewSlot = state.SizeMapSlot.Other();
            }
        }
        else
        {
            state.SizeMapVisible = isVisible;
            if (isVisible && state.PreviewVisible && state.PreviewSlot == state.SizeMapSlot)
            {
                state.SizeMapSlot = state.PreviewSlot.Other();
            }
        }
    }

    /// <summary>Moves <paramref name="module"/> to the other place; the module there, if any, takes its place.</summary>
    public static void Move(this LibraryViewState state, LibraryModule module)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (module == LibraryModule.Preview)
        {
            var target = state.PreviewSlot.Other();
            if (state.SizeMapVisible && state.SizeMapSlot == target)
            {
                state.SizeMapSlot = state.PreviewSlot;
            }
            state.PreviewSlot = target;
        }
        else
        {
            var target = state.SizeMapSlot.Other();
            if (state.PreviewVisible && state.PreviewSlot == target)
            {
                state.PreviewSlot = state.SizeMapSlot;
            }
            state.SizeMapSlot = target;
        }
    }
}
