using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>TracerDef root plus the only currently safe nested Material form:
/// an explicit comma-prefixed Material reference placeholder.</summary>
public sealed class TracerBodyEmitter : IXAssetBodyEmitter
{
    private const int MaterialSerializedSize = 0xA8;
    public XAssetType AssetType => XAssetType.Tracer;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not ITracerBuildData data)
        {
            diagnostics.Add(new("body", "Tracer build data does not implement ITracerBuildData.", rowIndex, AssetType));
            return diagnostics;
        }
        ValidateString(data.Name, "name", diagnostics, rowIndex);
        if (data.Colors.Count != 5)
            diagnostics.Add(new("colors", "Tracer requires exactly five serialized color rows.", rowIndex, AssetType));
        ValidateExternalReference(data.MaterialReference, XAssetType.Material, "material", diagnostics, rowIndex);
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        ITracerBuildData data = (ITracerBuildData)buildData;
        var segments = new List<EmissionBlockSegment>();
        var sourceSegments = new List<EmissionBlockSegment>();

        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(0x70, 4);
        plan.Push(XFileBlockType.LARGE);
        int beforeName = segments.Count;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, segments, plan.StringAliases);
        plan.Pop(XFileBlockType.LARGE);

        EmissionAddress? materialRoot = null;
        PlannedString? materialName = null;
        int beforeMaterialName = segments.Count;
        if (data.MaterialReference is { } material)
        {
            materialRoot = plan.Allocate(MaterialSerializedSize, 4);
            plan.Push(XFileBlockType.LARGE);
            materialName = AssetBodyEmitterHelpers.PlanString(material.OriginalSerializedName, plan, segments, plan.StringAliases);
            plan.Pop(XFileBlockType.LARGE);
        }
        int afterMaterialName = segments.Count;
        plan.Pop(XFileBlockType.TEMP);

        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        rootWriter.WriteInt32(materialRoot is null ? 0 : -1);
        rootWriter.WriteUInt32(data.DrawInterval);
        rootWriter.WriteSingle(data.Speed); rootWriter.WriteSingle(data.BeamLength); rootWriter.WriteSingle(data.BeamWidth);
        rootWriter.WriteSingle(data.ScrewRadius); rootWriter.WriteSingle(data.ScrewDistance);
        foreach (TracerColorBuildData color in data.Colors)
        {
            rootWriter.WriteSingle(color.Red); rootWriter.WriteSingle(color.Green);
            rootWriter.WriteSingle(color.Blue); rootWriter.WriteSingle(color.Alpha);
        }
        var rootSegment = new EmissionBlockSegment(root, rootWriter.ToArray());
        segments.Add(rootSegment);
        sourceSegments.Add(rootSegment);
        sourceSegments.AddRange(segments.Skip(beforeName).Take(beforeMaterialName - beforeName));
        if (materialRoot is { } nestedRoot)
        {
            var materialWriter = new XSourceWriter();
            materialWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(materialName));
            materialWriter.Reserve(MaterialSerializedSize - sizeof(int));
            var materialSegment = new EmissionBlockSegment(nestedRoot, materialWriter.ToArray());
            segments.Add(materialSegment);
            sourceSegments.Add(materialSegment);
            sourceSegments.AddRange(segments.Skip(beforeMaterialName).Take(afterMaterialName - beforeMaterialName));
        }
        return new AssetBodyEmission(AssetType, root, segments, sourceSegments);
    }

    private static void ValidateString(string? value, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        if (value is { } text && !AssetBodyEmitterHelpers.IsLatin1CString(text))
            diagnostics.Add(new(path, "XString must be a Latin-1 C string.", rowIndex, XAssetType.Tracer));
    }

    internal static void ValidateExternalReference(SymbolicXAssetReference? reference, XAssetType type, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        if (reference is null)
            return;
        if (reference.AssetType != type)
            diagnostics.Add(new(path, $"Reference must have serialized type '{type}', not '{reference.AssetType}'.", rowIndex, XAssetType.Tracer));
        else if (!reference.IsExternalReference || !AssetBodyEmitterHelpers.IsLatin1CString(reference.OriginalSerializedName))
            diagnostics.Add(new(path, "Only a comma-prefixed Latin-1 external reference is supported.", rowIndex, XAssetType.Tracer));
    }
}
