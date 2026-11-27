using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;
using JGlancer.Design;

namespace JGlancer.App.Library;

/// <summary>
/// A side module's header buttons (macOS D-127): move it to the other place (swapping with the module there), and
/// close it.
/// </summary>
internal sealed class ModuleHeaderButtons : StackPanel
{
    private readonly Button _move = new() { Classes = { "icon" } };
    private readonly JGIcon _moveGlyph = new();
    private readonly Button _close = new() { Classes = { "icon" } };
    private ModuleSlot _slot = ModuleSlot.Right;

    public ModuleHeaderButtons()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        _move.Content = _moveGlyph;
        _close.Content = new JGIcon { Data = JGIconData.X };
        _move.Click += (_, _) => Library?.MoveModule(Module);
        _close.Click += (_, _) => Library?.SetModuleVisible(Module, false);
        Children.Add(_move);
        Children.Add(_close);
        Update();
    }

    /// <summary>The module these buttons belong to.</summary>
    public LibraryModule Module
    {
        get;
        set
        {
            field = value;
            Update();
        }
    }

    /// <summary>The place the module is in now: Move goes to the other one.</summary>
    public ModuleSlot Slot
    {
        get => _slot;
        set
        {
            _slot = value;
            Update();
        }
    }

    /// <summary>The main library model (its view state holds the places).</summary>
    public LibraryModel? Library { get; set; }

    private void Update()
    {
        _moveGlyph.Data = _slot == ModuleSlot.Right ? JGIconData.PanelBottom : JGIconData.PanelRight;
        var move = _slot == ModuleSlot.Right ? L.T("Move Down") : L.T("Move to the Right");
        ToolTip.SetTip(_move, move);
        AutomationProperties.SetName(_move, move);
        var close = Module == LibraryModule.Preview ? L.T("Hide Preview") : L.T("Hide Size Map");
        ToolTip.SetTip(_close, close);
        AutomationProperties.SetName(_close, close);
    }
}
