using IW4.Game.Database;
using IW4.Game.Zone;
using IW4.Runtime.Assets.Images;
using IW4.Runtime.Assets.Sound;
using IW4.Runtime.Database;

namespace IW4.Loaders.Database;

/// <summary>
/// Application-facing result for one complete DB_LoadXZone call. XZone remains
/// the engine-shaped runtime object; this loader-owned record retains the
/// managed context, decoded bytes, semantic assets, and diagnostics needed by
/// tools without placing an operation result in IW4.Game.
/// </summary>
public sealed record LoadedXZone(
    string SourceName,
    XZone Zone,
    DbLoadContext Context,
    DbHeader Header,
    XFile XFile,
    XAssetListSnapshot XAssetList,
    IReadOnlyList<XAssetLoadResult> LoadedAssets,
    byte[] ZoneBytes,
    IReadOnlyList<string> Warnings,
    ZoneObjectObservation? Observation)
{
    public IGfxImagePayloadResolver ImagePayloadResolver { get; init; } =
        UnavailableGfxImagePayloadResolver.Instance;

    public ISoundPayloadResolver SoundPayloadResolver { get; init; } =
        UnavailableSoundPayloadResolver.Instance;
}
