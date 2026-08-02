using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>Complete GameMapMp root and its G_GlassData graph. The graph is
/// emitted as explicit roots/arrays/XStrings, never copied from source bytes.</summary>
public sealed class GameWorldMpBodyEmitter : IXAssetBodyEmitter
{
    private const int RootSize = 0x08;
    private const int GlassDataSize = 0x80;
    private const int GlassPieceSize = 0x0c;
    private const int GlassNameSize = 0x0c;

    public XAssetType AssetType => XAssetType.GameMapMp;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IGameWorldMpBuildData data)
        {
            diagnostics.Add(Error("body", "GameMapMp build data does not implement IGameWorldMpBuildData.", rowIndex));
            return diagnostics;
        }
        ValidateString(data.Name, "name", diagnostics, rowIndex);
        if (data.GlassData is not { } glass)
        {
            diagnostics.Add(Error(
                "glassData",
                "GameMapMp requires a non-null G_GlassData header.",
                rowIndex));
            return diagnostics;
        }
        if (glass.Pieces.Count > 0x10000)
            diagnostics.Add(Error("glassData.pieces", "Glass piece count exceeds the loader safety bound.", rowIndex));
        if (glass.Names.Count > 0x10000)
            diagnostics.Add(Error("glassData.names", "Glass-name count exceeds the loader safety bound.", rowIndex));
        if (glass.GetPad14To7FCopy().Length != 0x6c)
            diagnostics.Add(Error("glassData.pad14To7F", "Glass data requires exactly 0x6c preserved pad bytes.", rowIndex));
        if (glass.ImportedGlassNamesPointerRaw == 0 && glass.Names.Count != 0)
            diagnostics.Add(Error("glassData.glassNamesPointer", "A null imported glass-name pointer cannot own a non-empty name table.", rowIndex));
        for (int index = 0; index < glass.Names.Count; index++)
        {
            GGlassNameBuildData value = glass.Names[index];
            if (value.PieceIndices.Count > ushort.MaxValue)
                diagnostics.Add(Error($"glassData.names[{index}].pieceIndices", "Piece-index count exceeds the serialized ushort range.", rowIndex));
            if (value.PieceIndices.Any(piece => piece >= glass.Pieces.Count))
                diagnostics.Add(Error($"glassData.names[{index}].pieceIndices", "A piece index is outside the glass-piece array.", rowIndex));
            ValidateString(value.Name, $"glassData.names[{index}].name", diagnostics, rowIndex);
        }
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IGameWorldMpBuildData data = (IGameWorldMpBuildData)buildData;
        var segments = new List<EmissionBlockSegment>();
        var source = new List<EmissionBlockSegment>();

        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(RootSize, 4);
        plan.Push(XFileBlockType.LARGE);
        int beforeName = segments.Count;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, segments, plan.StringAliases);
        EmissionBlockSegment[] nameSegments = segments.Skip(beforeName).ToArray();
        GlassPlan? glass = data.GlassData is null ? null : PlanGlass(data.GlassData, plan, segments);
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        rootWriter.WriteInt32(glass is null ? 0 : -1);
        if (rootWriter.Position != RootSize) throw new InvalidDataException("GameMapMp root emission did not produce 0x08 bytes.");
        EmissionBlockSegment rootSegment = new(root, rootWriter.ToArray());
        segments.Add(rootSegment);
        source.Add(rootSegment);
        source.AddRange(nameSegments);
        if (glass is not null) source.AddRange(glass.SourceSegments);
        return new AssetBodyEmission(AssetType, root, segments, source);
    }

    private static GlassPlan PlanGlass(GGlassDataBuildData data, EmissionPlan plan, List<EmissionBlockSegment> segments)
    {
        EmissionAddress root = plan.Allocate(GlassDataSize, 4);
        EmissionAddress? piecesAddress = data.Pieces.Count == 0 ? null : plan.Allocate(checked(data.Pieces.Count * GlassPieceSize), 4);
        EmissionAddress? namesAddress = data.Names.Count == 0 ? null : plan.Allocate(checked(data.Names.Count * GlassNameSize), 4);
        var names = data.Names.ToArray();
        var plannedNames = new PlannedString?[names.Length];
        var indexAddresses = new EmissionAddress?[names.Length];
        var childSources = new List<EmissionBlockSegment>();
        for (int index = 0; index < names.Length; index++)
        {
            int beforeName = segments.Count;
            plannedNames[index] = AssetBodyEmitterHelpers.PlanString(names[index].Name, plan, segments, plan.StringAliases);
            childSources.AddRange(segments.Skip(beforeName));
            if (names[index].PieceIndices.Count != 0)
            {
                EmissionAddress address = plan.Allocate(checked(names[index].PieceIndices.Count * sizeof(ushort)), 2);
                var indexWriter = new XSourceWriter();
                foreach (ushort value in names[index].PieceIndices) indexWriter.WriteUInt16(value);
                EmissionBlockSegment indices = new(address, indexWriter.ToArray());
                segments.Add(indices); childSources.Add(indices); indexAddresses[index] = address;
            }
        }

        if (piecesAddress is { } pieceAddress)
        {
            var writer = new XSourceWriter();
            foreach (GGlassPieceBuildData piece in data.Pieces)
            { writer.WriteUInt16(piece.DamageTaken); writer.WriteUInt16(piece.CollapseTime); writer.WriteInt32(piece.LastStateChangeTime); writer.WriteUInt16(piece.PackedImpactDir); writer.WriteUInt16(piece.PackedImpactPos); }
            segments.Add(new EmissionBlockSegment(pieceAddress, writer.ToArray()));
        }
        if (namesAddress is { } nameAddress)
        {
            var writer = new XSourceWriter();
            for (int index = 0; index < names.Length; index++)
            { writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(plannedNames[index])); writer.WriteUInt16(ScriptStringEmissionScope.ResolveOpaqueImportedRaw(names[index].ScriptString, $"glassData.names[{index}].scriptString")); writer.WriteUInt16(checked((ushort)names[index].PieceIndices.Count)); writer.WriteInt32(indexAddresses[index] is null ? 0 : -1); }
            segments.Add(new EmissionBlockSegment(nameAddress, writer.ToArray()));
        }
        var rootWriter = new XSourceWriter();
        int glassNamesPointerRaw =
            plan.PreserveImportedXAssetPointerValues &&
            data.ImportedGlassNamesPointerRaw is { } importedRaw
                ? importedRaw
                : namesAddress is null ? 0 : -1;
        rootWriter.WriteInt32(piecesAddress is null ? 0 : -1); rootWriter.WriteInt32(data.Pieces.Count); rootWriter.WriteUInt16(data.DamageToWeaken); rootWriter.WriteUInt16(data.DamageToDestroy); rootWriter.WriteInt32(data.Names.Count); rootWriter.WriteInt32(glassNamesPointerRaw); rootWriter.WriteBytes(data.GetPad14To7FCopy());
        if (rootWriter.Position != GlassDataSize) throw new InvalidDataException("G_GlassData root emission did not produce 0x80 bytes.");
        EmissionBlockSegment rootSegment = new(root, rootWriter.ToArray()); segments.Add(rootSegment);

        // G_GlassData consumes root, pieces, names, then each name string and
        // its piece-index list.  Keep that sequence explicit.
        var source = new List<EmissionBlockSegment> { rootSegment };
        if (piecesAddress is { } piecesBlock) source.Add(segments.Single(segment => segment.Address == piecesBlock));
        if (namesAddress is { } namesBlock) source.Add(segments.Single(segment => segment.Address == namesBlock));
        source.AddRange(childSources);
        return new GlassPlan(source);
    }

    private static void ValidateString(string? value, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        if (value is not null && !AssetBodyEmitterHelpers.IsLatin1CString(value)) diagnostics.Add(Error(path, "XString must be a Latin-1 C string.", rowIndex));
    }
    private static EmissionError Error(string path, string message, int? rowIndex) => new(path, message, rowIndex, XAssetType.GameMapMp);
    private sealed record GlassPlan(IReadOnlyList<EmissionBlockSegment> SourceSegments);
}
