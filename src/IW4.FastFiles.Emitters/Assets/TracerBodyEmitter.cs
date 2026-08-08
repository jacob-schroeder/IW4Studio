using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>TracerDef root and its nested Material pointer topology.</summary>
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
        if (data.MaterialLink is { } materialLink)
        {
            diagnostics.AddRange(NestedXAssetEmission.Validate(
                materialLink,
                XAssetType.Material,
                "material",
                rowIndex,
                AssetType));
            if (data.MaterialReference != materialLink.Reference)
            {
                diagnostics.Add(new(
                    "material",
                    "Retained Material link identity must equal the parallel Material reference.",
                    rowIndex,
                    AssetType));
            }
        }
        else
        {
            ValidateExternalReference(data.MaterialReference, XAssetType.Material, "material", diagnostics, rowIndex);
        }
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
        int afterName = segments.Count;
        plan.Pop(XFileBlockType.LARGE);

        MaterialPlan material = PlanMaterial(
            data.MaterialReference,
            data.MaterialLink,
            plan,
            segments,
            new EmissionAddress(root.Block, checked(root.Offset + sizeof(int))));
        plan.Pop(XFileBlockType.TEMP);

        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        rootWriter.WriteInt32(material.PointerRaw);
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
        sourceSegments.AddRange(segments.Skip(beforeName).Take(afterName - beforeName));
        sourceSegments.AddRange(material.SourceSegments);
        return new AssetBodyEmission(AssetType, root, segments, sourceSegments);
    }

    private static MaterialPlan PlanMaterial(
        SymbolicXAssetReference? reference,
        NestedXAssetBuildLink? link,
        EmissionPlan plan,
        List<EmissionBlockSegment> all,
        EmissionAddress ownerCell)
    {
        if (link is { } retained)
        {
            NestedXAssetPlan nested = NestedXAssetEmission.Plan(
                retained,
                plan,
                all,
                ownerCell,
                "Tracer.Material");
            return new MaterialPlan(nested.PointerRaw, nested.Source);
        }
        if (reference is null)
            return new MaterialPlan(0, []);

        EmissionAddress root = plan.Allocate(MaterialSerializedSize, 4);
        plan.Push(XFileBlockType.LARGE);
        int beforeName = all.Count;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(
            reference.OriginalSerializedName,
            plan,
            all,
            plan.StringAliases);
        EmissionBlockSegment[] nameSource = all.Skip(beforeName).ToArray();
        plan.Pop(XFileBlockType.LARGE);
        var writer = new XSourceWriter();
        writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        writer.Reserve(MaterialSerializedSize - sizeof(int));
        var rootSegment = new EmissionBlockSegment(root, writer.ToArray());
        all.Add(rootSegment);
        return new MaterialPlan(-1, [rootSegment, .. nameSource]);
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

    private sealed record MaterialPlan(
        int PointerRaw,
        IReadOnlyList<EmissionBlockSegment> SourceSegments);
}
