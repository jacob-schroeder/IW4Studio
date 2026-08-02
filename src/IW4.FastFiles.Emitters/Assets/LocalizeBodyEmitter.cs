using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

public sealed class LocalizeBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.Localize;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(buildData);
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not ILocalizeBuildData localize)
        {
            diagnostics.Add(new EmissionError("body", "Localize build data does not implement ILocalizeBuildData.", rowIndex, AssetType));
            return diagnostics;
        }

        if (localize.Name is { } name && !AssetBodyEmitterHelpers.IsLatin1CString(name))
            diagnostics.Add(new EmissionError("name", "Localize name contains an embedded null or non-Latin-1 character.", rowIndex, AssetType));
        if (localize.Value is { } value && !AssetBodyEmitterHelpers.IsLatin1CString(value))
            diagnostics.Add(new EmissionError("value", "Localize value contains an embedded null or non-Latin-1 character.", rowIndex, AssetType));
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        ILocalizeBuildData localize = (ILocalizeBuildData)buildData;
        var segments = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(0x08, alignment: 4);
        plan.Push(XFileBlockType.LARGE);
        IDictionary<string, EmissionAddress> aliases = plan.StringAliases;
        PlannedString? value = AssetBodyEmitterHelpers.PlanString(localize.Value, plan, segments, aliases);
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(localize.Name, plan, segments, aliases);
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(value));
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        segments.Add(new EmissionBlockSegment(root, rootWriter.ToArray()));
        return new AssetBodyEmission(AssetType, root, segments);
    }
}
