using System.Runtime.InteropServices;

namespace JGlancer.Mpv;

/// <summary>
/// The parts of libmpv's C API the app uses (client.h, render.h, render_gl.h of the pinned build). Strings are UTF-8.
/// Layouts and constants are taken from those headers.
/// </summary>
internal static unsafe partial class MpvNative
{
    private const string Library = "libmpv-2";

    // mpv_format
    public const int FormatNone = 0;
    public const int FormatString = 1;
    public const int FormatFlag = 3;
    public const int FormatInt64 = 4;
    public const int FormatDouble = 5;

    // mpv_event_id
    public const int EventNone = 0;
    public const int EventShutdown = 1;
    public const int EventStartFile = 6;
    public const int EventEndFile = 7;
    public const int EventFileLoaded = 8;
    public const int EventVideoReconfig = 17;
    public const int EventSeek = 20;
    public const int EventPlaybackRestart = 21;
    public const int EventPropertyChange = 22;

    // mpv_end_file_reason
    public const int EndFileEof = 0;
    public const int EndFileStop = 2;
    public const int EndFileQuit = 3;
    public const int EndFileError = 4;

    // mpv_render_param_type
    public const int RenderParamInvalid = 0;
    public const int RenderParamApiType = 1;
    public const int RenderParamOpenGlInitParams = 2;
    public const int RenderParamOpenGlFbo = 3;
    public const int RenderParamFlipY = 4;
    public const int RenderParamBlockForTargetTime = 12;

    public const ulong RenderUpdateFrame = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct Event
    {
        public int EventId;
        public int Error;
        public ulong ReplyUserdata;
        public void* Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyData
    {
        public byte* Name;
        public int Format;
        public void* Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EndFileData
    {
        public int Reason;
        public int Error;
        public long PlaylistEntryId;
        public long PlaylistInsertId;
        public int PlaylistInsertNumEntries;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RenderParam
    {
        public int Type;
        public void* Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OpenGlInitParams
    {
        public delegate* unmanaged<void*, byte*, void*> GetProcAddress;
        public void* GetProcAddressContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OpenGlFbo
    {
        public int Fbo;
        public int Width;
        public int Height;
        public int InternalFormat;
    }

    [LibraryImport(Library, EntryPoint = "mpv_client_api_version")]
    public static partial ulong ClientApiVersion();

    [LibraryImport(Library, EntryPoint = "mpv_create")]
    public static partial nint Create();

    [LibraryImport(Library, EntryPoint = "mpv_initialize")]
    public static partial int Initialize(nint handle);

    [LibraryImport(Library, EntryPoint = "mpv_terminate_destroy")]
    public static partial void TerminateDestroy(nint handle);

    [LibraryImport(Library, EntryPoint = "mpv_error_string")]
    public static partial byte* ErrorString(int error);

    [LibraryImport(Library, EntryPoint = "mpv_set_option_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetOptionString(nint handle, string name, string value);

    [LibraryImport(Library, EntryPoint = "mpv_command")]
    public static partial int Command(nint handle, byte** args);

    [LibraryImport(Library, EntryPoint = "mpv_command_async")]
    public static partial int CommandAsync(nint handle, ulong replyUserdata, byte** args);

    [LibraryImport(Library, EntryPoint = "mpv_set_property", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetProperty(nint handle, string name, int format, void* data);

    [LibraryImport(Library, EntryPoint = "mpv_set_property_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetPropertyString(nint handle, string name, string value);

    [LibraryImport(Library, EntryPoint = "mpv_get_property", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int GetProperty(nint handle, string name, int format, void* data);

    [LibraryImport(Library, EntryPoint = "mpv_get_property_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial byte* GetPropertyString(nint handle, string name);

    [LibraryImport(Library, EntryPoint = "mpv_free")]
    public static partial void Free(void* data);

    [LibraryImport(Library, EntryPoint = "mpv_observe_property", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int ObserveProperty(nint handle, ulong replyUserdata, string name, int format);

    [LibraryImport(Library, EntryPoint = "mpv_wait_event")]
    public static partial Event* WaitEvent(nint handle, double timeout);

    [LibraryImport(Library, EntryPoint = "mpv_wakeup")]
    public static partial void Wakeup(nint handle);

    [LibraryImport(Library, EntryPoint = "mpv_render_context_create")]
    public static partial int RenderContextCreate(nint* context, nint handle, RenderParam* parameters);

    [LibraryImport(Library, EntryPoint = "mpv_render_context_set_update_callback")]
    public static partial void RenderContextSetUpdateCallback(nint context, delegate* unmanaged<void*, void> callback, void* callbackContext);

    [LibraryImport(Library, EntryPoint = "mpv_render_context_update")]
    public static partial ulong RenderContextUpdate(nint context);

    [LibraryImport(Library, EntryPoint = "mpv_render_context_render")]
    public static partial int RenderContextRender(nint context, RenderParam* parameters);

    [LibraryImport(Library, EntryPoint = "mpv_render_context_report_swap")]
    public static partial void RenderContextReportSwap(nint context);

    [LibraryImport(Library, EntryPoint = "mpv_render_context_free")]
    public static partial void RenderContextFree(nint context);

    /// <summary>A UTF-8 string from mpv (not freed).</summary>
    public static string? Text(byte* value) => value is null ? null : Marshal.PtrToStringUTF8((nint)value);

    /// <summary>Whether the native library can be loaded (scripts\deps.ps1 fetched it).</summary>
    public static bool IsAvailable
    {
        get
        {
            try
            {
                return ClientApiVersion() > 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }
}
