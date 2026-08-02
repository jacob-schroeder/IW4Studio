using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Zone;

namespace IW4.Studio.MapEditor.Compilation.Lighting;

/// <summary>
/// One target-observed technique path used to prove that a material can
/// consume the bounded M3 vertex streams.
/// </summary>
public sealed class GfxWorldTargetMaterialTechniqueEvidence
{
    private readonly IReadOnlyList<MaterialVertexStreamRouting>
        _vertexRoutes;

    internal GfxWorldTargetMaterialTechniqueEvidence(
        int slot,
        string techniqueName,
        ushort flags,
        int passCount,
        string vertexShaderName,
        string pixelShaderName,
        IEnumerable<MaterialVertexStreamRouting> vertexRoutes)
    {
        if (slot < 0)
            throw new ArgumentOutOfRangeException(nameof(slot));
        ArgumentException.ThrowIfNullOrWhiteSpace(techniqueName);
        if (passCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(passCount));
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexShaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pixelShaderName);
        ArgumentNullException.ThrowIfNull(vertexRoutes);

        MaterialVertexStreamRouting[] routeCopy =
            vertexRoutes.ToArray();
        if (routeCopy.Length == 0 ||
            routeCopy.Select(value => value.Source).Distinct().Count() !=
                routeCopy.Length ||
            routeCopy.Select(value => value.Dest).Distinct().Count() !=
                routeCopy.Length)
        {
            throw new ArgumentException(
                "A target-material technique requires unique source and " +
                "destination vertex routes.",
                nameof(vertexRoutes));
        }

        Slot = slot;
        TechniqueName = techniqueName;
        Flags = flags;
        PassCount = passCount;
        VertexShaderName = vertexShaderName;
        PixelShaderName = pixelShaderName;
        _vertexRoutes =
            new ReadOnlyCollection<MaterialVertexStreamRouting>(
                routeCopy);
    }

    public int Slot { get; }

    public string TechniqueName { get; }

    public ushort Flags { get; }

    public int PassCount { get; }

    public string VertexShaderName { get; }

    public string PixelShaderName { get; }

    public IReadOnlyList<MaterialVertexStreamRouting> VertexRoutes =>
        _vertexRoutes;
}

/// <summary>
/// Immutable evidence for one external material whose complete definition is
/// supplied by an official dependency zone loaded before the generated map.
/// This is dependency and compatibility evidence, not generated-map target
/// acceptance.
/// </summary>
public sealed class GfxWorldTargetMaterialDependencyEvidence
{
    private readonly IReadOnlyList<ZoneAssetKey>
        _resolvedTransitiveAssetKeys;
    private readonly IReadOnlyList<uint> _stateLoadBits;

    internal GfxWorldTargetMaterialDependencyEvidence(
        string logicalName,
        string providerZoneName,
        MapCompilerSha256Digest providerFastFileSha256,
        int providerXAssetRow,
        XAssetType providerRootType,
        string providerRootName,
        string providerReferencePath,
        string consumerEvidenceZoneName,
        MapCompilerSha256Digest consumerEvidenceFastFileSha256,
        MapCompilerSha256Digest materialSemanticSha256,
        byte sortKey,
        byte cameraRegion,
        byte gameFlags,
        byte stateFlags,
        string techniqueSetName,
        MaterialWorldVertexFormat worldVertexFormat,
        GfxWorldTargetMaterialTechniqueEvidence primaryTechnique,
        GfxWorldTargetMaterialTechniqueEvidence dynamicFogTechnique,
        uint materialSamplerNameHash,
        string materialSamplerImageName,
        byte materialSamplerSemantic,
        IEnumerable<uint> stateLoadBits,
        IEnumerable<ZoneAssetKey> resolvedTransitiveAssetKeys)
    {
        AssetKey = new ZoneAssetKey(XAssetType.Material, logicalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerZoneName);
        ArgumentNullException.ThrowIfNull(providerFastFileSha256);
        if (providerXAssetRow < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerXAssetRow));
        }
        if (!Enum.IsDefined(providerRootType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerRootType));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(providerRootName);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerReferencePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            consumerEvidenceZoneName);
        ArgumentNullException.ThrowIfNull(
            consumerEvidenceFastFileSha256);
        ArgumentNullException.ThrowIfNull(materialSemanticSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(techniqueSetName);
        if (!Enum.IsDefined(worldVertexFormat))
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldVertexFormat));
        }
        ArgumentNullException.ThrowIfNull(primaryTechnique);
        ArgumentNullException.ThrowIfNull(dynamicFogTechnique);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            materialSamplerImageName);
        ArgumentNullException.ThrowIfNull(stateLoadBits);
        ArgumentNullException.ThrowIfNull(resolvedTransitiveAssetKeys);

        uint[] stateLoadBitsCopy = stateLoadBits.ToArray();
        if (stateLoadBitsCopy.Length != 2)
        {
            throw new ArgumentException(
                "The observed IW4 material state requires exactly two load " +
                "words.",
                nameof(stateLoadBits));
        }
        ZoneAssetKey[] transitiveCopy =
            resolvedTransitiveAssetKeys
                .Distinct()
                .OrderBy(value => value.Type)
                .ThenBy(
                    value => value.LogicalName,
                    StringComparer.Ordinal)
                .ToArray();
        if (transitiveCopy.Length == 0)
        {
            throw new ArgumentException(
                "A target material requires a resolved transitive asset " +
                "closure.",
                nameof(resolvedTransitiveAssetKeys));
        }

        ProviderZoneName = NormalizeZoneName(providerZoneName);
        ProviderFastFileSha256 = providerFastFileSha256;
        ProviderXAssetRow = providerXAssetRow;
        ProviderRootType = providerRootType;
        ProviderRootName = providerRootName;
        ProviderReferencePath = providerReferencePath;
        ConsumerEvidenceZoneName =
            NormalizeZoneName(consumerEvidenceZoneName);
        ConsumerEvidenceFastFileSha256 =
            consumerEvidenceFastFileSha256;
        MaterialSemanticSha256 = materialSemanticSha256;
        SortKey = sortKey;
        CameraRegion = cameraRegion;
        GameFlags = gameFlags;
        StateFlags = stateFlags;
        TechniqueSetName = techniqueSetName;
        WorldVertexFormat = worldVertexFormat;
        PrimaryTechnique = primaryTechnique;
        DynamicFogTechnique = dynamicFogTechnique;
        MaterialSamplerNameHash = materialSamplerNameHash;
        MaterialSamplerImageName = materialSamplerImageName;
        MaterialSamplerSemantic = materialSamplerSemantic;
        _stateLoadBits =
            new ReadOnlyCollection<uint>(stateLoadBitsCopy);
        _resolvedTransitiveAssetKeys =
            new ReadOnlyCollection<ZoneAssetKey>(transitiveCopy);
        DependencyDigest =
            GfxWorldTargetMaterialDependencyDigestCalculator.Compute(
                this);
        EvidenceProvenanceDigest =
            GfxWorldTargetMaterialDependencyDigestCalculator
                .ComputeEvidenceProvenance(this);
    }

    public ZoneAssetKey AssetKey { get; }

    public string SerializedExternalName =>
        $",{AssetKey.LogicalName}";

    public string ProviderZoneName { get; }

    public MapCompilerSha256Digest ProviderFastFileSha256 { get; }

    public int ProviderXAssetRow { get; }

    public XAssetType ProviderRootType { get; }

    public string ProviderRootName { get; }

    public string ProviderReferencePath { get; }

    public string ConsumerEvidenceZoneName { get; }

    public MapCompilerSha256Digest ConsumerEvidenceFastFileSha256
    {
        get;
    }

    public MapCompilerSha256Digest MaterialSemanticSha256 { get; }

    public byte SortKey { get; }

    public byte CameraRegion { get; }

    public byte GameFlags { get; }

    public byte StateFlags { get; }

    public string TechniqueSetName { get; }

    public MaterialWorldVertexFormat WorldVertexFormat { get; }

    public GfxWorldTargetMaterialTechniqueEvidence PrimaryTechnique
    {
        get;
    }

    public GfxWorldTargetMaterialTechniqueEvidence DynamicFogTechnique
    {
        get;
    }

    public uint MaterialSamplerNameHash { get; }

    public string MaterialSamplerImageName { get; }

    public byte MaterialSamplerSemantic { get; }

    public IReadOnlyList<uint> StateLoadBits => _stateLoadBits;

    public IReadOnlyList<ZoneAssetKey> ResolvedTransitiveAssetKeys =>
        _resolvedTransitiveAssetKeys;

    public MapCompilerSha256Digest DependencyDigest { get; }

    public MapCompilerSha256Digest EvidenceProvenanceDigest { get; }

    public bool IsOpaque =>
        SortKey == 1 &&
        CameraRegion == 0 &&
        StateLoadBits.SequenceEqual([0x58128812u, 0x0000000Du]);

    private static string NormalizeZoneName(string value)
    {
        string normalized = value
            .Trim()
            .Replace('\\', '/')
            .ToLowerInvariant();
        if (normalized.Length == 0 ||
            normalized.Contains('/') ||
            normalized.EndsWith(".ff", StringComparison.Ordinal) ||
            normalized.IndexOf('\0') >= 0)
        {
            throw new ArgumentException(
                "A dependency zone name must be a bare normalized zone " +
                "identity.",
                nameof(value));
        }
        return normalized;
    }
}

/// <summary>
/// Official PS3 material observations admitted by the bounded target-probe
/// compiler. Adding an entry requires a new immutable evidence handoff.
/// </summary>
public static class GfxWorldTargetMaterialDependencyCatalog
{
    public static GfxWorldTargetMaterialDependencyEvidence
        CommonMpChemLightGlow { get; } =
            new(
                logicalName: "m/mtl_weapon_chem_light_glow",
                providerZoneName: "common_mp",
                providerFastFileSha256:
                    new MapCompilerSha256Digest(
                        "af30c393026db3e0a643dda62a80e960425919e5b21681b" +
                        "2157a7a1d94d3ed1d"),
                providerXAssetRow: 5660,
                providerRootType: XAssetType.Weapon,
                providerRootName: "lightstick_mp",
                providerReferencePath:
                    "$.Definition.GunModels[0].Materials[5]",
                consumerEvidenceZoneName: "mp_terminal",
                consumerEvidenceFastFileSha256:
                    new MapCompilerSha256Digest(
                        "a5c1af63685ac3bcbdec37c4c9d88fca58f5cd0c960d7" +
                        "d7f246edf09bbd052f0"),
                materialSemanticSha256:
                    new MapCompilerSha256Digest(
                        "f6b52ba6b2c2e430e3095c7d2496be4a0fe0929db617e" +
                        "bc8ea35a599d725a751"),
                sortKey: 1,
                cameraRegion: 0,
                gameFlags: 0x40,
                stateFlags: 0x59,
                techniqueSetName: "m_unlit_replace_lin",
                worldVertexFormat:
                    MaterialWorldVertexFormat
                        .MTL_WORLDVERT_TEX_1_NRM_1,
                primaryTechnique:
                    Technique(
                        slot: 9,
                        name: "vertcol_simple_fog_lin",
                        vertexShader: "vertcol_simple_fog.hlsl",
                        pixelShader:
                            "vertcol_simple_fog_lin.hlsl"),
                dynamicFogTechnique:
                    Technique(
                        slot: 10,
                        name: "vertcol_simple_fog_lin_dfog",
                        vertexShader: "vertcol_simple_dfog.hlsl",
                        pixelShader:
                            "vertcol_simple_dfog_lin.hlsl"),
                materialSamplerNameHash: 0xA0AB1041,
                materialSamplerImageName: "chem_light_col",
                materialSamplerSemantic: 2,
                stateLoadBits: [0x58128812, 0x0000000D],
                resolvedTransitiveAssetKeys:
                [
                    new ZoneAssetKey(
                        XAssetType.Techset,
                        "m_unlit_replace_lin"),
                    new ZoneAssetKey(
                        XAssetType.VertexShader,
                        "vertcol_simple_fog.hlsl"),
                    new ZoneAssetKey(
                        XAssetType.PixelShader,
                        "vertcol_simple_fog_lin.hlsl"),
                    new ZoneAssetKey(
                        XAssetType.VertexShader,
                        "vertcol_simple_dfog.hlsl"),
                    new ZoneAssetKey(
                        XAssetType.PixelShader,
                        "vertcol_simple_dfog_lin.hlsl"),
                    new ZoneAssetKey(
                        XAssetType.Image,
                        "chem_light_col")
                ]);

    private static GfxWorldTargetMaterialTechniqueEvidence Technique(
        int slot,
        string name,
        string vertexShader,
        string pixelShader) =>
        new(
            slot,
            name,
            flags: 0x0008,
            passCount: 1,
            vertexShader,
            pixelShader,
            [
                new MaterialVertexStreamRouting(0, 0),
                new MaterialVertexStreamRouting(1, 3),
                new MaterialVertexStreamRouting(2, 8)
            ]);
}

/// <summary>
/// Canonical semantic dependency digest used by whole-map content identity.
/// It binds the logical external edge and compatible transitive graph while
/// deliberately excluding which evidence file, row, or owner path happened
/// to prove that graph.
/// </summary>
public static class GfxWorldTargetMaterialDependencyDigestCalculator
{
    private const string SemanticDomain =
        "iw4-studio.gfxworld.target-material-dependency/v2";

    private const string EvidenceProvenanceDomain =
        "iw4-studio.gfxworld.target-material-evidence-provenance/v1";

    public static MapCompilerSha256Digest Compute(
        GfxWorldTargetMaterialDependencyEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "domain", SemanticDomain);
        Append(hash, "asset-type", (int)evidence.AssetKey.Type);
        Append(hash, "asset-name", evidence.AssetKey.LogicalName);
        Append(
            hash,
            "material-semantic-sha256",
            evidence.MaterialSemanticSha256.Value);
        Append(hash, "sort-key", evidence.SortKey);
        Append(hash, "camera-region", evidence.CameraRegion);
        Append(hash, "game-flags", evidence.GameFlags);
        Append(hash, "state-flags", evidence.StateFlags);
        Append(hash, "technique-set", evidence.TechniqueSetName);
        Append(
            hash,
            "world-vertex-format",
            (int)evidence.WorldVertexFormat);
        AppendTechnique(hash, "primary", evidence.PrimaryTechnique);
        AppendTechnique(
            hash,
            "dynamic-fog",
            evidence.DynamicFogTechnique);
        Append(
            hash,
            "material-sampler-name-hash",
            evidence.MaterialSamplerNameHash);
        Append(
            hash,
            "material-sampler-image",
            evidence.MaterialSamplerImageName);
        Append(
            hash,
            "material-sampler-semantic",
            evidence.MaterialSamplerSemantic);
        Append(hash, "state-load-bit-count", evidence.StateLoadBits.Count);
        foreach (uint value in evidence.StateLoadBits)
            Append(hash, "state-load-bits", value);
        Append(
            hash,
            "transitive-asset-count",
            evidence.ResolvedTransitiveAssetKeys.Count);
        foreach (ZoneAssetKey key in
                 evidence.ResolvedTransitiveAssetKeys)
        {
            Append(hash, "transitive-asset-type", (int)key.Type);
            Append(hash, "transitive-asset-name", key.LogicalName);
        }

        return new MapCompilerSha256Digest(
            Convert.ToHexString(hash.GetHashAndReset())
                .ToLowerInvariant());
    }

    /// <summary>
    /// Audit digest for the immutable files and paths that established the
    /// semantic contract. It is intentionally not map content identity.
    /// </summary>
    public static MapCompilerSha256Digest ComputeEvidenceProvenance(
        GfxWorldTargetMaterialDependencyEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "domain", EvidenceProvenanceDomain);
        Append(hash, "asset-type", (int)evidence.AssetKey.Type);
        Append(hash, "asset-name", evidence.AssetKey.LogicalName);
        Append(hash, "provider-zone", evidence.ProviderZoneName);
        Append(
            hash,
            "provider-fastfile-sha256",
            evidence.ProviderFastFileSha256.Value);
        Append(hash, "provider-xasset-row", evidence.ProviderXAssetRow);
        Append(hash, "provider-root-type", (int)evidence.ProviderRootType);
        Append(hash, "provider-root-name", evidence.ProviderRootName);
        Append(
            hash,
            "provider-reference-path",
            evidence.ProviderReferencePath);
        Append(
            hash,
            "consumer-evidence-zone",
            evidence.ConsumerEvidenceZoneName);
        Append(
            hash,
            "consumer-evidence-fastfile-sha256",
            evidence.ConsumerEvidenceFastFileSha256.Value);
        Append(
            hash,
            "material-semantic-sha256",
            evidence.MaterialSemanticSha256.Value);

        return new MapCompilerSha256Digest(
            Convert.ToHexString(hash.GetHashAndReset())
                .ToLowerInvariant());
    }

    private static void AppendTechnique(
        IncrementalHash hash,
        string prefix,
        GfxWorldTargetMaterialTechniqueEvidence value)
    {
        Append(hash, $"{prefix}-slot", value.Slot);
        Append(hash, $"{prefix}-name", value.TechniqueName);
        Append(hash, $"{prefix}-flags", value.Flags);
        Append(hash, $"{prefix}-pass-count", value.PassCount);
        Append(
            hash,
            $"{prefix}-vertex-shader",
            value.VertexShaderName);
        Append(
            hash,
            $"{prefix}-pixel-shader",
            value.PixelShaderName);
        Append(
            hash,
            $"{prefix}-vertex-route-count",
            value.VertexRoutes.Count);
        foreach (MaterialVertexStreamRouting route in value.VertexRoutes)
        {
            Append(hash, $"{prefix}-vertex-route-source", route.Source);
            Append(hash, $"{prefix}-vertex-route-destination", route.Dest);
        }
    }

    private static void Append(
        IncrementalHash hash,
        string tag,
        string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        AppendHeader(hash, tag, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void Append(
        IncrementalHash hash,
        string tag,
        int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        AppendHeader(hash, tag, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void Append(
        IncrementalHash hash,
        string tag,
        uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        AppendHeader(hash, tag, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendHeader(
        IncrementalHash hash,
        string tag,
        int payloadLength)
    {
        byte[] tagBytes = Encoding.UTF8.GetBytes(tag);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, tagBytes.Length);
        hash.AppendData(length);
        hash.AppendData(tagBytes);
        BinaryPrimitives.WriteInt32BigEndian(length, payloadLength);
        hash.AppendData(length);
    }
}
