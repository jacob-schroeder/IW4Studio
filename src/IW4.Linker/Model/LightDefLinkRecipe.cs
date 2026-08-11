using IW4.Assets.Assets.LightDef;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen GfxLightDef schema data. The image reference is reduced to logical
/// identity at the request boundary rather than retaining a mutable asset.
/// </summary>
internal sealed class LightDefLinkRecipe : AssetLinkRecipe
{
    private readonly AssetDependency? _image;
    private readonly byte[] _padding;
    private readonly IReadOnlyList<AssetDependency> _dependencies;

    private LightDefLinkRecipe(
        AssetKey key,
        string name,
        AssetDependency? image,
        byte samplerState,
        byte[] padding,
        uint lmapLookupStart)
        : base(key, name)
    {
        _image = image;
        _padding = padding;
        SamplerState = samplerState;
        LmapLookupStart = lmapLookupStart;
        _dependencies = image is { } dependency
            ? Array.AsReadOnly([dependency])
            : Array.Empty<AssetDependency>();
    }

    private byte SamplerState { get; }
    private uint LmapLookupStart { get; }

    public override IReadOnlyList<AssetDependency> Dependencies => _dependencies;

    public static LightDefLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        LightDefAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (originalSerializedName.StartsWith(','))
        {
            throw new NotSupportedException(
                "Canonical linking currently supports LightDef providers only as owned definitions.");
        }

        byte[] sourcePadding = definition.Pad09To0B ??
            throw new InvalidDataException(
                "LightDef padding at 0x09..0x0B cannot be null.");
        byte[] padding = sourcePadding.Length == 0
            ? new byte[3]
            : sourcePadding.ToArray();
        if (padding.Length != 3)
        {
            throw new InvalidDataException(
                "LightDef padding at 0x09..0x0B must contain exactly three bytes.");
        }

        AssetDependency? image = null;
        if (definition.Image is { } imageDefinition)
        {
            if (imageDefinition.SerializedAssetType != XAssetType.Image)
            {
                throw new InvalidDataException(
                    "LightDef.Image must identify a serialized Image provider.");
            }

            AssetKey imageKey;
            try
            {
                imageKey = AssetKey.FromDefinition(imageDefinition);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    "LightDef.Image has an invalid asset identity.",
                    exception);
            }

            image = new AssetDependency(
                imageKey,
                XAssetType.Image,
                "LightDef.Image");
        }

        return new LightDefLinkRecipe(
            key,
            originalSerializedName,
            image,
            definition.SamplerState,
            padding,
            definition.LmapLookupStart);
    }

    public override void Emit(
        ZoneEmissionWriter output,
        Action<AssetDependency, XBlockAddress, int> emitDependency)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(emitDependency);

        output.PushTempScope();
        try
        {
            XBlockAddress root = output.Allocate(
                XFileBlockType.TEMP,
                LightDefAsset.SerializedSize,
                alignment: 4);
            int rootSourceOffset = output.SourceLength;
            output.WriteInt32(-1);
            output.WriteInt32(0);
            output.WriteBytes([SamplerState]);
            output.WriteBytes(_padding);
            output.WriteUInt32(LmapLookupStart);

            EmitName(output);

            if (_image is { } image)
            {
                emitDependency(
                    image,
                    new XBlockAddress(
                        XFileBlockType.TEMP,
                        checked(root.Offset + sizeof(int))),
                    checked(rootSourceOffset + sizeof(int)));
            }
        }
        finally
        {
            output.PopTempScope();
        }
    }
}
