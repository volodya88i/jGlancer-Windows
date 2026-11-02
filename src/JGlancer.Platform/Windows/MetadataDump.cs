using System.Runtime.Versioning;
using System.Text;
using JGlancer.Platform.Windows.Com;

namespace JGlancer.Platform.Windows;

/// <summary>Developer diagnostics: every Shell property of a file and the WIC facts the metadata reader looks at.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe class MetadataDump
{
    public static string Describe(string path)
    {
        var text = new StringBuilder();
        text.AppendLine(path);
        var store = ComInterop.PropertyStore(path);
        if (store is null)
        {
            text.AppendLine("  (no property store)");
        }
        else
        {
            try
            {
                store.GetCount(out var count);
                for (uint i = 0; i < count; i++)
                {
                    PropertyKey key;
                    if (store.GetAt(i, &key) < 0)
                    {
                        continue;
                    }
                    var value = default(PropVariant);
                    if (store.GetValue(&key, &value) >= 0 && value.Type != PropVariant.Empty)
                    {
                        text.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                            $"  {ComInterop.PropertyName(key) ?? $"{key.FormatId:B}/{key.PropertyId}"} = {value} (vt {value.Type})");
                    }
                    value.Clear();
                }
            }
            finally
            {
                ComInterop.Release(store);
            }
        }
        text.Append(WicPhotoReader.Describe(path));
        return text.ToString();
    }
}
