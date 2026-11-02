using System.Text.Json;
using System.Text.Json.Serialization;
using JGlancer.Core.Model;

namespace JGlancer.Core.Actions;

/// <summary>A program the user chose for Open With (FEATURES §9): its executable and the name shown.</summary>
public sealed record ExternalApp(string Path, string Name)
{
    /// <summary>Programs are the same when their executables are (paths are case-insensitive on Windows).</summary>
    public bool IsSame(ExternalApp other) =>
        other is not null && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Open With settings (macOS OpenWithSettings, ARCHITECTURE §16): per kind any number of programs and one primary.
/// The first program added to a kind becomes its primary; removing the primary promotes the next one.
/// </summary>
public sealed class OpenWithSettings
{
    /// <summary>Per kind, in the order added (JSON).</summary>
    public Dictionary<MediaKind, List<ExternalApp>> Apps { get; set; } = [];

    /// <summary>Per kind, the primary program's path (JSON).</summary>
    public Dictionary<MediaKind, string> Primaries { get; set; } = [];

    /// <summary>The kind's programs, primary first, then in the order they were added.</summary>
    public IReadOnlyList<ExternalApp> AppsFor(MediaKind kind)
    {
        var list = Apps.GetValueOrDefault(kind) ?? [];
        return Primary(kind) is { } primary ? [primary, .. list.Where(app => !app.IsSame(primary))] : list;
    }

    public ExternalApp? Primary(MediaKind kind) =>
        Primaries.TryGetValue(kind, out var path) && Apps.TryGetValue(kind, out var list)
            ? list.FirstOrDefault(app => string.Equals(app.Path, path, StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>Kinds that have a primary program (for "Edit in …").</summary>
    [JsonIgnore]
    public IReadOnlySet<MediaKind> KindsWithPrimary =>
        new[] { MediaKind.Photo, MediaKind.Video, MediaKind.Audio }.Where(kind => Primary(kind) is not null).ToHashSet();

    /// <summary>Adds <paramref name="app"/> for <paramref name="kind"/> (one already there is updated in place).</summary>
    public void Add(ExternalApp app, MediaKind kind)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!Apps.TryGetValue(kind, out var list))
        {
            list = [];
            Apps[kind] = list;
        }
        var index = list.FindIndex(existing => existing.IsSame(app));
        if (index >= 0)
        {
            list[index] = app;
        }
        else
        {
            list.Add(app);
        }
        Primaries.TryAdd(kind, app.Path);
    }

    public void Remove(string path, MediaKind kind)
    {
        if (Apps.TryGetValue(kind, out var list))
        {
            list.RemoveAll(app => string.Equals(app.Path, path, StringComparison.OrdinalIgnoreCase));
        }
        if (Primaries.TryGetValue(kind, out var primary) && string.Equals(primary, path, StringComparison.OrdinalIgnoreCase))
        {
            if (list?.FirstOrDefault() is { } next)
            {
                Primaries[kind] = next.Path;
            }
            else
            {
                Primaries.Remove(kind);
            }
        }
    }

    public void SetPrimary(string path, MediaKind kind)
    {
        if (Apps.TryGetValue(kind, out var list) && list.Any(app => string.Equals(app.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            Primaries[kind] = path;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, OpenWithJson.Default.OpenWithSettings);

    /// <summary>Reads saved settings; anything unreadable gives none.</summary>
    public static OpenWithSettings FromJson(string? json)
    {
        try
        {
            return json is null ? new() : JsonSerializer.Deserialize(json, OpenWithJson.Default.OpenWithSettings) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}

/// <summary>Source-generated JSON (NativeAOT: no reflection).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OpenWithSettings))]
internal sealed partial class OpenWithJson : JsonSerializerContext;
