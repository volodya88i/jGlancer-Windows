using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace JGlancer.Platform.Windows.Com;

// Source-generated COM interfaces (ComWrappers, NativeAOT-safe). The vtable order must match the SDK headers exactly;
// methods after the last one used are left out. Interfaces come back as raw pointers and are wrapped with
// ComInterop.Wrap (unique instances): only those can be released at once with ComInterop.Release; wrappers the
// generated marshaller creates for out parameters are shared and would keep files open until garbage collection.

/// <summary>propsys.h IPropertyStore.</summary>
[GeneratedComInterface]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, PropertyKey* key);

    [PreserveSig]
    int GetValue(PropertyKey* key, PropVariant* value);
}

/// <summary>wincodec.h IWICImagingFactory, up to CreateBitmapFlipRotator.</summary>
[GeneratedComInterface]
[Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IWICImagingFactory
{
    [PreserveSig]
    int CreateDecoderFromFilename(
        [MarshalAs(UnmanagedType.LPWStr)] string fileName, Guid* vendor, uint desiredAccess, int metadataOptions,
        nint* decoder);

    [PreserveSig]
    int CreateDecoderFromStream(nint stream, Guid* vendor, int metadataOptions, nint* decoder);

    [PreserveSig]
    int CreateDecoderFromFileHandle(nint file, Guid* vendor, int metadataOptions, nint* decoder);

    [PreserveSig]
    int CreateComponentInfo(Guid* component, nint* info);

    [PreserveSig]
    int CreateDecoder(Guid* container, Guid* vendor, nint* decoder);

    [PreserveSig]
    int CreateEncoder(Guid* container, Guid* vendor, nint* encoder);

    [PreserveSig]
    int CreatePalette(nint* palette);

    [PreserveSig]
    int CreateFormatConverter(nint* converter);

    [PreserveSig]
    int CreateBitmapScaler(nint* scaler);

    [PreserveSig]
    int CreateBitmapClipper(nint* clipper);

    [PreserveSig]
    int CreateBitmapFlipRotator(nint* flipRotator);

    [PreserveSig]
    int CreateStream(nint* stream);

    [PreserveSig]
    int CreateColorContext(nint* context);
}

/// <summary>wincodec.h IWICColorContext: an embedded ICC profile or an EXIF color space.</summary>
[GeneratedComInterface]
[Guid("3C613A02-34B2-44ea-9A7C-45AEA9C6FD6D")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IWICColorContext
{
    [PreserveSig]
    int InitializeFromFilename([MarshalAs(UnmanagedType.LPWStr)] string fileName);

    [PreserveSig]
    int InitializeFromMemory(byte* buffer, uint size);

    [PreserveSig]
    int InitializeFromExifColorSpace(uint value);

    /// <summary>1 = an ICC profile, 2 = an EXIF color space.</summary>
    [PreserveSig]
    int GetType(int* type);

    [PreserveSig]
    int GetProfileBytes(uint size, byte* buffer, uint* actual);

    [PreserveSig]
    int GetExifColorSpace(uint* value);
}

/// <summary>wincodec.h IWICFormatConverter.</summary>
[GeneratedComInterface]
[Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IWICFormatConverter : IWICBitmapSource
{
    [PreserveSig]
    int Initialize(IWICBitmapSource source, Guid* format, int dither, nint palette, double alphaThreshold, int paletteType);
}

/// <summary>wincodec.h IWICBitmapScaler.</summary>
[GeneratedComInterface]
[Guid("00000302-a8f2-4877-ba0a-fd2b6645fb94")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IWICBitmapScaler : IWICBitmapSource
{
    [PreserveSig]
    int Initialize(IWICBitmapSource source, uint width, uint height, int mode);
}

/// <summary>wincodec.h IWICBitmapFlipRotator.</summary>
[GeneratedComInterface]
[Guid("5009834F-2D6A-41ce-9E1B-17C5AFF7A782")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IWICBitmapFlipRotator : IWICBitmapSource
{
    [PreserveSig]
    int Initialize(IWICBitmapSource source, int options);
}

/// <summary>ShObjIdl_core.h SIZE as passed to IShellItemImageFactory.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeSize
{
    public int Width;
    public int Height;
}

/// <summary>ShObjIdl_core.h IShellItemImageFactory: the Windows thumbnail system (video frames, cover art, …).</summary>
[GeneratedComInterface]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IShellItemImageFactory
{
    [PreserveSig]
    int GetImage(NativeSize size, int flags, nint* bitmap);
}

/// <summary>ShObjIdl_core.h IAssocHandler: a program Windows offers for a file type (Open With).</summary>
[GeneratedComInterface]
[Guid("F04061AC-1659-4a3f-A954-775AA57FC083")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IAssocHandler
{
    [PreserveSig]
    int GetName(char** name);

    [PreserveSig]
    int GetUIName(char** name);

    [PreserveSig]
    int GetIconLocation(char** path, int* index);

    [PreserveSig]
    int IsRecommended();

    [PreserveSig]
    int MakeDefault(char* description);

    [PreserveSig]
    int Invoke(nint dataObject);
}

/// <summary>ShObjIdl_core.h IEnumAssocHandlers.</summary>
[GeneratedComInterface]
[Guid("973810ae-9599-4b88-9e4d-6ee98c9552da")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IEnumAssocHandlers
{
    [PreserveSig]
    int Next(uint count, nint* handlers, uint* fetched);
}

/// <summary>ShObjIdl_core.h IShellItemArray, up to GetItemAt.</summary>
[GeneratedComInterface]
[Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IShellItemArray
{
    [PreserveSig]
    int BindToHandler(nint bindContext, Guid* handler, Guid* interfaceId, nint* result);

    [PreserveSig]
    int GetPropertyStore(int flags, nint interfaceId, nint store);

    [PreserveSig]
    int GetPropertyDescriptionList(nint key, nint interfaceId, nint list);

    [PreserveSig]
    int GetAttributes(int flags, uint mask, nint attributes);

    [PreserveSig]
    int GetCount(uint* count);

    [PreserveSig]
    int GetItemAt(uint index, nint* item);
}

/// <summary>wincodec.h IWICBitmapDecoder.</summary>
[GeneratedComInterface]
[Guid("9EDDE9E7-8DEE-47ea-99DF-E6FAF2ED44BF")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IWICBitmapDecoder
{
    [PreserveSig]
    int QueryCapability(nint stream, uint* capability);

    [PreserveSig]
    int Initialize(nint stream, int options);

    [PreserveSig]
    int GetContainerFormat(Guid* format);

    [PreserveSig]
    int GetDecoderInfo(nint* info);

    [PreserveSig]
    int CopyPalette(nint palette);

    [PreserveSig]
    int GetMetadataQueryReader(nint* reader);

    [PreserveSig]
    int GetPreview(nint* source);

    [PreserveSig]
    int GetColorContexts(uint count, nint* contexts, uint* actualCount);

    [PreserveSig]
    int GetThumbnail(nint* thumbnail);

    [PreserveSig]
    int GetFrameCount(uint* count);

    [PreserveSig]
    int GetFrame(uint index, nint* frame);
}

/// <summary>wincodec.h IWICBitmapSource.</summary>
[GeneratedComInterface]
[Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IWICBitmapSource
{
    [PreserveSig]
    int GetSize(uint* width, uint* height);

    [PreserveSig]
    int GetPixelFormat(Guid* format);

    [PreserveSig]
    int GetResolution(double* dpiX, double* dpiY);

    [PreserveSig]
    int CopyPalette(nint palette);

    [PreserveSig]
    int CopyPixels(nint rect, uint stride, uint bufferSize, byte* buffer);
}

/// <summary>wincodec.h IWICBitmapFrameDecode.</summary>
[GeneratedComInterface]
[Guid("3B16811B-6A43-4ec9-A813-3D930C13B940")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IWICBitmapFrameDecode : IWICBitmapSource
{
    [PreserveSig]
    int GetMetadataQueryReader(nint* reader);

    /// <summary>Fills <paramref name="contexts"/> (made by the factory) with the frame's color contexts.</summary>
    [PreserveSig]
    int GetColorContexts(uint count, nint* contexts, uint* actual);
}

/// <summary>wincodec.h IWICMetadataQueryReader.</summary>
[GeneratedComInterface]
[Guid("30989668-E1C9-4597-B395-458EEDB808DF")]
[SupportedOSPlatform("windows")]
internal unsafe partial interface IWICMetadataQueryReader
{
    [PreserveSig]
    int GetContainerFormat(Guid* format);

    [PreserveSig]
    int GetLocation(uint maxLength, char* namespaceName, uint* actualLength);

    [PreserveSig]
    int GetMetadataByName([MarshalAs(UnmanagedType.LPWStr)] string name, PropVariant* value);
}
