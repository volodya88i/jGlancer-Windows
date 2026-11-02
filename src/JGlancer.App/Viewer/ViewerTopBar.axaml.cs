using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using JGlancer.Design;

namespace JGlancer.App.Viewer;

/// <summary>The viewers' top bar: "‹ Library", the name (shortened in the middle), "12 / 340", trailing controls.</summary>
internal sealed partial class ViewerTopBar : UserControl
{
    public ViewerTopBar()
    {
        InitializeComponent();
        // Alt held as "‹ Library" is clicked: the audio viewer hands its song to the mini player (macOS D-92).
        BackButton.AddHandler(PointerPressedEvent, (_, e) => _altOnPress = e.KeyModifiers.HasFlag(KeyModifiers.Alt),
            RoutingStrategies.Tunnel, handledEventsToo: true);
        BackButton.Click += (_, _) =>
        {
            IsAltBack = _altOnPress;
            _altOnPress = false;
            BackRequested?.Invoke(this, EventArgs.Empty);
            IsAltBack = false;
        };
        AddFolderButton.Click += (_, _) => AddFolderRequested?.Invoke(this, EventArgs.Empty);
        ToolTip.SetTip(AddFolderButton, JGlancer.Core.Localization.L.T("This folder isn't in the library."));
        BarGrid.SizeChanged += (_, _) => FitName();
        TrailingHost.SizeChanged += (_, _) => FitName();
        // The window knows the file shown and runs the action.
        InfoButton.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Shell.MainWindow window)
            {
                await window.PerformAsync(Core.Actions.ActionId.GetInfo);
            }
        };
    }

    /// <summary>"‹ Library".</summary>
    public event EventHandler? BackRequested;

    /// <summary>During <see cref="BackRequested"/>: Alt was held as the button was clicked.</summary>
    public bool IsAltBack { get; private set; }

    private bool _altOnPress;

    /// <summary>The viewer's own controls at the right end.</summary>
    public object? Trailing
    {
        get => TrailingHost.Content;
        set => TrailingHost.Content = value;
    }

    /// <summary>Shows <paramref name="name"/> and, for a folder of more than one file, its position.</summary>
    /// <param name="assetId">The library file shown (its flag star); null outside the library.</param>
    /// <summary>"Add Folder": the file shown is outside the library.</summary>
    public event EventHandler? AddFolderRequested;

    public bool AddFolderVisible
    {
        get => AddFolderButton.IsVisible;
        set
        {
            AddFolderButton.IsVisible = value;
            Avalonia.Threading.Dispatcher.UIThread.Post(FitName, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    public void Show(string name, int index, int count, long? assetId)
    {
        NameText.Text = name;
        Star.AssetId = assetId;
        PositionText.Text = count > 1 ? string.Create(CultureInfo.InvariantCulture, $"{index + 1} / {count}") : "";
        // Before the first layout the bar has no width yet: fit once it has.
        NameText.MaxWidth = double.PositiveInfinity;
        Avalonia.Threading.Dispatcher.UIThread.Post(FitName, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The name takes what the bar has left after the back button, the position and the trailing controls, and
    /// shortens in the middle beyond that; the position stays right after it (macOS truncationMode(.middle)).
    /// </summary>
    private void FitName()
    {
        if (BarGrid.Bounds.Width <= 0)
        {
            return;
        }
        AfterName.Measure(Size.Infinity);
        var used = BackButton.Bounds.Width + AfterName.DesiredSize.Width + (AddFolderButton.IsVisible ? AddFolderButton.Bounds.Width : 0) + TrailingHost.Bounds.Width + InfoButton.Bounds.Width
            + NameText.Margin.Left + NameText.Margin.Right + JGSpacing.M;
        NameText.MaxWidth = Math.Max(0, BarGrid.Bounds.Width - used);
    }
}
