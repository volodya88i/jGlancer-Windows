using Avalonia.Controls;
using JGlancer.App.Library;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;

namespace JGlancer.App.SizeMap;

/// <summary>
/// The size map module (macOS SizeMapModule, D-127): its header ("SIZE MAP", the hovered item's or the folder's name ·
/// size, Move and ×) over the map, or a message for an album or a folder without files of the query.
/// </summary>
internal sealed partial class SizeMapModule : UserControl
{
    private SizeMapModel? _model;
    private SizeTree.Node? _hovered;

    public SizeMapModule()
    {
        InitializeComponent();
        Canvas.HoverChanged += (_, node) =>
        {
            _hovered = node;
            UpdateInfo();
        };
        Canvas.FileClicked += (_, id) => RevealRequested?.Invoke(this, id);
        Canvas.DoubleClicked += (_, item) =>
        {
            if (_model?.Library is not { } library)
            {
                return;
            }
            if (item.IsFolder)
            {
                library.SelectFolder(item.Id);
            }
            else
            {
                library.Open(item.Id);
            }
        };
    }

    /// <summary>A file was clicked: the window selects it in the files, if they show it.</summary>
    public event EventHandler<long>? RevealRequested;

    public void Attach(SizeMapModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        HeaderButtons.Library = model.Library;
        model.Changed += (_, _) => Update();
        model.Library.Selection.Changed += (_, _) => Canvas.Selected = model.Library.Selection.Selected.ToHashSet();
        Update();
    }

    /// <summary>The place it is in (its Move button goes to the other).</summary>
    public ModuleSlot Slot
    {
        get => HeaderButtons.Slot;
        set => HeaderButtons.Slot = value;
    }

    /// <summary>Shown or hidden: the map is made only while it shows.</summary>
    public bool IsActive
    {
        get => _model?.IsActive == true;
        set
        {
            if (_model is not null && _model.IsActive != value)
            {
                _model.IsActive = value;
            }
        }
    }

    private void Update()
    {
        if (_model is not { } model)
        {
            return;
        }
        _hovered = null;
        Canvas.Tree = model.Tree;
        Canvas.Selected = model.Library.Selection.Selected.ToHashSet();
        var message = model.State switch
        {
            SizeMapModel.Content.Album => L.T("Albums have no size map: choose a folder."),
            SizeMapModel.Content.Empty => L.T("Nothing here."),
            _ => null,
        };
        MessageText.Text = message;
        MessageText.IsVisible = message is not null;
        Canvas.IsVisible = message is null;
        UpdateInfo();
    }

    /// <summary>The node under the pointer, else the folder shown: its name and size.</summary>
    private void UpdateInfo()
    {
        var node = _hovered ?? _model?.Tree.Root;
        InfoText.Text = node is null ? "" : node.Name + " · " + ByteFormat.Format(node.Size);
    }
}
