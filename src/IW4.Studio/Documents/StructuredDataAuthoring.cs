using IW4.Assets.Assets.StructuredData;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>Detached StructuredData projection captured from the fully
/// materialized runtime graph. Source traversal fragments are never replayed
/// to construct authoring data.</summary>
public sealed class StructuredDataAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal StructuredDataAuthoredSnapshot(StructuredDataBuildData buildData) => BuildData = Copy(buildData);
    internal StructuredDataBuildData BuildData { get; }
    public XAssetType AssetType => XAssetType.StructuredDataDef;

    internal static StructuredDataAuthoredSnapshot Import(TargetZoneRowSource source) =>
        source.AuthoredDefinition?.SemanticSnapshot is StructuredDataAuthoredSnapshot snapshot
            ? new StructuredDataAuthoredSnapshot(snapshot.BuildData)
            : throw new InvalidDataException("StructuredData editing requires a capture-time detached semantic snapshot; source-fragment replay is not an authoring input.");

    internal static StructuredDataAuthoredSnapshot FromLoaded(StructuredDataDefSetAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return new StructuredDataAuthoredSnapshot(new StructuredDataBuildData(
            asset.Name,
            asset.Defs.Select(definition => new StructuredDataDefinitionBuildData(
                definition.Version,
                definition.FormatChecksum,
                definition.Enums.Select(@enum => new StructuredDataEnumBuildData(
                    @enum.ReservedEntryCount,
                    @enum.Entries.Select(entry => new StructuredDataEnumEntryBuildData(entry.String, entry.Index, entry.Padding)))),
                definition.Structs.Select(@struct => new StructuredDataStructBuildData(
                    @struct.Size,
                    @struct.BitOffset,
                    @struct.Properties.Select(property => new StructuredDataPropertyBuildData(
                        property.Name,
                        Type(property.Type),
                        property.Offset)))),
                definition.IndexedArrays.Select(array => new StructuredDataIndexedArrayBuildData(
                    array.ArraySize,
                    Type(array.ElementType),
                    array.ElementSize)),
                definition.EnumedArrays.Select(array => new StructuredDataEnumedArrayBuildData(
                    array.EnumIndex,
                    Type(array.ElementType),
                    array.ElementSize)),
                Type(definition.RootType),
                definition.Size,
                definition.IndexedArraysPointer.Raw != 0,
                definition.EnumedArraysPointer.Raw != 0))));
    }

    private static StructuredDataTypeBuildData Type(StructuredDataType value) =>
        new(value.Type, value.UnionValue);

    private static StructuredDataBuildData Copy(StructuredDataBuildData value) => new(
        value.Name,
        value.Definitions.Select(definition => new StructuredDataDefinitionBuildData(
            definition.Version,
            definition.FormatChecksum,
            definition.Enums.Select(@enum => new StructuredDataEnumBuildData(
                @enum.ReservedEntryCount,
                @enum.Entries.Select(entry => new StructuredDataEnumEntryBuildData(entry.Value, entry.Index, entry.Padding)))),
            definition.Structs.Select(@struct => new StructuredDataStructBuildData(
                @struct.Size,
                @struct.BitOffset,
                @struct.Properties.Select(property => new StructuredDataPropertyBuildData(
                    property.Name,
                    new StructuredDataTypeBuildData(property.Type.Category, property.Type.UnionValue),
                    property.Offset)))),
            definition.IndexedArrays.Select(array => new StructuredDataIndexedArrayBuildData(
                array.ArraySize,
                new StructuredDataTypeBuildData(array.ElementType.Category, array.ElementType.UnionValue),
                array.ElementSize)),
            definition.EnumedArrays.Select(array => new StructuredDataEnumedArrayBuildData(
                array.EnumIndex,
                new StructuredDataTypeBuildData(array.ElementType.Category, array.ElementType.UnionValue),
                array.ElementSize)),
            new StructuredDataTypeBuildData(definition.RootType.Category, definition.RootType.UnionValue),
            definition.Size,
            definition.IndexedArraysPresent,
            definition.EnumedArraysPresent)));
}

public sealed class StructuredDataDraft
{
    private StructuredDataBuildData _data;
    internal StructuredDataDraft(StructuredDataBuildData data) => _data = data ?? throw new ArgumentNullException(nameof(data));
    public StructuredDataBuildData BuildData => _data;
    public void ReplaceDefinition(int index, StructuredDataDefinitionBuildData definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if ((uint)index >= (uint)_data.Definitions.Count) throw new ArgumentOutOfRangeException(nameof(index));
        StructuredDataDefinitionBuildData[] definitions = _data.Definitions.ToArray();
        definitions[index] = definition;
        _data = new StructuredDataBuildData(_data.Name, definitions);
    }
    internal StructuredDataDraft Clone() => new(new StructuredDataAuthoredSnapshot(_data).BuildData);
}

public sealed class StructuredDataAuthoringAdapter : AssetAuthoringAdapter<StructuredDataAuthoredSnapshot, StructuredDataDraft, StructuredDataBuildData>
{
    private static readonly StructuredDataBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.StructuredDataDef;
    public override StructuredDataAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => StructuredDataAuthoredSnapshot.Import(source);
    public override StructuredDataDraft CreateDraft(StructuredDataAuthoredSnapshot authoredSnapshot) => new(authoredSnapshot.BuildData);
    public override StructuredDataDraft CloneDraft(StructuredDataDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(StructuredDataDraft draft) => Validator.Validate(draft.BuildData).Select(issue => new AssetValidationIssue(issue.Path, issue.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(StructuredDataDraft baseline, StructuredDataDraft current) =>
        System.Text.Json.JsonSerializer.Serialize(baseline.BuildData) == System.Text.Json.JsonSerializer.Serialize(current.BuildData);
    public override StructuredDataBuildData ExportBuildData(StructuredDataDraft draft)
    {
        if (ValidateDraft(draft).Count != 0) throw new InvalidOperationException("StructuredData draft has validation errors and cannot produce build data.");
        return new StructuredDataAuthoredSnapshot(draft.BuildData).BuildData;
    }
}
