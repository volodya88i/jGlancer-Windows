using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using JGlancer.Core.Localization;
using JGlancer.Design;

namespace JGlancer.App.FileOps;

/// <summary>
/// The file operations' toast at the bottom of the window (macOS ToastView, DESIGN.md §7): the message, Undo after an
/// operation, Stop and a progress line while a long batch runs. It disappears by itself (<see cref="JGMetrics.ToastSeconds"/>).
/// </summary>
internal sealed class ToastView : Border
{
    private readonly TextBlock _message = new() { Classes = { "body" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _undo = new() { Classes = { "tertiary" }, Content = L.T("Undo") };
    private readonly Button _stop = new() { Classes = { "tertiary" }, Content = L.T("Stop") };
    private readonly Button _action = new() { Classes = { "tertiary" } };
    private readonly Border _bar = new() { Height = JGMetrics.OutlineWidth * 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
    private FileOperationsModel? _model;

    public ToastView()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(JGSpacing.M, 0, JGSpacing.M, JGSpacing.L + JGMetrics.StatusBarHeight);
        MaxWidth = JGMetrics.ReadableTextWidth;
        CornerRadius = new CornerRadius(JGMetrics.RadiusLarge);
        BorderThickness = new Thickness(JGMetrics.OutlineWidth);
        ClipToBounds = true;
        BoxShadow = new BoxShadows(new BoxShadow { Blur = JGMetrics.ToastShadowRadius, Color = Color.FromArgb((byte)(255 * JGMetrics.ToastShadowOpacity), 0, 0, 0) });
        this[!BackgroundProperty] = this.GetResourceObservable(JGColor.BgRaised.ResourceKey).ToBinding();
        this[!BorderBrushProperty] = this.GetResourceObservable(JGColor.LineStrong.ResourceKey).ToBinding();
        _message[!TextBlock.ForegroundProperty] = this.GetResourceObservable(JGColor.TextPrimary.ResourceKey).ToBinding();
        _bar[!BackgroundProperty] = this.GetResourceObservable(JGColor.Accent.ResourceKey).ToBinding();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = JGSpacing.S, Margin = new Thickness(JGSpacing.M, JGSpacing.Xs) };
        row.Children.Add(_message);
        row.Children.Add(_action);
        row.Children.Add(_undo);
        row.Children.Add(_stop);
        Child = new Panel { Children = { row, _bar } };
        _undo.Click += async (_, _) =>
        {
            if (_model is { } model)
            {
                model.DismissToast();
                await model.UndoAsync();
            }
        };
        _stop.Click += (_, _) => _model?.Stop();
        _action.Click += (_, _) =>
        {
            if (_model?.Toast?.Action is { } action)
            {
                _model.DismissToast();
                action();
            }
        };
        IsVisible = false;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
    }

    public FileOperationsModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.ToastChanged -= OnToastChanged;
            }
            _model = value;
            if (_model is not null)
            {
                _model.ToastChanged += OnToastChanged;
            }
            Update();
        }
    }

    private void OnToastChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        if (_model?.Toast is not { } toast)
        {
            IsVisible = false;
            return;
        }
        IsVisible = true;
        _message.Text = toast.Message;
        AutomationProperties.SetName(this, toast.Message);
        _undo.IsVisible = toast.CanUndo;
        _action.IsVisible = toast.ActionLabel is not null;
        _action.Content = toast.ActionLabel;
        _stop.IsVisible = toast.Progress is not null;
        _bar.IsVisible = toast.Progress is not null;
        _bar.Width = Bounds.Width * (toast.Progress ?? 0);
    }
}
