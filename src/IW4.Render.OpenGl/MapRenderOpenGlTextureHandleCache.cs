using Silk.NET.OpenGL;
using IW4.Render.Textures;
using Texture = IW4.Render.Textures.Texture;
using TextureTarget = Silk.NET.OpenGL.TextureTarget;

namespace IW4.Render.OpenGl;

/// <summary>
/// Stable OpenGL texture-object ownership plus mutable storage residency.
/// Eviction never deletes a handle: draw resources retain valid identities
/// while the renderer replaces only that object's image storage.
/// </summary>
internal sealed class MapRenderOpenGlTextureHandleCache
{
    private readonly Dictionary<
        Texture,
        MapRenderOpenGlTextureResidencyEntry> _entriesByTexture =
            new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<
        uint,
        MapRenderOpenGlTextureResidencyEntry> _entriesByHandle = [];
    private long _nextCreationOrdinal;

    internal IEnumerable<uint> Handles =>
        _entriesByHandle.Keys;

    internal IEnumerable<MapRenderOpenGlTextureResidencyEntry> Entries =>
        _entriesByHandle.Values;

    internal int Count => _entriesByTexture.Count;

    internal long ResidentBytes =>
        SumResidentBytes();

    internal int ResidentCount =>
        CountResidentEntries();

    internal bool TryGetHandle(
        Texture texture,
        out uint handle)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (_entriesByTexture.TryGetValue(
                texture,
                out MapRenderOpenGlTextureResidencyEntry? entry))
        {
            handle = entry.Handle;
            return true;
        }
        handle = 0;
        return false;
    }

    internal bool TryGetEntry(
        uint handle,
        out MapRenderOpenGlTextureResidencyEntry entry) =>
        _entriesByHandle.TryGetValue(handle, out entry!);

    internal MapRenderOpenGlTextureResidencyEntry Add(
        Texture texture,
        uint handle,
        TextureTarget target,
        int faceCount,
        int storageLevelCount,
        long estimatedResidentBytes,
        bool isPinned,
        OpenGlAuthoredBcUploadPlan? authoredBcPlan,
        bool usesDirectAuthoredBcUpload)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (handle == 0)
            throw new ArgumentOutOfRangeException(nameof(handle));
        if (faceCount is not (1 or 6))
            throw new ArgumentOutOfRangeException(nameof(faceCount));
        if (storageLevelCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(storageLevelCount));
        if (estimatedResidentBytes <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(estimatedResidentBytes));
        if (_entriesByHandle.ContainsKey(handle))
        {
            throw new ArgumentException(
                "The OpenGL texture handle is already owned.",
                nameof(handle));
        }

        var entry = new MapRenderOpenGlTextureResidencyEntry(
            texture,
            handle,
            target,
            faceCount,
            storageLevelCount,
            estimatedResidentBytes,
            isPinned,
            authoredBcPlan,
            usesDirectAuthoredBcUpload,
            _nextCreationOrdinal++);
        _entriesByTexture.Add(texture, entry);
        _entriesByHandle.Add(handle, entry);
        return entry;
    }

    internal void Remove(Texture texture, uint handle)
    {
        ArgumentNullException.ThrowIfNull(texture);
        _entriesByTexture.Remove(texture);
        _entriesByHandle.Remove(handle);
    }

    internal void Clear()
    {
        _entriesByTexture.Clear();
        _entriesByHandle.Clear();
        _nextCreationOrdinal = 0;
    }

    private long SumResidentBytes()
    {
        long result = 0;
        foreach (MapRenderOpenGlTextureResidencyEntry entry in
                 _entriesByHandle.Values)
        {
            result = checked(result + entry.ResidentBytes);
        }
        return result;
    }

    private int CountResidentEntries()
    {
        int result = 0;
        foreach (MapRenderOpenGlTextureResidencyEntry entry in
                 _entriesByHandle.Values)
        {
            if (entry.IsResident)
                result++;
        }
        return result;
    }
}

internal sealed class MapRenderOpenGlTextureResidencyEntry
{
    internal MapRenderOpenGlTextureResidencyEntry(
        Texture source,
        uint handle,
        TextureTarget target,
        int faceCount,
        int storageLevelCount,
        long estimatedResidentBytes,
        bool isPinned,
        OpenGlAuthoredBcUploadPlan? authoredBcPlan,
        bool usesDirectAuthoredBcUpload,
        long creationOrdinal)
    {
        if (usesDirectAuthoredBcUpload && authoredBcPlan is null)
        {
            throw new ArgumentException(
                "A direct authored-BC upload requires an authored upload plan.",
                nameof(usesDirectAuthoredBcUpload));
        }
        Source = source;
        Handle = handle;
        Target = target;
        FaceCount = faceCount;
        StorageLevelCount = storageLevelCount;
        EstimatedResidentBytes = estimatedResidentBytes;
        IsPinned = isPinned;
        AuthoredBcPlan = authoredBcPlan;
        UsesDirectAuthoredBcUpload = usesDirectAuthoredBcUpload;
        CreationOrdinal = creationOrdinal;
    }

    internal Texture Source { get; }

    internal uint Handle { get; }

    internal TextureTarget Target { get; }

    internal int FaceCount { get; }

    internal int StorageLevelCount { get; }

    internal long EstimatedResidentBytes { get; }

    internal bool IsPinned { get; private set; }

    internal OpenGlAuthoredBcUploadPlan? AuthoredBcPlan { get; }

    internal bool UsesDirectAuthoredBcUpload { get; }

    /// <summary>
    /// Renderer-owned compatibility representation created only when a proven
    /// authored chain first becomes resident on a backend without the required
    /// S3TC format. The source scene texture remains immutable.
    /// </summary>
    internal Texture? DecodedAuthoredBcFallback { get; private set; }

    internal long CreationOrdinal { get; }

    internal long LastVisibleFrame { get; private set; } = -1;

    internal long LastResidentFrame { get; private set; } = -1;

    internal bool IsResident { get; private set; }

    internal long ResidentBytes =>
        IsResident ? EstimatedResidentBytes : 0;

    internal void MarkVisible(long frameIndex)
    {
        if (frameIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        LastVisibleFrame = frameIndex;
    }

    internal void MarkResident(long frameIndex)
    {
        if (frameIndex < -1)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        IsResident = true;
        LastResidentFrame = frameIndex;
    }

    internal void Pin() =>
        IsPinned = true;

    internal void SetDecodedAuthoredBcFallback(Texture fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        if (UsesDirectAuthoredBcUpload || AuthoredBcPlan is null)
        {
            throw new InvalidOperationException(
                "This residency entry does not require a decoded authored-BC fallback.");
        }
        DecodedAuthoredBcFallback = fallback;
    }

    internal long ReleaseDecodedAuthoredBcFallback()
    {
        long releasedBytes =
            DecodedAuthoredBcFallback?.DecodedFallbackByteCount ?? 0;
        DecodedAuthoredBcFallback = null;
        return releasedBytes;
    }

    internal void MarkEvicted() =>
        IsResident = false;
}

internal static class MapRenderOpenGlTextureResidencyPolicy
{
    private static readonly IComparer<
        MapRenderOpenGlTextureResidencyEntry> EvictionComparer =
            Comparer<MapRenderOpenGlTextureResidencyEntry>.Create(
                static (left, right) =>
                {
                    int comparison = left.LastVisibleFrame.CompareTo(
                        right.LastVisibleFrame);
                    if (comparison != 0)
                        return comparison;
                    comparison = left.LastResidentFrame.CompareTo(
                        right.LastResidentFrame);
                    return comparison != 0
                        ? comparison
                        : left.CreationOrdinal.CompareTo(
                            right.CreationOrdinal);
                });

    internal static void CollectEvictionCandidates(
        IEnumerable<MapRenderOpenGlTextureResidencyEntry> entries,
        IReadOnlySet<uint> visibleHandles,
        long frameIndex,
        int graceFrames,
        List<MapRenderOpenGlTextureResidencyEntry> destination)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(visibleHandles);
        ArgumentNullException.ThrowIfNull(destination);
        if (frameIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        if (graceFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(graceFrames));

        destination.Clear();
        long oldestEligibleFrame = frameIndex - graceFrames;
        foreach (MapRenderOpenGlTextureResidencyEntry entry in entries)
        {
            if (entry.IsResident &&
                !entry.IsPinned &&
                !visibleHandles.Contains(entry.Handle) &&
                entry.LastVisibleFrame <= oldestEligibleFrame)
            {
                destination.Add(entry);
            }
        }
        destination.Sort(EvictionComparer);
    }

    internal static IReadOnlyList<MapRenderOpenGlTextureResidencyEntry>
        OrderEvictionCandidates(
            IEnumerable<MapRenderOpenGlTextureResidencyEntry> entries,
            IReadOnlySet<uint> visibleHandles,
            long frameIndex,
            int graceFrames)
    {
        var result =
            new List<MapRenderOpenGlTextureResidencyEntry>();
        CollectEvictionCandidates(
            entries,
            visibleHandles,
            frameIndex,
            graceFrames,
            result);
        return result;
    }
}
