using System.Text;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.LightDef;
using IW4.Game.Assets.Material;

namespace IW4.Formats.SourceFormat.LightDef;

/// <summary>
/// Exchanges the supported point-light definition and writes the legacy IW4
/// light source tuple: sampler-state byte, attenuation image name, and null.
/// </summary>
public sealed class LightDefExchange
{
    private const string PointLinearName = "light_point_linear";
    private const string PointLinearImageName = "falloff_linear";
    private const uint PointLinearLookupStart = 1;

    /// <summary>Loads the recovered PS3 point-light profile from its legacy tuple.</summary>
    public LightDefAsset LinkPointLinear(
        string sourceDirectory,
        Func<string, GfxImageAsset> loadImage)
    {
        ArgumentNullException.ThrowIfNull(loadImage);
        string path = Path.Combine(sourceDirectory, "lights", PointLinearName);
        byte[] source = File.ReadAllBytes(path);
        byte[] expected = [
            (byte)(MaterialSamplerState.FilterLinear | MaterialSamplerState.ClampU | MaterialSamplerState.ClampV),
            .. Encoding.Latin1.GetBytes(PointLinearImageName),
            0
        ];
        if (!source.AsSpan().SequenceEqual(expected))
            throw new InvalidDataException($"LightDef '{PointLinearName}' does not match the supported native sampler and attenuation image.");

        GfxImageAsset image = loadImage(PointLinearImageName);
        if (image.Name != PointLinearImageName || image.Format != 0x81 ||
            image.TextureControl1 != 0x0001a9ff ||
            image.Width != 32 || image.Height != 1 || image.Depth != 1 ||
            image.BaseWidth != 32 || image.BaseHeight != 1 || image.BaseDepth != 1 ||
            image.LevelCount != 6 || image.BaseLevelCount != 6 ||
            image.MapType != MapType.TwoDimensional ||
            image.TextureSemantic != TextureSemantic.Function ||
            image.Category != ImageCategory.LoadFromFile || image.UsesSrgbReads ||
            image.PayloadByteCount != 128 ||
            image.PayloadBytes.Count != 128 || image.StreamData.Any(part => part.HasStreamingData))
        {
            throw new InvalidDataException($"LightDef '{PointLinearName}' requires the native falloff_linear image profile.");
        }

        return new LightDefAsset
        {
            Name = PointLinearName,
            Image = image,
            SamplerState = (MaterialSamplerState)expected[0],
            LmapLookupStart = PointLinearLookupStart
        };
    }

    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        LightDefAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "LightDef");
        string imageName = SourceOutput.NormalizeReferencedAssetName(
            asset.Image?.Name,
            $"LightDef '{assetName}' attenuation image");
        byte[] encodedImageName = EncodeLatin1(
            imageName,
            $"LightDef '{assetName}' attenuation image");
        var contents = new byte[checked(encodedImageName.Length + 2)];
        contents[0] = (byte)asset.SamplerState;
        encodedImageName.CopyTo(contents, 1);

        return new SourceOutput(sourceDirectory).WriteBinaryBatch([
            (
                $"lights/{assetName}",
                stream => stream.Write(contents))
        ]);
    }

    private static byte[] EncodeLatin1(
        string value,
        string field)
    {
        if (value.Any(character => character > byte.MaxValue))
        {
            throw new InvalidDataException(
                $"{field} cannot be represented as an IW4 Latin-1 string.");
        }

        return Encoding.Latin1.GetBytes(value);
    }
}
