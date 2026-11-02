using System.Runtime.InteropServices;

namespace JGlancer.Mpv;

/// <summary>
/// mpv's OpenGL render API for one player (render.h, render_gl.h): the app draws frames into its own GL framebuffer,
/// so video sits inside the UI's composition and overlays draw on top of it. Create, render and dispose on the GL
/// thread with its context current. <see cref="FrameReady"/> comes from an mpv thread.
/// </summary>
public sealed unsafe class MpvRenderContext : IDisposable
{
    private readonly Func<string, nint> _getProcAddress;
    private readonly GCHandle _self;
    private nint _context;

    /// <param name="getProcAddress">Looks up GL functions in the current context.</param>
    public MpvRenderContext(MpvPlayer player, Func<string, nint> getProcAddress)
    {
        ArgumentNullException.ThrowIfNull(player);
        _getProcAddress = getProcAddress;
        _self = GCHandle.Alloc(this);
        var apiType = Marshal.StringToCoTaskMemUTF8("opengl");
        try
        {
            var init = new MpvNative.OpenGlInitParams
            {
                GetProcAddress = &GetProcAddress,
                GetProcAddressContext = (void*)GCHandle.ToIntPtr(_self),
            };
            var parameters = stackalloc MpvNative.RenderParam[3];
            parameters[0] = new MpvNative.RenderParam { Type = MpvNative.RenderParamApiType, Data = (void*)apiType };
            parameters[1] = new MpvNative.RenderParam { Type = MpvNative.RenderParamOpenGlInitParams, Data = &init };
            parameters[2] = default;
            nint context;
            var result = MpvNative.RenderContextCreate(&context, player.Handle, parameters);
            if (result < 0)
            {
                _self.Free();
                throw new InvalidOperationException("mpv_render_context_create: " + MpvPlayer.ErrorText(result));
            }
            _context = context;
        }
        finally
        {
            Marshal.FreeCoTaskMem(apiType);
        }
        MpvNative.RenderContextSetUpdateCallback(_context, &OnUpdate, (void*)GCHandle.ToIntPtr(_self));
    }

    /// <summary>A new frame (or a redraw) is wanted; raised on an mpv thread.</summary>
    public event Action? FrameReady;

    /// <summary>True when mpv has a new frame to draw (call before <see cref="Render"/>).</summary>
    public bool Update() => _context != 0 && (MpvNative.RenderContextUpdate(_context) & MpvNative.RenderUpdateFrame) != 0;

    /// <summary>Draws the current frame into framebuffer <paramref name="fbo"/> of <paramref name="width"/> × <paramref name="height"/>.</summary>
    /// <returns>False when mpv could not draw (no frame yet, or an error).</returns>
    public bool Render(int fbo, int width, int height, bool flipY)
    {
        if (_context == 0)
        {
            return false;
        }
        var target = new MpvNative.OpenGlFbo { Fbo = fbo, Width = width, Height = height, InternalFormat = 0 };
        var flip = flipY ? 1 : 0;
        // Never wait for the frame's display time on the UI's render thread.
        var block = 0;
        var parameters = stackalloc MpvNative.RenderParam[4];
        parameters[0] = new MpvNative.RenderParam { Type = MpvNative.RenderParamOpenGlFbo, Data = &target };
        parameters[1] = new MpvNative.RenderParam { Type = MpvNative.RenderParamFlipY, Data = &flip };
        parameters[2] = new MpvNative.RenderParam { Type = MpvNative.RenderParamBlockForTargetTime, Data = &block };
        parameters[3] = default;
        return MpvNative.RenderContextRender(_context, parameters) >= 0;
    }

    public void ReportSwap()
    {
        if (_context != 0)
        {
            MpvNative.RenderContextReportSwap(_context);
        }
    }

    [UnmanagedCallersOnly]
    private static void* GetProcAddress(void* context, byte* name)
    {
        var self = (MpvRenderContext)GCHandle.FromIntPtr((nint)context).Target!;
        return (void*)self._getProcAddress(MpvNative.Text(name) ?? "");
    }

    [UnmanagedCallersOnly]
    private static void OnUpdate(void* context)
    {
        if (GCHandle.FromIntPtr((nint)context).Target is MpvRenderContext self)
        {
            self.FrameReady?.Invoke();
        }
    }

    /// <summary>Frees the context (on the GL thread, before the player is disposed).</summary>
    public void Dispose()
    {
        if (_context == 0)
        {
            return;
        }
        MpvNative.RenderContextSetUpdateCallback(_context, null, null);
        MpvNative.RenderContextFree(_context);
        _context = 0;
        _self.Free();
    }
}
