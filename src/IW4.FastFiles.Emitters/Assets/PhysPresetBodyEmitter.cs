using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

public sealed class PhysPresetBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.PhysPreset;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IPhysPresetBuildData data)
        {
            diagnostics.Add(new("body", "PhysPreset build data does not implement IPhysPresetBuildData.", rowIndex, AssetType));
            return diagnostics;
        }
        ValidateString(data.Name, "name", diagnostics, rowIndex);
        ValidateString(data.SndAliasPrefix, "sndAliasPrefix", diagnostics, rowIndex);
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IPhysPresetBuildData data = (IPhysPresetBuildData)buildData;
        var segments = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(0x2c, 4);
        plan.Push(XFileBlockType.LARGE);
        IDictionary<string, EmissionAddress> aliases = plan.StringAliases;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, segments, aliases);
        PlannedString? prefix = AssetBodyEmitterHelpers.PlanString(data.SndAliasPrefix, plan, segments, aliases);
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);
        var writer = new XSourceWriter();
        writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        writer.WriteInt32(data.Type);
        writer.WriteSingle(data.Mass); writer.WriteSingle(data.Bounce); writer.WriteSingle(data.Friction);
        writer.WriteSingle(data.BulletForceScale); writer.WriteSingle(data.ExplosiveForceScale);
        writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(prefix));
        writer.WriteSingle(data.PiecesSpreadFraction); writer.WriteSingle(data.PiecesUpwardVelocity);
        writer.WriteByte(data.TempDefaultToCylinder); writer.WriteByte(data.PerSurfaceSndAlias); writer.WriteUInt16(data.Pad2A);
        segments.Add(new(root, writer.ToArray()));
        return new AssetBodyEmission(AssetType, root, segments);
    }

    private static void ValidateString(string? value, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        if (value is { } text && !AssetBodyEmitterHelpers.IsLatin1CString(text))
            diagnostics.Add(new(path, "XString must be a Latin-1 C string.", rowIndex, XAssetType.PhysPreset));
    }
}
