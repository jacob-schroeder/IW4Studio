using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

public sealed class LeaderboardBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.LeaderboardDef;
    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not ILeaderboardBuildData data)
        {
            diagnostics.Add(new("body", "Leaderboard build data does not implement ILeaderboardBuildData.", rowIndex, AssetType));
            return diagnostics;
        }
        if (data.Name is { } leaderboardName && !AssetBodyEmitterHelpers.IsLatin1CString(leaderboardName))
            diagnostics.Add(new("name", "Leaderboard name must be a Latin-1 C string.", rowIndex, AssetType));
        for (int index = 0; index < data.Columns.Count; index++)
        {
            ILeaderboardColumnBuildData column = data.Columns[index];
            if (column.Name is { } columnName && !AssetBodyEmitterHelpers.IsLatin1CString(columnName)) diagnostics.Add(new($"columns[{index}].name", "Column name must be a Latin-1 C string.", rowIndex, AssetType));
            if (column.StatName is { } stat && !AssetBodyEmitterHelpers.IsLatin1CString(stat)) diagnostics.Add(new($"columns[{index}].statName", "Column statName must be a Latin-1 C string.", rowIndex, AssetType));
            if (column.GetPad0DTo0FCopy().Length != 3) diagnostics.Add(new($"columns[{index}].pad", "Leaderboard column pad must preserve exactly three bytes.", rowIndex, AssetType));
        }
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        ILeaderboardBuildData data = (ILeaderboardBuildData)buildData;
        var segments = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(0x18, 4);
        plan.Push(XFileBlockType.LARGE);
        IDictionary<string, EmissionAddress> aliases = plan.StringAliases;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, segments, aliases);
        EmissionAddress? table = data.Columns.Count == 0 ? null : plan.Allocate(checked(data.Columns.Count * 0x20), 4);
        var columns = new (PlannedString? Name, PlannedString? Stat)[data.Columns.Count];
        for (int index = 0; index < data.Columns.Count; index++)
            columns[index] = (AssetBodyEmitterHelpers.PlanString(data.Columns[index].Name, plan, segments, aliases), AssetBodyEmitterHelpers.PlanString(data.Columns[index].StatName, plan, segments, aliases));
        plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP);
        if (table is { } tableAddress)
        {
            var tableWriter = new XSourceWriter();
            for (int index = 0; index < data.Columns.Count; index++)
            {
                ILeaderboardColumnBuildData column = data.Columns[index];
                tableWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(columns[index].Name));
                tableWriter.WriteInt32(column.Id); tableWriter.WriteInt32(column.PropertyId); tableWriter.WriteByte(column.HiddenRaw); tableWriter.WriteBytes(column.GetPad0DTo0FCopy());
                tableWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(columns[index].Stat));
                tableWriter.WriteInt32(column.Type); tableWriter.WriteInt32(column.Precision); tableWriter.WriteInt32(column.Aggregation);
            }
            segments.Add(new(tableAddress, tableWriter.ToArray()));
        }
        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); rootWriter.WriteInt32(data.Id); rootWriter.WriteInt32(data.Columns.Count); rootWriter.WriteInt32(data.XpColumnId); rootWriter.WriteInt32(data.PrestigeColumnId); rootWriter.WriteInt32(table is null ? 0 : -1);
        segments.Add(new(root, rootWriter.ToArray()));
        return new AssetBodyEmission(AssetType, root, segments);
    }
}
