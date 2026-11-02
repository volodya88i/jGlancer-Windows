using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using JGlancer.Core.Localization;
using JGlancer.Core.Model;

namespace JGlancer.App.Viewer;

/// <summary>
/// Compare two photos (W-77): side by side with their names, sizes and dimensions; zoom
/// and pan in one and the other follows (the same part of each, whatever their sizes) while linked. L links and
/// unlinks, X swaps the sides, Ctrl+0 / Ctrl+1 / Ctrl+9 / Ctrl+= / Ctrl+- zoom, double-click toggles fit and 100 %,
/// Esc or "‹ Library" closes.
/// </summary>
internal sealed partial class CompareView : UserControl
{
    private CompareModel? _model;
    private bool _linked = true;
    private bool _following;

    public CompareView()
    {
        InitializeComponent();
        TopBar.BackRequested += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        LinkButton.Click += (_, _) => ToggleLink();
        SwapButton.Click += (_, _) => _model?.Swap();
        FitButton.Click += (_, _) => Zoom(ZoomCommand.Fit);
        ActualSizeButton.Click += (_, _) => Zoom(ZoomCommand.ActualSize);
        ToolTip.SetTip(SwapButton, $"{L.T("Swap Sides")} (X)");
        AutomationProperties.SetName(SwapButton, L.T("Swap Sides"));
        LeftCanvas.ViewChanged += (_, _) => Follow(LeftCanvas, RightCanvas);
        RightCanvas.ViewChanged += (_, _) => Follow(RightCanvas, LeftCanvas);
        LeftCanvas.ZoomedBeyondFit += (_, _) => _model?.RequestFullResolution(0);
        RightCanvas.ZoomedBeyondFit += (_, _) => _model?.RequestFullResolution(1);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        UpdateLink();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Focus(), Avalonia.Threading.DispatcherPriority.Input);
    }

    /// <summary>Esc or "‹ Library".</summary>
    public event EventHandler? CloseRequested;

    public CompareModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.Changed -= OnModelChanged;
            }
            _model = value;
            if (_model is not null)
            {
                _model.Changed += OnModelChanged;
            }
            TopBar.Show(L.T("Compare"), 0, 1, null);
            OnModelChanged(this, EventArgs.Empty);
        }
    }

    /// <summary>The compared photos, left first (the window's actions apply to both).</summary>
    public IReadOnlyList<Asset> Assets => _model is { } model ? [model.Asset(0), model.Asset(1)] : [];

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (_model is not { } model)
        {
            return;
        }
        var (left, right) = (model.Asset(0), model.Asset(1));
        LeftCanvas.Display(left.Id, model.Picture(0), 0);
        RightCanvas.Display(right.Id, model.Picture(1), 0);
        (LeftName.Text, LeftFacts.Text) = (left.Name, Facts(left, model.Picture(0)));
        (RightName.Text, RightFacts.Text) = (right.Name, Facts(right, model.Picture(1)));
        if (_linked)
        {
            // A refined picture keeps the zoom; the other side shows the same part again.
            RightCanvas.ApplyLinkedView(LeftCanvas.LinkedView);
        }
    }

    /// <summary>"6000 × 4000 · 12.3 MB" (the decoded size once known; the library's until then).</summary>
    private static string Facts(Asset asset, ViewerPicture? picture)
    {
        var (width, height) = picture is { IsPlaceholder: false } decoded
            ? (decoded.FullWidth, decoded.FullHeight)
            : (asset.Width ?? 0, asset.Height ?? 0);
        var size = ByteFormat.Format(asset.Size);
        return width > 0 && height > 0 ? string.Create(CultureInfo.InvariantCulture, $"{width} × {height} · ") + size : size;
    }

    private void Follow(PhotoCanvas from, PhotoCanvas to)
    {
        if (!_linked || _following)
        {
            return;
        }
        _following = true;
        try
        {
            to.ApplyLinkedView(from.LinkedView);
        }
        finally
        {
            _following = false;
        }
    }

    private void ToggleLink()
    {
        _linked = !_linked;
        UpdateLink();
        if (_linked)
        {
            RightCanvas.ApplyLinkedView(LeftCanvas.LinkedView);
        }
    }

    private void UpdateLink()
    {
        LinkButton.Classes.Set("on", _linked);
        var label = L.T(_linked ? "Unlink Zoom" : "Link Zoom");
        ToolTip.SetTip(LinkButton, $"{label} (L)");
        AutomationProperties.SetName(LinkButton, label);
    }

    /// <summary>Zooms the side with the keyboard focus (the left one otherwise); a linked other side follows.</summary>
    private void Zoom(ZoomCommand command) => (RightCanvas.IsFocused ? RightCanvas : LeftCanvas).Zoom(command);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var control = e.KeyModifiers == KeyModifiers.Control;
        ZoomCommand? zoom = control ? e.Key switch
        {
            Key.D0 or Key.NumPad0 => ZoomCommand.Fit,
            Key.D9 or Key.NumPad9 => ZoomCommand.Fill,
            Key.D1 or Key.NumPad1 => ZoomCommand.ActualSize,
            Key.OemPlus or Key.Add => ZoomCommand.ZoomIn,
            Key.OemMinus or Key.Subtract => ZoomCommand.ZoomOut,
            _ => null,
        } : null;
        if (zoom is { } command)
        {
            Zoom(command);
        }
        else if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (e.Key == Key.L)
        {
            ToggleLink();
        }
        else if (e.Key == Key.X)
        {
            _model?.Swap();
        }
        else
        {
            return;
        }
        e.Handled = true;
    }
}
