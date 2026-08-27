using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents;

/// <summary>
/// Detached authoring state for one Material. Material-owned tables are
/// copied; referenced XAsset providers remain dependencies of the draft.
/// </summary>
public sealed class MaterialDraft
{
    internal MaterialDraft(MaterialAsset value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Material = Copy(value);
    }

    private MaterialDraft(MaterialDraft value) => Material = Copy(value.Material);

    public MaterialAsset Material { get; private set; }

    public MaterialDraft WithTextureImage(
        int textureTableOrdinal,
        GfxImageAsset image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if ((uint)textureTableOrdinal >= (uint)Material.Textures.Count)
            throw new ArgumentOutOfRangeException(nameof(textureTableOrdinal));
        if (string.IsNullOrWhiteSpace(image.Name))
            throw new ArgumentException("A replacement image requires an asset name.", nameof(image));

        MaterialTextureDef selected = Material.Textures[textureTableOrdinal];
        if (selected.Semantic == TextureSemantic.WaterMap)
        {
            throw new InvalidOperationException(
                "A water texture row stores MaterialWater data and cannot be replaced with an Image provider.");
        }

        var result = new MaterialDraft(this);
        MaterialTextureDef[] textures = result.Material.Textures
            .Select((texture, index) => Copy(
                texture,
                index == textureTableOrdinal ? image : texture.Image))
            .ToArray();
        result.Material = Copy(result.Material, textures);
        return result;
    }

    internal MaterialDraft Clone() => new(this);

    internal MaterialAsset ToAsset() => Copy(Material);

    internal bool SemanticallyEquals(MaterialDraft other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return MaterialEquals(Material, other.Material);
    }

    private static MaterialAsset Copy(
        MaterialAsset value,
        IReadOnlyList<MaterialTextureDef>? textures = null) => new()
    {
        Offset = value.Offset,
        RuntimeAddress = value.RuntimeAddress,
        Info = new MaterialInfo
        {
            NamePointer = value.Info.NamePointer,
            Name = value.Info.Name,
            GameFlags = value.Info.GameFlags,
            SortKey = value.Info.SortKey,
            TextureAtlasRowCount = value.Info.TextureAtlasRowCount,
            TextureAtlasColumnCount = value.Info.TextureAtlasColumnCount,
            DrawSurf = value.Info.DrawSurf,
            SurfaceTypeBits = value.Info.SurfaceTypeBits,
            HashIndex = value.Info.HashIndex,
            Pad16 = value.Info.Pad16
        },
        StateBitsEntries = value.StateBitsEntries.ToArray(),
        TextureCount = value.TextureCount,
        ConstantCount = value.ConstantCount,
        StateBitsCount = value.StateBitsCount,
        StateFlags = value.StateFlags,
        CameraRegion = value.CameraRegion,
        XStringCount = value.XStringCount,
        Pad43 = value.Pad43,
        InlineTechniqueSlotStateBits = value.InlineTechniqueSlotStateBits.ToArray(),
        Pad8E = value.Pad8E,
        RuntimeTechniqueSlotStateBitsPointer = value.RuntimeTechniqueSlotStateBitsPointer,
        RuntimeTechniqueSlotStateBits = value.RuntimeTechniqueSlotStateBits.ToArray(),
        TechniqueSetPointer = value.TechniqueSetPointer,
        TechniqueSet = value.TechniqueSet,
        TextureTablePointer = value.TextureTablePointer,
        Textures = textures?.Select(texture => Copy(texture, texture.Image)).ToArray()
            ?? value.Textures.Select(texture => Copy(texture, texture.Image)).ToArray(),
        ConstantTablePointer = value.ConstantTablePointer,
        Constants = value.Constants.Select(Copy).ToArray(),
        StateBitsPointer = value.StateBitsPointer,
        StateBits = value.StateBits.Select(Copy).ToArray(),
        XStringTablePointer = value.XStringTablePointer,
        XStrings = value.XStrings.Select(value => value with { }).ToArray()
    };

    private static MaterialTextureDef Copy(
        MaterialTextureDef value,
        GfxImageAsset? image) => new()
    {
        NameHash = value.NameHash,
        NameStart = value.NameStart,
        NameEnd = value.NameEnd,
        SamplerState = value.SamplerState,
        Semantic = value.Semantic,
        DataPointer = value.DataPointer,
        Image = image,
        Water = Copy(value.Water)
    };

    private static MaterialWater? Copy(MaterialWater? value) => value is null
        ? null
        : new MaterialWater
        {
            Writable = value.Writable,
            H0XPointer = value.H0XPointer,
            H0YPointer = value.H0YPointer,
            WTermPointer = value.WTermPointer,
            M = value.M,
            N = value.N,
            Lx = value.Lx,
            Lz = value.Lz,
            Gravity = value.Gravity,
            WindVelocity = value.WindVelocity,
            WindDirection = value.WindDirection,
            Amplitude = value.Amplitude,
            CodeConstant = value.CodeConstant,
            ImagePointer = value.ImagePointer,
            H0X = value.H0X.ToArray(),
            H0Y = value.H0Y.ToArray(),
            WTerm = value.WTerm.ToArray(),
            Image = value.Image
        };

    private static MaterialConstantDef Copy(MaterialConstantDef value) => new()
    {
        NameHash = value.NameHash,
        NameBytes = value.NameBytes.ToArray(),
        Literal = value.Literal
    };

    private static GfxStateBits Copy(GfxStateBits value) => new()
    {
        LoadBitsPointer = value.LoadBitsPointer,
        LoadBits = value.LoadBits.ToArray(),
        CommandWordCount = value.CommandWordCount
    };

    private static bool MaterialEquals(MaterialAsset left, MaterialAsset right) =>
        InfoEquals(left.Info, right.Info) &&
        left.StateBitsEntries.SequenceEqual(right.StateBitsEntries) &&
        left.TextureCount == right.TextureCount &&
        left.ConstantCount == right.ConstantCount &&
        left.StateBitsCount == right.StateBitsCount &&
        left.StateFlags == right.StateFlags &&
        left.CameraRegion == right.CameraRegion &&
        left.XStringCount == right.XStringCount &&
        left.Pad43 == right.Pad43 &&
        left.InlineTechniqueSlotStateBits.SequenceEqual(
            right.InlineTechniqueSlotStateBits) &&
        left.Pad8E == right.Pad8E &&
        left.RuntimeTechniqueSlotStateBits.SequenceEqual(
            right.RuntimeTechniqueSlotStateBits) &&
        ProviderNameEquals(left.TechniqueSet, right.TechniqueSet) &&
        SequenceEqual(left.Textures, right.Textures, TextureEquals) &&
        SequenceEqual(left.Constants, right.Constants, ConstantEquals) &&
        SequenceEqual(left.StateBits, right.StateBits, StateBitsEquals) &&
        SequenceEqual(left.XStrings, right.XStrings, XStringEquals);

    private static bool InfoEquals(MaterialInfo left, MaterialInfo right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        left.GameFlags == right.GameFlags &&
        left.SortKey == right.SortKey &&
        left.TextureAtlasRowCount == right.TextureAtlasRowCount &&
        left.TextureAtlasColumnCount == right.TextureAtlasColumnCount &&
        left.DrawSurf == right.DrawSurf &&
        left.SurfaceTypeBits == right.SurfaceTypeBits &&
        left.HashIndex == right.HashIndex &&
        left.Pad16 == right.Pad16;

    private static bool TextureEquals(
        MaterialTextureDef left,
        MaterialTextureDef right) =>
        left.NameHash == right.NameHash &&
        left.NameStart == right.NameStart &&
        left.NameEnd == right.NameEnd &&
        left.SamplerState == right.SamplerState &&
        left.Semantic == right.Semantic &&
        ImageEquals(left.Image, right.Image) &&
        WaterEquals(left.Water, right.Water);

    private static bool ConstantEquals(
        MaterialConstantDef left,
        MaterialConstantDef right) =>
        left.NameHash == right.NameHash &&
        left.NameBytes.SequenceEqual(right.NameBytes) &&
        left.Literal == right.Literal;

    private static bool StateBitsEquals(GfxStateBits left, GfxStateBits right) =>
        left.LoadBits.SequenceEqual(right.LoadBits) &&
        left.CommandWordCount == right.CommandWordCount;

    private static bool XStringEquals(
        MaterialXStringEntry left,
        MaterialXStringEntry right) =>
        left.Index == right.Index &&
        string.Equals(left.Value, right.Value, StringComparison.Ordinal);

    private static bool WaterEquals(MaterialWater? left, MaterialWater? right) =>
        ReferenceEquals(left, right) ||
        left is not null &&
        right is not null &&
        left.Writable == right.Writable &&
        left.M == right.M &&
        left.N == right.N &&
        left.Lx.Equals(right.Lx) &&
        left.Lz.Equals(right.Lz) &&
        left.Gravity.Equals(right.Gravity) &&
        left.WindVelocity.Equals(right.WindVelocity) &&
        left.WindDirection == right.WindDirection &&
        left.Amplitude.Equals(right.Amplitude) &&
        left.CodeConstant == right.CodeConstant &&
        left.H0X.SequenceEqual(right.H0X) &&
        left.H0Y.SequenceEqual(right.H0Y) &&
        left.WTerm.SequenceEqual(right.WTerm) &&
        ImageEquals(left.Image, right.Image);

    private static bool ImageEquals(GfxImageAsset? left, GfxImageAsset? right) =>
        ReferenceEquals(left, right) ||
        left is not null &&
        right is not null &&
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        left.Format == right.Format &&
        left.LevelCount == right.LevelCount &&
        left.DimensionCount == right.DimensionCount &&
        left.MultiFaceControl == right.MultiFaceControl &&
        left.TextureControl1 == right.TextureControl1 &&
        left.Width == right.Width &&
        left.Height == right.Height &&
        left.Depth == right.Depth &&
        left.SerializedMemoryLocation == right.SerializedMemoryLocation &&
        left.MinLodControl == right.MinLodControl &&
        left.RenderTargetPitch == right.RenderTargetPitch &&
        left.SerializedPixelsOffset == right.SerializedPixelsOffset &&
        left.MapType == right.MapType &&
        left.TextureSemantic == right.TextureSemantic &&
        left.Category == right.Category &&
        left.UseSrgbReads == right.UseSrgbReads &&
        left.CardMemory == right.CardMemory &&
        left.BaseWidth == right.BaseWidth &&
        left.BaseHeight == right.BaseHeight &&
        left.BaseDepth == right.BaseDepth &&
        left.BaseLevelCount == right.BaseLevelCount &&
        left.Cached == right.Cached &&
        left.StreamData.SequenceEqual(right.StreamData) &&
        left.StreamImageIndex == right.StreamImageIndex &&
        left.StreamEntries.SequenceEqual(right.StreamEntries) &&
        left.PayloadByteCount == right.PayloadByteCount &&
        left.PayloadBytes.SequenceEqual(right.PayloadBytes);

    private static bool ProviderNameEquals(
        IW4.Assets.Assets.BaseAsset? left,
        IW4.Assets.Assets.BaseAsset? right) =>
        ReferenceEquals(left, right) ||
        left is not null &&
        right is not null &&
        left.SerializedAssetType == right.SerializedAssetType &&
        string.Equals(
            left.SerializedAssetName,
            right.SerializedAssetName,
            StringComparison.Ordinal);

    private static bool SequenceEqual<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        Func<T, T, bool> equals)
    {
        if (left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index++)
        {
            if (!equals(left[index], right[index]))
                return false;
        }
        return true;
    }
}

internal sealed class MaterialAdapter :
    AssetAuthoringAdapter<MaterialAsset, MaterialDraft>
{
    public override XAssetType AssetType => XAssetType.Material;

    public override MaterialDraft CreateDraft(MaterialAsset value) => new(value);

    public override MaterialDraft CloneDraft(MaterialDraft value) => value.Clone();

    public override MaterialAsset CreateDefinition(MaterialDraft value) =>
        value.ToAsset();

    public override bool SemanticallyEquals(
        MaterialDraft left,
        MaterialDraft right) => left.SemanticallyEquals(right);
}
