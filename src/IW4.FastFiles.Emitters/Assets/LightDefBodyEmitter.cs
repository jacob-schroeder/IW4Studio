using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>LightDef root plus an explicit nested comma-prefixed GfxImage
/// placeholder; Image bodies themselves remain a later binary-resource slice.</summary>
public sealed class LightDefBodyEmitter : IXAssetBodyEmitter
{
    private const int GfxImageSerializedSize = 0x50;
    public XAssetType AssetType => XAssetType.LightDef;
    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not ILightDefBuildData data)
        {
            diagnostics.Add(new("body", "LightDef build data does not implement ILightDefBuildData.", rowIndex, AssetType));
            return diagnostics;
        }
        if (data.Name is { } name && !AssetBodyEmitterHelpers.IsLatin1CString(name)) diagnostics.Add(new("name", "XString must be a Latin-1 C string.", rowIndex, AssetType));
        if (data.ImageLink is { } imageLink)
        {
            diagnostics.AddRange(NestedXAssetEmission.Validate(
                imageLink,
                XAssetType.Image,
                "image",
                rowIndex,
                AssetType));
        }
        else
        {
            TracerBodyEmitter.ValidateExternalReference(
                data.ImageReference,
                XAssetType.Image,
                "image",
                diagnostics,
                rowIndex);
        }
        if (data.GetPad09To0BCopy().Length != 3) diagnostics.Add(new("pad09To0B", "LightDef padding must preserve exactly three bytes.", rowIndex, AssetType));
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        ILightDefBuildData data = (ILightDefBuildData)buildData;
        var segments = new List<EmissionBlockSegment>(); var sourceSegments = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(0x10, 4);
        plan.Push(XFileBlockType.LARGE); int beforeName = segments.Count;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, segments, plan.StringAliases);
        int afterName = segments.Count;
        plan.Pop(XFileBlockType.LARGE);
        EmissionAddress? imageRoot = null;
        PlannedString? imageName = null;
        int beforeImageName = segments.Count;
        int afterImageName = beforeImageName;
        int imageRaw = 0;
        IReadOnlyList<EmissionBlockSegment> linkedImageSource = [];
        if (data.ImageLink is { } imageLink)
        {
            EmissionAddress ownerCell = new(
                root.Block,
                checked(root.Offset + sizeof(int)));
            NestedXAssetPlan nested = NestedXAssetEmission.Plan(
                imageLink,
                plan,
                segments,
                ownerCell,
                "LightDef.Image");
            imageRaw = nested.PointerRaw;
            linkedImageSource = nested.Source;
        }
        else if (data.ImageReference is { } image)
        {
            imageRoot = plan.Allocate(GfxImageSerializedSize, 4);
            plan.Push(XFileBlockType.LARGE);
            imageName = AssetBodyEmitterHelpers.PlanString(image.OriginalSerializedName, plan, segments, plan.StringAliases);
            plan.Pop(XFileBlockType.LARGE);
            afterImageName = segments.Count;
            imageRaw = -1;
        }
        plan.Pop(XFileBlockType.TEMP);
        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); rootWriter.WriteInt32(imageRaw);
        rootWriter.WriteByte(data.SamplerState); rootWriter.WriteBytes(data.GetPad09To0BCopy()); rootWriter.WriteUInt32(data.LmapLookupStart);
        var rootSegment = new EmissionBlockSegment(root, rootWriter.ToArray()); segments.Add(rootSegment); sourceSegments.Add(rootSegment);
        sourceSegments.AddRange(
            segments.Skip(beforeName).Take(afterName - beforeName));
        if (data.ImageLink is not null)
        {
            sourceSegments.AddRange(linkedImageSource);
        }
        else if (imageRoot is { } nestedRoot)
        {
            var imageWriter = new XSourceWriter(); imageWriter.Reserve(GfxImageSerializedSize - sizeof(int)); imageWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(imageName));
            var imageSegment = new EmissionBlockSegment(nestedRoot, imageWriter.ToArray()); segments.Add(imageSegment); sourceSegments.Add(imageSegment);
            sourceSegments.AddRange(segments.Skip(beforeImageName).Take(afterImageName - beforeImageName));
        }
        return new AssetBodyEmission(AssetType, root, segments, sourceSegments);
    }
}
