using System.Collections.ObjectModel;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Scheduling.Lifecycle;

/// <summary>
/// Exact backend-neutral PS3 recipe for producing target 5 FloatZ and target
/// 8 ProcessedFloatZ from their canonical fullscreen materials.
/// </summary>
public sealed class MapRenderNormalCameraFloatZRecipe
{
    public const uint StateBits0 = 0x1812_8812;
    public const uint StateBits1 = 0xE00E_0002;
    public const int TechniqueSlot = (int)MaterialTechniqueType.Unlit;
    public const MaterialTechniqueFlags TechniqueFlags =
        MaterialTechniqueFlags.UsesFloatZ;

    private readonly MapRenderNormalCameraFloatZTargetPlan[] _targets;

    private MapRenderNormalCameraFloatZRecipe(
        IReadOnlyList<MapRenderNormalCameraFloatZTargetPlan> targets,
        MapRenderNormalCameraMaterialAssetContract floatZ,
        MapRenderNormalCameraMaterialAssetContract processedFloatZ)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(floatZ);
        ArgumentNullException.ThrowIfNull(processedFloatZ);

        _targets = targets.ToArray();
        if (!_targets.Select(target => target.Kind).SequenceEqual(
                [
                    MapRenderNormalCameraTargetKind.FloatZ,
                    MapRenderNormalCameraTargetKind.ProcessedFloatZ
                ]) ||
            _targets[0].RawProgramImageSlot != 2 ||
            _targets[1].RawProgramImageSlot != 5 ||
            _targets.Any(target =>
                target.Dimensions !=
                    MapRenderNormalCameraTargetDimensions
                        .HalfDisplayShiftClamp ||
                target.Ps3SurfaceSampleCount != 1 ||
                target.RawImageSetupFormat != 0x01aa_e49c ||
                target.ImageSetupFlags !=
                    (MapRenderNormalCameraImageSetupFlags.NoPicmip |
                     MapRenderNormalCameraImageSetupFlags.NoMipmaps) ||
                target.ImageFormat != new GfxImageFormat(
                    (byte)((byte)GfxImageBaseFormat.X32Float |
                        (byte)GfxImageFormatFlags.Linear)) ||
                target.SurfaceColorFormat !=
                    RsxSurfaceColorFormat.FloatX32))
        {
            throw new ArgumentException(
                "FloatZ requires exact half-display single-sample target rows 5 and 8.",
                nameof(targets));
        }

        RequireExactMaterial(
            floatZ,
            "$floatz",
            "floatz",
            "floatz.hlsl");
        RequireExactMaterial(
            processedFloatZ,
            "$processed_floatz",
            "processed_floatz",
            "processed_floatz.hlsl");

        Targets = Array.AsReadOnly(_targets);
        FloatZ = floatZ;
        ProcessedFloatZ = processedFloatZ;
    }

    public static MapRenderNormalCameraFloatZRecipe Current { get; } =
        Create();

    public ReadOnlyCollection<MapRenderNormalCameraFloatZTargetPlan> Targets
        { get; }

    public MapRenderNormalCameraFloatZTargetPlan FloatZTarget =>
        _targets[0];

    public MapRenderNormalCameraFloatZTargetPlan ProcessedFloatZTarget =>
        _targets[1];

    public MapRenderNormalCameraMaterialAssetContract FloatZ { get; }

    public MapRenderNormalCameraMaterialAssetContract ProcessedFloatZ
        { get; }

    public MapRenderNormalCameraFloatZTargetPlan GetTarget(
        MapRenderNormalCameraTargetKind kind)
    {
        if (kind is not MapRenderNormalCameraTargetKind.FloatZ and
            not MapRenderNormalCameraTargetKind.ProcessedFloatZ)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return _targets.Single(target => target.Kind == kind);
    }

    private static MapRenderNormalCameraFloatZRecipe Create()
    {
        MapRenderNormalCameraMaterialAssetContract floatZ = Material(
            "$floatz",
            "floatz",
            "floatz.hlsl",
            [
                CodeArgument(
                    MaterialShaderArgumentType.CodeVertexConst,
                    0,
                    MaterialConstantSource.WorldViewProjectionMatrix0,
                    rowCount: 4),
                CodeArgument(
                    MaterialShaderArgumentType.CodeVertexConst,
                    17,
                    MaterialConstantSource.ClipSpaceLookupScale),
                CodeArgument(
                    MaterialShaderArgumentType.CodeVertexConst,
                    18,
                    MaterialConstantSource.ClipSpaceLookupOffset),
                TextureArgument(
                    0,
                    MaterialTextureSource.RawFloatZ)
            ]);
        MapRenderNormalCameraMaterialAssetContract processedFloatZ = Material(
            "$processed_floatz",
            "processed_floatz",
            "processed_floatz.hlsl",
            [
                CodeArgument(
                    MaterialShaderArgumentType.CodeVertexConst,
                    0,
                    MaterialConstantSource.WorldViewProjectionMatrix0,
                    rowCount: 4),
                CodeArgument(
                    MaterialShaderArgumentType.CodeVertexConst,
                    17,
                    MaterialConstantSource.ClipSpaceLookupScale),
                CodeArgument(
                    MaterialShaderArgumentType.CodeVertexConst,
                    18,
                    MaterialConstantSource.ClipSpaceLookupOffset),
                TextureArgument(
                    0,
                    MaterialTextureSource.FloatZ),
                CodeArgument(
                    MaterialShaderArgumentType.CodePixelConst,
                    1,
                    MaterialConstantSource.ZNear)
            ]);

        return new MapRenderNormalCameraFloatZRecipe(
            [
                new MapRenderNormalCameraFloatZTargetPlan(
                    MapRenderNormalCameraTargetKind.FloatZ),
                new MapRenderNormalCameraFloatZTargetPlan(
                    MapRenderNormalCameraTargetKind.ProcessedFloatZ)
            ],
            floatZ,
            processedFloatZ);
    }

    private static MapRenderNormalCameraMaterialAssetContract Material(
        string materialName,
        string programName,
        string shaderName,
        IReadOnlyList<MapRenderNormalCameraMaterialArgumentContract> arguments)
        => new(
            materialName,
            programName,
            programName,
            TechniqueSlot,
            TechniqueFlags,
            shaderName,
            shaderName,
            StateBits0,
            StateBits1,
            arguments);

    private static MapRenderNormalCameraMaterialArgumentContract CodeArgument(
        MaterialShaderArgumentType type,
        ushort destination,
        MaterialConstantSource source,
        byte firstRow = 0,
        byte rowCount = 1) => new(
            type,
            destination,
            new MaterialCodeConstantArgument(
                source,
                firstRow,
                rowCount).PackedValue);

    private static MapRenderNormalCameraMaterialArgumentContract
        TextureArgument(
            ushort destination,
            MaterialTextureSource source) => new(
                MaterialShaderArgumentType.CodePixelSampler,
                destination,
                (uint)source);

    private static void RequireExactMaterial(
        MapRenderNormalCameraMaterialAssetContract contract,
        string materialName,
        string programName,
        string shaderName)
    {
        if (!string.Equals(
                contract.MaterialName,
                materialName,
                StringComparison.Ordinal) ||
            !string.Equals(
                contract.TechniqueSetName,
                programName,
                StringComparison.Ordinal) ||
            !string.Equals(
                contract.TechniqueName,
                programName,
                StringComparison.Ordinal) ||
            contract.TechniqueSlot != TechniqueSlot ||
            contract.TechniqueFlags != TechniqueFlags ||
            contract.PassCount != 1 ||
            !string.Equals(
                contract.VertexShaderName,
                shaderName,
                StringComparison.Ordinal) ||
            !string.Equals(
                contract.PixelShaderName,
                shaderName,
                StringComparison.Ordinal) ||
            contract.StateBits0 != StateBits0 ||
            contract.StateBits1 != StateBits1)
        {
            throw new ArgumentException(
                $"FloatZ material '{materialName}' no longer matches its exact PS3 identity.");
        }
    }
}
