using System.Diagnostics.CodeAnalysis;
using IW4.Assets.Assets.RawFile;

namespace IW4.Render.Assets;

/// <summary>
/// Exact-revision boundary for renderer-owned RawFile lookups. Createart fog
/// resolution uses this instead of retaining a dependency-zone object.
/// </summary>
public interface IMapRenderCanonicalRawFileProvider
{
    bool HasCanonicalAssetPoolRevision(long expectedPoolRevision);

    bool TryResolveCanonicalRawFile(
        string name,
        long expectedPoolRevision,
        [NotNullWhen(true)] out RawFileAsset? rawFile);
}
