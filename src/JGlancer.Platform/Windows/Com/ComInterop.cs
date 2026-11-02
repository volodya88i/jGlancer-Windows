using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace JGlancer.Platform.Windows.Com;

/// <summary>Creating and releasing COM objects through source-generated ComWrappers.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class ComInterop
{
    private const uint ClsCtxInprocServer = 0x1;
    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>
    /// Releases the native object now instead of at garbage collection: decoders and property stores keep files
    /// open, and the user must be able to rename or delete them right after a scan.
    /// </summary>
    public static void Release(object? instance)
    {
        if (instance is ComObject comObject)
        {
            comObject.FinalRelease();
        }
    }

    /// <summary>CoCreateInstance of an in-process server as interface <typeparamref name="T"/>.</summary>
    public static T Create<T>(Guid classId, Guid interfaceId)
        where T : class
    {
        nint pointer;
        Marshal.ThrowExceptionForHR(CoCreateInstance(&classId, 0, ClsCtxInprocServer, &interfaceId, &pointer));
        return Wrap<T>(pointer);
    }

    /// <summary>Wraps a native interface pointer (taking over its reference) as <typeparamref name="T"/>.</summary>
    public static T Wrap<T>(nint pointer)
        where T : class
    {
        try
        {
            return (T)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    /// <summary>The property store of a file (Shell property handlers: video, audio and photo properties).</summary>
    public static IPropertyStore? PropertyStore(string path)
    {
        var interfaceId = typeof(IPropertyStore).GUID;
        nint pointer;
        var result = SHGetPropertyStoreFromParsingName(path, 0, GetPropertyStoreFlags.Default, &interfaceId, &pointer);
        return result < 0 || pointer == 0 ? null : Wrap<IPropertyStore>(pointer);
    }

    /// <summary>The canonical name of a property key (e.g. <c>System.Media.Duration</c>), for diagnostics.</summary>
    public static string? PropertyName(PropertyKey key)
    {
        char* name;
        if (PSGetNameFromPropertyKey(&key, &name) < 0 || name == null)
        {
            return null;
        }
        try
        {
            return new string(name);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)name);
        }
    }

    private enum GetPropertyStoreFlags
    {
        Default = 0,
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* classId, nint outer, uint context, Guid* interfaceId, nint* instance);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHGetPropertyStoreFromParsingName(string path, nint bindContext, GetPropertyStoreFlags flags, Guid* interfaceId, nint* store);

    [LibraryImport("propsys.dll")]
    private static partial int PSGetNameFromPropertyKey(PropertyKey* key, char** name);
}
