using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

public sealed class SndCurveBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.SndCurve;
    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not ISndCurveBuildData data)
        {
            diagnostics.Add(new("body", "SndCurve build data does not implement ISndCurveBuildData.", rowIndex, AssetType));
            return diagnostics;
        }
        if (data.Filename is { } filename && !AssetBodyEmitterHelpers.IsLatin1CString(filename))
            diagnostics.Add(new("filename", "SndCurve filename must be a Latin-1 C string.", rowIndex, AssetType));
        if (data.KnotCount > 16)
            diagnostics.Add(new("knotCount", "SndCurve KnotCount must be in the 0..16 fixed-slot range.", rowIndex, AssetType));
        if (data.Knots.Count != 16)
            diagnostics.Add(new("knots", "SndCurve requires all sixteen serialized knot slots, including unused slots.", rowIndex, AssetType));
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        ISndCurveBuildData data = (ISndCurveBuildData)buildData;
        var segments = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(0x88, 4);
        plan.Push(XFileBlockType.LARGE);
        PlannedString? filename = AssetBodyEmitterHelpers.PlanString(data.Filename, plan, segments, plan.StringAliases);
        plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP);
        var writer = new XSourceWriter();
        writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(filename));
        writer.WriteUInt16(data.KnotCount); writer.WriteUInt16(data.Padding);
        foreach (SndCurveKnotBuildData knot in data.Knots) { writer.WriteSingle(knot.X); writer.WriteSingle(knot.Y); }
        segments.Add(new(root, writer.ToArray()));
        return new AssetBodyEmission(AssetType, root, segments);
    }
}
