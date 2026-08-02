using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Compiler-owned renderer defaults for a world that has no authored lighting
/// bake. These values are retail-observed source data, not initialized runtime
/// descriptors and not substitutes for a future lighting bake.
/// </summary>
internal static class GfxWorldNoBakeRuntimeDefaults
{
    internal const string ReflectionProbeAssetName =
        "*reflection_probe0";
    internal const string ReflectionProbeSerializedReferenceName =
        ",*reflection_probe0";
    internal const byte ReflectionProbeIndex = 0;
    internal const byte NoLightmapSurfaceIndex = 0x1F;
    internal const ushort EmptyLightGridRowDataStart = ushort.MaxValue;

    private const string CanonicalFallbackLightGridColorHex =
        "002FB021009D64007E91006A005A8500196056003F98003100855A006019563F00" +
        "98310000B02F219D00647E00916A000041F43100EC8900AEBC0089007CB8DE00" +
        "4700B87CDE470000F44131EC0089AE00BC89004275FE7550FFCD12FFFF0ABD20" +
        "B7FFFF468220FFB7FF824642FE7575FF50CDFF12FFBD0A6D94FF9A80FEDD61FF" +
        "FE4ECF66CDFEA8BFFFFF9EE5FE79A466FECDA8FFBFFFE59EFEA4796DFF949AFE" +
        "80DDFF61FECF4E";

    private static readonly byte[] CanonicalFallbackLightGridColor =
        Convert.FromHexString(CanonicalFallbackLightGridColorHex);

    internal static GfxLightGrid CreateEmptyLightGrid() =>
        new()
        {
            HasLightRegions = 0,
            SunPrimaryLightIndex = 0,
            Mins = [0, 0, 0],
            Maxs = [0, 0, 0],
            RowAxis = 0,
            ColAxis = 1,
            RowDataStart = [EmptyLightGridRowDataStart],
            RawRowDataSize = 0,
            RawRowData = [],
            EntryCount = 0,
            Entries = [],
            ColorCount = 2,
            Colors =
            [
                CreateFallbackLightGridColor(),
                CreateFallbackLightGridColor()
            ]
        };

    internal static SymbolicXAssetReference
        CreateReflectionProbeReference() =>
        new(
            XAssetType.Image,
            ReflectionProbeSerializedReferenceName);

    internal static IGfxImageBuildData
        CreateReflectionProbeDefinition() =>
        new CompilerOwnedDefaultReflectionProbeImageBuildData();

    internal static bool IsCanonicalEmptyLightGrid(
        GfxLightGrid value) =>
        value.HasLightRegions == 0 &&
        value.SunPrimaryLightIndex == 0 &&
        value.Mins.SequenceEqual(new ushort[] { 0, 0, 0 }) &&
        value.Maxs.SequenceEqual(new ushort[] { 0, 0, 0 }) &&
        value.RowAxis == 0 &&
        value.ColAxis == 1 &&
        value.RowDataStart.SequenceEqual(
            new[] { EmptyLightGridRowDataStart }) &&
        value.RawRowDataSize == 0 &&
        value.RawRowData.Count == 0 &&
        value.EntryCount == 0 &&
        value.Entries.Count == 0 &&
        value.ColorCount == 2 &&
        value.Colors.Count == 2 &&
        value.Colors.All(IsCanonicalFallbackLightGridColor);

    internal static bool IsCompilerOwnedReflectionProbe(
        IXAssetBuildData? value)
    {
        if (value is not IGfxImageBuildData image)
            return false;

        byte[]? payload = image.GetPayloadCopy();
        return
            image.AssetType == XAssetType.Image &&
            string.Equals(
                image.Name,
                ReflectionProbeAssetName,
                StringComparison.Ordinal) &&
            image.Format == 0x85 &&
            image.LevelCount == 7 &&
            image.DimensionCount == 2 &&
            image.MultiFaceControl == 1 &&
            image.TextureFlags == 0x0001_AAE4 &&
            image.Width == 64 &&
            image.Height == 64 &&
            image.Depth == 1 &&
            image.PixelDataBlock == 0 &&
            image.Pad0F == 0 &&
            image.RenderTargetPitch == 0 &&
            image.PixelsOffset == 0 &&
            image.MapType == 5 &&
            image.TextureSemantic == 1 &&
            image.Category == 1 &&
            image.Pad1B == 0 &&
            image.CardMemory ==
                CompilerOwnedDefaultReflectionProbeImageBuildData
                    .PayloadByteCount &&
            image.BaseWidth == 64 &&
            image.BaseHeight == 64 &&
            image.BaseDepth == 1 &&
            image.BaseLevelCount == 7 &&
            image.Cached == 0 &&
            image.StreamData.Count == 4 &&
            image.StreamData.All(value => !value.HasStreamingData) &&
            image.ExternalStreamPackageIndices.Count == 0 &&
            payload is not null &&
            CompilerOwnedDefaultReflectionProbeImageBuildData
                .IsCanonicalPayload(payload);
    }

    private static GfxLightGridColors
        CreateFallbackLightGridColor() =>
        new(CanonicalFallbackLightGridColor.ToArray());

    private static bool IsCanonicalFallbackLightGridColor(
        GfxLightGridColors value) =>
        value.RgbBytes.Count == GfxLightGridColors.SerializedSize &&
        value.RgbBytes.SequenceEqual(
            CanonicalFallbackLightGridColor);
}

/// <summary>
/// Exact retail default cube used as probe zero by IW4 multiplayer worlds.
/// Its payload is generated rather than stored as an opaque binary resource so
/// the compiler-owned representation stays deterministic and reviewable.
/// </summary>
internal sealed class CompilerOwnedDefaultReflectionProbeImageBuildData :
    IGfxImageBuildData
{
    private const int FaceCount = 6;
    private const int TexelsPerFaceAcrossMips = 5_461;
    private const int BytesPerTexel = 4;
    private const int FaceStride = 21_888;

    private static readonly IReadOnlyList<GfxImageStreamBuildData>
        EmptyStreamData =
            Array.AsReadOnly(
                new GfxImageStreamBuildData[4]);

    private static readonly IReadOnlyList<uint>
        EmptyExternalStreamPackageIndices =
            Array.AsReadOnly(Array.Empty<uint>());

    internal const int PayloadByteCount = FaceCount * FaceStride;

    private static readonly byte[] CanonicalPayload =
        CreatePayload();

    public XAssetType AssetType => XAssetType.Image;
    public string Name =>
        GfxWorldNoBakeRuntimeDefaults.ReflectionProbeAssetName;
    public byte Format => 0x85;
    public byte LevelCount => 7;
    public byte DimensionCount => 2;
    public byte MultiFaceControl => 1;
    public uint TextureFlags => 0x0001_AAE4;
    public ushort Width => 64;
    public ushort Height => 64;
    public ushort Depth => 1;
    public byte PixelDataBlock => 0;
    public byte Pad0F => 0;
    public uint RenderTargetPitch => 0;
    public uint PixelsOffset => 0;
    public byte MapType => 5;
    public byte TextureSemantic => 1;
    public byte Category => 1;
    public byte Pad1B => 0;
    public uint CardMemory => PayloadByteCount;
    public ushort BaseWidth => 64;
    public ushort BaseHeight => 64;
    public ushort BaseDepth => 1;
    public byte BaseLevelCount => 7;
    public byte Cached => 0;
    public IReadOnlyList<GfxImageStreamBuildData> StreamData =>
        EmptyStreamData;
    public IReadOnlyList<uint> ExternalStreamPackageIndices =>
        EmptyExternalStreamPackageIndices;

    public byte[] GetPayloadCopy() => CanonicalPayload.ToArray();

    internal static bool IsCanonicalPayload(
        ReadOnlySpan<byte> value) =>
        value.SequenceEqual(CanonicalPayload);

    private static byte[] CreatePayload()
    {
        var payload = new byte[PayloadByteCount];
        int faceTexelByteCount =
            TexelsPerFaceAcrossMips * BytesPerTexel;

        for (int face = 0; face < FaceCount; face++)
        {
            int faceStart = face * FaceStride;
            int faceTexelEnd = faceStart + faceTexelByteCount;
            for (int offset = faceStart;
                 offset < faceTexelEnd;
                 offset += BytesPerTexel)
            {
                payload[offset] = 0x00;
                payload[offset + 1] = 0xC9;
                payload[offset + 2] = 0x16;
                payload[offset + 3] = 0x16;
            }
        }

        return payload;
    }
}
