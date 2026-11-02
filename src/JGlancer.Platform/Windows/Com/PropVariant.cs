using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGlancer.Platform.Windows.Com;

/// <summary>A property key (format id + property id), as in propkey.h.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PropertyKey(Guid FormatId, uint PropertyId);

/// <summary>
/// The PROPVARIANT layout for x64/arm64 (24 bytes): the type at offset 0, the value at 8. Only the types metadata
/// properties use are read; <see cref="Clear"/> frees whatever the callee allocated.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
[SupportedOSPlatform("windows")]
internal unsafe partial struct PropVariant
{
    public const ushort Empty = 0;
    public const ushort I2 = 2;
    public const ushort I4 = 3;
    public const ushort R4 = 4;
    public const ushort R8 = 5;
    public const ushort BStr = 8;
    public const ushort Bool = 11;
    public const ushort UI1 = 17;
    public const ushort UI2 = 18;
    public const ushort UI4 = 19;
    public const ushort I8 = 20;
    public const ushort UI8 = 21;
    public const ushort LPStr = 30;
    public const ushort LPWStr = 31;
    public const ushort FileTime = 64;
    public const ushort ClsId = 72;
    public const ushort VectorR8 = 0x1005;
    public const ushort VectorLPWStr = 0x101F;

    [FieldOffset(0)]
    public ushort Type;

    [FieldOffset(8)]
    public long Int64;

    [FieldOffset(8)]
    public double Double;

    [FieldOffset(8)]
    public float Single;

    [FieldOffset(8)]
    public nint Pointer;

    /// <summary>The element pointer of a vector (its count is the low half of <see cref="Int64"/>).</summary>
    [FieldOffset(16)]
    public nint VectorElements;

    /// <summary>A vector of strings (several artists); null for other types.</summary>
    public readonly string[]? Strings
    {
        get
        {
            if (Type != VectorLPWStr || VectorElements == 0)
            {
                return null;
            }
            var count = (int)(uint)Int64;
            var result = new string[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = Marshal.PtrToStringUni(((nint*)VectorElements)[i]) ?? "";
            }
            return result;
        }
    }

    /// <summary>A vector of doubles (GPS degrees, minutes, seconds); null for other types.</summary>
    public readonly double[]? Doubles => Type == VectorR8 && VectorElements != 0
        ? new ReadOnlySpan<double>((double*)VectorElements, (int)(uint)Int64).ToArray()
        : null;

    /// <summary>The value as a 64-bit integer, for any integer type; null otherwise.</summary>
    public readonly long? Integer => Type switch
    {
        UI1 => (byte)Int64,
        I2 => (short)Int64,
        UI2 => (ushort)Int64,
        I4 => (int)Int64,
        UI4 => (uint)Int64,
        I8 or UI8 => Int64,
        Bool => (short)Int64 != 0 ? 1 : 0,
        _ => null,
    };

    /// <summary>The value as a double, for integer and floating types; null otherwise.</summary>
    public readonly double? Number => Type switch
    {
        R8 => Double,
        R4 => Single,
        _ => Integer,
    };

    /// <summary>The value as text (ANSI and wide strings, GUIDs); null otherwise.</summary>
    public readonly string? Text => Type switch
    {
        LPWStr or BStr => Pointer == 0 ? null : Marshal.PtrToStringUni(Pointer),
        LPStr => Pointer == 0 ? null : Marshal.PtrToStringAnsi(Pointer),
        ClsId => Pointer == 0 ? null : (*(Guid*)Pointer).ToString("B").ToUpperInvariant(),
        _ => null,
    };

    /// <summary>A FILETIME value as UTC; null for other types.</summary>
    public readonly DateTimeOffset? Date => Type == FileTime && Int64 > 0 ? new DateTimeOffset(DateTime.FromFileTimeUtc(Int64), TimeSpan.Zero) : null;

    public override readonly string ToString() => Text ?? Date?.ToString("O") ?? Number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? $"vt={Type}";

    /// <summary>Frees strings and other memory owned by the variant and resets it to empty.</summary>
    public void Clear()
    {
        fixed (PropVariant* self = &this)
        {
            _ = PropVariantClear(self);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(PropVariant* variant);
}
