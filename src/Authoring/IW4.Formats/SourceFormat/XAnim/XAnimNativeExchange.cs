using IW4.Formats.SourceFormat.Technique;
using IW4.Game.Assets.XAnim;

namespace IW4.Formats.SourceFormat.XAnim;

/// <summary>Exchanges the retained PS3 XAnim graph without a playback projection.</summary>
public sealed class XAnimNativeExchange
{
    public IReadOnlyList<string> Unlink(string sourceDirectory, XAnimPartsAsset animation)
    {
        ArgumentNullException.ThrowIfNull(animation);
        string name = SourceOutput.NormalizeOwnedAssetName(animation.Name, "XAnim");
        string json = NativeXAnimCodec.Serialize(animation, name);
        return new SourceOutput(sourceDirectory).WriteTextBatch(
            [($"xanim_native/{name}.json", writer => writer.WriteLine(json))]);
    }

    public XAnimPartsAsset Link(string sourceDirectory, string animationName)
    {
        string name = SourceOutput.NormalizeOwnedAssetName(animationName, "XAnim");
        string path = NativeSourcePath.Resolve(sourceDirectory, "xanim_native", name, ".json");
        return NativeXAnimCodec.Deserialize(File.ReadAllText(path), name);
    }
}
