using IW4.Assets.Assets.Image;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Canonical external GfxImage body. Runtime-mutated texture header state is
/// deliberately excluded; only the comma-prefixed wire identity is frozen.
/// </summary>
internal sealed class GfxImageReferenceLinkRecipe : AssetLinkRecipe
{
    private GfxImageReferenceLinkRecipe(
        AssetKey key,
        string originalSerializedName)
        : base(
            key,
            originalSerializedName,
            requireReferencePlaceholder: true)
    {
    }

    public static GfxImageReferenceLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        GfxImageAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new GfxImageReferenceLinkRecipe(key, originalSerializedName);
    }

    public static GfxImageReferenceLinkRecipe CreateExternal(
        AssetKey key,
        string originalSerializedName) =>
        new(key, originalSerializedName);

    public override void Emit(
        ZoneEmissionWriter output,
        Action<AssetDependency, XBlockAddress, int> emitDependency)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(emitDependency);

        output.PushTempScope();
        try
        {
            output.Allocate(
                XFileBlockType.TEMP,
                GfxImageAsset.SerializedSize,
                alignment: 4);
            output.ReserveSource(GfxImageAsset.SerializedSize - sizeof(int));
            output.WriteInt32(-1);
            EmitName(output);
        }
        finally
        {
            output.PopTempScope();
        }
    }
}
