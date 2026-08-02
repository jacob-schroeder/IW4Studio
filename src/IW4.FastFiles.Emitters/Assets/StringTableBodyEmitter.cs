using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

public sealed class StringTableBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.StringTable;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(buildData);
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IStringTableBuildData table)
        {
            diagnostics.Add(new EmissionError("body", "StringTable build data does not implement IStringTableBuildData.", rowIndex, AssetType));
            return diagnostics;
        }

        if (table.Name is { } name && !AssetBodyEmitterHelpers.IsLatin1CString(name))
            diagnostics.Add(new EmissionError("name", "StringTable name contains an embedded null or non-Latin-1 character.", rowIndex, AssetType));
        int expected = -1;
        try { expected = checked(table.RowCount * table.ColumnCount); }
        catch (OverflowException) { }
        if (table.RowCount < 0 || table.ColumnCount < 0 || expected < 0 || expected > 0x100000)
            diagnostics.Add(new EmissionError("dimensions", "StringTable dimensions are out of the loader-supported range.", rowIndex, AssetType));
        else if (table.Cells.Count != expected)
            diagnostics.Add(new EmissionError("cells", "StringTable cell count does not equal rowCount × columnCount.", rowIndex, AssetType));
        for (int index = 0; index < table.Cells.Count; index++)
        {
            if (table.Cells[index].Value is { } value && !AssetBodyEmitterHelpers.IsLatin1CString(value))
                diagnostics.Add(new EmissionError($"cells[{index}].value", "Cell value contains an embedded null or non-Latin-1 character.", rowIndex, AssetType));
        }
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IStringTableBuildData table = (IStringTableBuildData)buildData;
        var segments = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(0x10, alignment: 4);
        plan.Push(XFileBlockType.LARGE);
        IDictionary<string, EmissionAddress> aliases = plan.StringAliases;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(table.Name, plan, segments, aliases);
        EmissionAddress? cells = table.Cells.Count == 0 ? null : plan.Allocate(checked(table.Cells.Count * 0x08), alignment: 4);
        var cellStrings = new PlannedString?[table.Cells.Count];
        for (int index = 0; index < table.Cells.Count; index++)
            cellStrings[index] = AssetBodyEmitterHelpers.PlanString(table.Cells[index].Value, plan, segments, aliases);
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        if (cells is { } cellsAddress)
        {
            var cellWriter = new XSourceWriter();
            for (int index = 0; index < table.Cells.Count; index++)
            {
                cellWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(cellStrings[index]));
                cellWriter.WriteInt32(table.Cells[index].Hash);
            }
            segments.Add(new EmissionBlockSegment(cellsAddress, cellWriter.ToArray()));
        }

        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        rootWriter.WriteInt32(table.ColumnCount);
        rootWriter.WriteInt32(table.RowCount);
        rootWriter.WriteInt32(cells is null ? 0 : -1);
        segments.Add(new EmissionBlockSegment(root, rootWriter.ToArray()));
        return new AssetBodyEmission(AssetType, root, segments);
    }
}
