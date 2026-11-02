using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using JGlancer.Mpv;

namespace JGlancer.App.Viewer.Media;

/// <summary>
/// The video picture (W-33): mpv draws each frame into this control's OpenGL framebuffer (Avalonia's GL on ANGLE),
/// so the picture is part of the window's composition and the viewer's controls lie over it like on macOS. A frame
/// is drawn only when mpv has one (its update callback asks for it): a paused video costs nothing.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The render context lives on the GL thread and is freed in OnOpenGlDeinit / OnOpenGlLost.")]
internal sealed class MpvVideoView : OpenGlControlBase
{
    private MpvRenderContext? _render;
    private TaskCompletionSource _released = Completed();
    private readonly Lock _gate = new();
    private int _renders;
    private DispatcherTimer? _watchdog;
    private int _rendersAtSignal;

    /// <summary>
    /// How long a signalled frame may wait for Avalonia's render pass. mpv sends its next "frame ready" only after the
    /// last one was acknowledged (render context update); while the picture is off screen (swiped aside, hidden)
    /// Avalonia may skip the pass, and the video would freeze for good. After this wait the app acknowledges itself.
    /// </summary>
    private static readonly TimeSpan FrameWait = TimeSpan.FromMilliseconds(80);

    public MpvVideoView(MpvPlayer player)
    {
        Player = player;
    }

    public MpvPlayer Player { get; }

    /// <summary>
    /// Completes while no render context exists (none yet, or freed): only then may the player go. The control may be
    /// moved to another window (the previews, W-41): it frees its context on leaving and makes a new one on arriving.
    /// </summary>
    public Task Released => _released.Task;

    /// <summary>mpv has a render context: files may load now (UI thread). Raised again after every move.</summary>
    public event EventHandler? RenderReady;

    /// <summary>The GL setup failed: the viewer shows a message instead of a black picture.</summary>
    public event EventHandler? Unavailable;

    /// <summary>A new frame was drawn (on the GL thread; `--switch-check`).</summary>
    public event Action? FrameDrawn;

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        _released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _render = new MpvRenderContext(Player, gl.GetProcAddress);
            _render.FrameReady += () => Dispatcher.UIThread.Post(OnFrameReady, DispatcherPriority.Render);
            Dispatcher.UIThread.Post(() => RenderReady?.Invoke(this, EventArgs.Empty));
        }
        catch (InvalidOperationException)
        {
            _render = null;
            _released.TrySetResult();
            Dispatcher.UIThread.Post(() => Unavailable?.Invoke(this, EventArgs.Empty));
        }
    }

    /// <summary>A frame is ready (UI thread): draw it, and make sure it gets acknowledged even if no pass comes.</summary>
    private void OnFrameReady()
    {
        RequestNextFrameRendering();
        _rendersAtSignal = Volatile.Read(ref _renders);
        _watchdog ??= new DispatcherTimer(FrameWait, DispatcherPriority.Background, (_, _) => FrameWaited());
        _watchdog.Stop();
        _watchdog.Start();
    }

    private void FrameWaited()
    {
        _watchdog!.Stop();
        if (Volatile.Read(ref _renders) != _rendersAtSignal)
        {
            return;
        }
        // No render pass came (off screen): acknowledge the frame so mpv goes on signalling, and ask again.
        lock (_gate)
        {
            _render?.Update();
        }
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        lock (_gate)
        {
            if (_render is null)
            {
                return;
            }
            Interlocked.Increment(ref _renders);
            var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            var width = Math.Max(1, (int)(Bounds.Width * scaling));
            var height = Math.Max(1, (int)(Bounds.Height * scaling));
            var isNew = _render.Update();
            _render.Render(fb, width, height, flipY: true);
            if (isNew)
            {
                FrameDrawn?.Invoke();
            }
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        lock (_gate)
        {
            _render?.Dispose();
            _render = null;
        }
        _released.TrySetResult();
    }

    protected override void OnOpenGlLost()
    {
        lock (_gate)
        {
            _render?.Dispose();
            _render = null;
        }
        _released.TrySetResult();
        base.OnOpenGlLost();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // Without a GL init there is no deinit; nothing holds the player then.
        if (_render is null)
        {
            _released.TrySetResult();
        }
    }
}
