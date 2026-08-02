using IW4.Assets.Zone;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets.Lifecycle.State;

/// <summary>
/// Immutable process-global renderer state for the active capacity-one
/// GfxWorld. Destination identity remains {kind, ordinal}; source-image
/// identity and current override-cache identities are separate side state.
/// </summary>
public sealed class GfxWorldTextureState
{
    public GfxWorldTextureState(
        XAssetPoolAddress worldAddress,
        XBlockAddress? reflectionProbeTexturesAddress,
        XBlockAddress? lightmapPrimaryTexturesAddress,
        XBlockAddress? lightmapSecondaryTexturesAddress,
        IReadOnlyList<GfxWorldTextureRowState> reflectionProbeRows,
        IReadOnlyList<GfxWorldTextureRowState> lightmapPrimaryRows,
        IReadOnlyList<GfxWorldTextureRowState> lightmapSecondaryRows,
        XAssetPoolAddress? primaryOverrideImageAddress,
        XAssetPoolAddress? secondaryOverrideImageAddress,
        long revision)
    {
        if (worldAddress.AssetType != XAssetType.GfxMap)
            throw new ArgumentException("World texture state requires a canonical GfxMap slot.", nameof(worldAddress));
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ValidateOverrideAddress(primaryOverrideImageAddress, nameof(primaryOverrideImageAddress));
        ValidateOverrideAddress(secondaryOverrideImageAddress, nameof(secondaryOverrideImageAddress));

        WorldAddress = worldAddress;
        ReflectionProbeTexturesAddress = reflectionProbeTexturesAddress;
        LightmapPrimaryTexturesAddress = lightmapPrimaryTexturesAddress;
        LightmapSecondaryTexturesAddress = lightmapSecondaryTexturesAddress;
        ReflectionProbeRows = SnapshotRows(
            reflectionProbeRows,
            GfxWorldTextureKind.ReflectionProbe,
            nameof(reflectionProbeRows));
        LightmapPrimaryRows = SnapshotRows(
            lightmapPrimaryRows,
            GfxWorldTextureKind.PrimaryLightmap,
            nameof(lightmapPrimaryRows));
        LightmapSecondaryRows = SnapshotRows(
            lightmapSecondaryRows,
            GfxWorldTextureKind.SecondaryLightmap,
            nameof(lightmapSecondaryRows));
        PrimaryOverrideImageAddress = primaryOverrideImageAddress;
        SecondaryOverrideImageAddress = secondaryOverrideImageAddress;
        Revision = revision;
    }

    public XAssetPoolAddress WorldAddress { get; }

    public XBlockAddress? ReflectionProbeTexturesAddress { get; }

    public XBlockAddress? LightmapPrimaryTexturesAddress { get; }

    public XBlockAddress? LightmapSecondaryTexturesAddress { get; }

    public IReadOnlyList<GfxWorldTextureRowState> ReflectionProbeRows { get; }

    public IReadOnlyList<GfxWorldTextureRowState> LightmapPrimaryRows { get; }

    public IReadOnlyList<GfxWorldTextureRowState> LightmapSecondaryRows { get; }

    public XAssetPoolAddress? PrimaryOverrideImageAddress { get; }

    public XAssetPoolAddress? SecondaryOverrideImageAddress { get; }

    public long Revision { get; }

    public IReadOnlyList<GfxWorldTextureRowState> GetRows(
        GfxWorldTextureKind kind) => kind switch
        {
            GfxWorldTextureKind.ReflectionProbe => ReflectionProbeRows,
            GfxWorldTextureKind.PrimaryLightmap => LightmapPrimaryRows,
            GfxWorldTextureKind.SecondaryLightmap => LightmapSecondaryRows,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    public bool TryGetRow(
        GfxWorldTextureKind kind,
        int ordinal,
        out GfxWorldTextureRowState? row)
    {
        IReadOnlyList<GfxWorldTextureRowState> rows = GetRows(kind);
        if ((uint)ordinal < (uint)rows.Count)
        {
            row = rows[ordinal];
            return true;
        }

        row = null;
        return false;
    }

    private static IReadOnlyList<GfxWorldTextureRowState> SnapshotRows(
        IReadOnlyList<GfxWorldTextureRowState> rows,
        GfxWorldTextureKind expectedKind,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(rows, parameterName);
        GfxWorldTextureRowState[] snapshot = rows.ToArray();
        for (int ordinal = 0; ordinal < snapshot.Length; ordinal++)
        {
            GfxWorldTextureRowState row = snapshot[ordinal] ??
                throw new ArgumentException("GfxWorld texture state cannot contain null rows.", parameterName);
            if (row.Kind != expectedKind || row.Ordinal != ordinal)
            {
                throw new ArgumentException(
                    $"{expectedKind} rows must retain contiguous native ordinals; row {ordinal} is {row.Kind}[{row.Ordinal}].",
                    parameterName);
            }
        }

        return Array.AsReadOnly(snapshot);
    }

    private static void ValidateOverrideAddress(
        XAssetPoolAddress? address,
        string parameterName)
    {
        if (address is { AssetType: not XAssetType.Image })
            throw new ArgumentException("Lightmap override cache identity must be an Image slot.", parameterName);
    }
}
