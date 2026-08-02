using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Zone;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Authority for target-owned support rows that are not semantic map roots.
/// These rows may enter the managed dependency candidate only; they do not
/// grant retail startup or editor persistence authority.
/// </summary>
public enum MinimalMultiplayerMapRuntimeSupportAuthority
{
    ManagedStartupContractOnly = 0
}

public enum MinimalMultiplayerMapLevelScriptDisposition
{
    OwnedMinimalMain = 0
}

/// <summary>
/// Runtime mode in which the bounded target artifact may be exercised.
/// This is an acceptance-test profile, not a general multiplayer launch
/// configuration.
/// </summary>
public enum MinimalMultiplayerMapTargetStartupProfile
{
    OfflineSplitScreenFreeForAll = 0
}

public enum MinimalMultiplayerMapConstantConfigStringDisposition
{
    OwnedMinimumMapIdentityTable = 0,
    OwnedRetargetedRetailFreeForAllTable = 1
}

public enum MinimalMultiplayerMapRuntimeSupportBlockerKind
{
    RetailStartupNotAccepted = 0
}

public sealed record MinimalMultiplayerMapRuntimeSupportBlocker(
    MinimalMultiplayerMapRuntimeSupportBlockerKind Kind,
    string Detail);

/// <summary>
/// Target-owned runtime support kept outside the five-root compiled map
/// graph. The diagnostic marker and a deterministic minimal level script are
/// unconditional. The split-screen profile also owns the exact sparse
/// two-column constant-configstring table required to retain the map identity.
/// It does not copy a retail map's generated constant-string snapshot.
/// </summary>
public sealed class MinimalMultiplayerMapRuntimeSupportCompilation
{
    private readonly IReadOnlyList<ZoneAssetKey> _ownedAssetKeys;
    private readonly IReadOnlyList<IXAssetBuildData> _ownedBuildData;
    private readonly IReadOnlyList<
        MinimalMultiplayerMapRuntimeSupportBlocker> _blockers;

    internal MinimalMultiplayerMapRuntimeSupportCompilation(
        string targetZoneName,
        MinimalMultiplayerMapTargetStartupProfile startupProfile,
        MapPrimaryChecksum primaryChecksum,
        IStringTableBuildData constantConfigStringTable,
        MinimalMultiplayerMapLevelScriptBuildData levelScript,
        MinimalMultiplayerMapFastFileDiagnosticMarkerBuildData
            diagnosticMarker,
        MapGameplayModelSupportCompilation? gameplayModelSupport = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZoneName);
        TargetZoneName = targetZoneName;
        StartupProfile = startupProfile;
        PrimaryChecksum = primaryChecksum;
        ConstantConfigStringTable =
            constantConfigStringTable ??
            throw new ArgumentNullException(
                nameof(constantConfigStringTable));
        LevelScript = levelScript ??
            throw new ArgumentNullException(nameof(levelScript));
        DiagnosticMarker = diagnosticMarker ??
            throw new ArgumentNullException(nameof(diagnosticMarker));
        GameplayModelSupport = gameplayModelSupport;
        var ownedRows = new List<(
            ZoneAssetKey Key,
            IXAssetBuildData BuildData)>
        {
            (
                new ZoneAssetKey(
                    XAssetType.StringTable,
                    constantConfigStringTable.Name!),
                constantConfigStringTable
            ),
            (
                new ZoneAssetKey(
                    XAssetType.RawFile,
                    levelScript.OriginalName),
                levelScript
            ),
            (
                new ZoneAssetKey(
                    XAssetType.RawFile,
                    diagnosticMarker.OriginalName),
                diagnosticMarker
            )
        };
        if (gameplayModelSupport is not null)
        {
            ownedRows.AddRange(
                gameplayModelSupport.RuntimeDefinitionKeys.Zip(
                    gameplayModelSupport.RuntimeDefinitionBuildData,
                    (key, buildData) => (
                        Key: key,
                        BuildData: buildData)));
        }
        (ZoneAssetKey Key, IXAssetBuildData BuildData)[] sortedOwnedRows =
            ownedRows
            .OrderBy(value => value.Key.Type)
            .ThenBy(
                value => value.Key.LogicalName,
                StringComparer.Ordinal)
            .ToArray();
        _ownedAssetKeys =
            new ReadOnlyCollection<ZoneAssetKey>(
                sortedOwnedRows.Select(value => value.Key).ToArray());
        _ownedBuildData =
            new ReadOnlyCollection<IXAssetBuildData>(
                sortedOwnedRows.Select(value => value.BuildData).ToArray());
        _blockers =
            new ReadOnlyCollection<
                MinimalMultiplayerMapRuntimeSupportBlocker>(
            [
                new(
                    MinimalMultiplayerMapRuntimeSupportBlockerKind
                        .RetailStartupNotAccepted,
                    "The sparse split-screen constant-configstring table is " +
                    "native-shape safe, but gamestate message size, startup, " +
                    "and delayed stability still require retail acceptance.")
            ]);
    }

    public MinimalMultiplayerMapRuntimeSupportAuthority Authority =>
        MinimalMultiplayerMapRuntimeSupportAuthority
            .ManagedStartupContractOnly;

    public string TargetZoneName { get; }

    public MinimalMultiplayerMapTargetStartupProfile StartupProfile
    {
        get;
    }

    public MapPrimaryChecksum PrimaryChecksum { get; }

    public bool DiagnosticMarkerCompiled => true;

    public bool LevelScriptCompiled => true;

    public bool ConstantConfigStringTableCompiled => true;

    public bool GameplayModelSupportCompiled =>
        GameplayModelSupport is not null;

    /// <summary>
    /// True means the artifact contains the support rows required to attempt
    /// the selected target profile. It does not mean retail acceptance has
    /// passed.
    /// </summary>
    public bool TargetLaunchReady => true;

    public bool PersistenceAuthorized => false;

    public bool RequiresSplitScreenDisabled => false;

    public bool RequiresSplitScreenEnabled => true;

    public bool RequiresOnlineGameDisabled => true;

    public bool RequiresLobbyHostState => false;

    public bool RequiresConstantConfigStringTable => true;

    public MinimalMultiplayerMapLevelScriptDisposition
        LevelScriptDisposition =>
            MinimalMultiplayerMapLevelScriptDisposition.OwnedMinimalMain;

    public MinimalMultiplayerMapConstantConfigStringDisposition
        ConstantConfigStringDisposition =>
            GameplayModelSupport is null
                ? MinimalMultiplayerMapConstantConfigStringDisposition
                    .OwnedMinimumMapIdentityTable
                : MinimalMultiplayerMapConstantConfigStringDisposition
                    .OwnedRetargetedRetailFreeForAllTable;

    public IReadOnlyList<ZoneAssetKey> OwnedAssetKeys =>
        _ownedAssetKeys;

    public IReadOnlyList<
        MinimalMultiplayerMapRuntimeSupportBlocker> Blockers =>
            _blockers;

    internal MinimalMultiplayerMapFastFileDiagnosticMarkerBuildData
        DiagnosticMarker { get; }

    internal MinimalMultiplayerMapLevelScriptBuildData LevelScript
    {
        get;
    }

    internal IStringTableBuildData
        ConstantConfigStringTable { get; }

    internal MapGameplayModelSupportCompilation? GameplayModelSupport
    {
        get;
    }

    internal IReadOnlyList<IXAssetBuildData> OwnedBuildData =>
        _ownedBuildData;
}

public static class MinimalMultiplayerMapRuntimeSupportCompiler
{
    public const string CompilerIdentity =
        "iw4-studio.target-acceptance.runtime-support." +
        "offline-splitscreen-dm@2";

    public static MinimalMultiplayerMapRuntimeSupportCompilation Compile(
        string mapAssetName,
        string targetZoneName,
        MinimalMultiplayerMapTargetStartupProfile startupProfile,
        MapPrimaryChecksum primaryChecksum)
    {
        string normalizedMapName =
            MapCompilerContentIdentityInput
                .NormalizeMultiplayerMapAssetName(mapAssetName);
        string normalizedZoneName =
            NormalizeTargetZoneName(targetZoneName);
        string expectedMapName =
            $"maps/mp/{normalizedZoneName}.d3dbsp";
        if (!string.Equals(
                normalizedMapName,
                expectedMapName,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The target zone identity must be the basename of the " +
                "normalized multiplayer map asset.",
                nameof(targetZoneName));
        }

        var marker =
            new MinimalMultiplayerMapFastFileDiagnosticMarkerBuildData(
                normalizedZoneName);
        var levelScript =
            new MinimalMultiplayerMapLevelScriptBuildData(
                normalizedZoneName);
        var constantConfigStringTable =
            new MinimalMultiplayerMapConstantConfigStringTableBuildData(
                normalizedZoneName,
                primaryChecksum);
        var result =
            new MinimalMultiplayerMapRuntimeSupportCompilation(
                normalizedZoneName,
                startupProfile,
                primaryChecksum,
                constantConfigStringTable,
                levelScript,
                marker);
        RequireValid(result);
        return result;
    }

    /// <summary>
    /// Adds the explicitly imported retail startup closure after pure scene
    /// compilation. The generated map graph remains source-independent; only
    /// the bounded target-test artifact gains the retail table and XModels.
    /// </summary>
    public static MinimalMultiplayerMapRuntimeSupportCompilation
        AttachGameplayModelSupport(
            MinimalMultiplayerMapRuntimeSupportCompilation support,
            MapGameplayModelSupportCompilation gameplayModelSupport)
    {
        ArgumentNullException.ThrowIfNull(support);
        ArgumentNullException.ThrowIfNull(gameplayModelSupport);
        RequireValid(support);
        if (support.GameplayModelSupport is not null)
        {
            throw new InvalidOperationException(
                "Gameplay-model support is already attached.");
        }
        if (!string.Equals(
                support.TargetZoneName,
                gameplayModelSupport.SourceZoneName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Gameplay-model support must come from the target zone it " +
                "will replace.",
                nameof(gameplayModelSupport));
        }

        IStringTableBuildData configStrings =
            new RetargetedRetailConstantConfigStringTableBuildData(
                gameplayModelSupport
                    .ConstantConfigStringTable
                    .BuildData,
                support.PrimaryChecksum);
        var result = new MinimalMultiplayerMapRuntimeSupportCompilation(
            support.TargetZoneName,
            support.StartupProfile,
            support.PrimaryChecksum,
            configStrings,
            support.LevelScript,
            support.DiagnosticMarker,
            gameplayModelSupport);
        RequireValid(result);
        return result;
    }

    internal static void RequireValid(
        MinimalMultiplayerMapRuntimeSupportCompilation support)
    {
        ArgumentNullException.ThrowIfNull(support);
        byte[] payload =
            support.DiagnosticMarker.GetSerializedPayloadCopy();
        ZoneAssetKey expectedScript =
            new(
                XAssetType.RawFile,
                $"maps/mp/{support.TargetZoneName}.gsc");
        ZoneAssetKey expectedMarker =
            new(
                XAssetType.RawFile,
                support.TargetZoneName);
        ZoneAssetKey expectedConstantConfigStrings =
            new(
                XAssetType.StringTable,
                "mp/configstrings/configstrings_ps3_" +
                $"{support.TargetZoneName}_dm.csv");
        ZoneAssetKey[] expectedKeys =
            new[]
            {
                expectedConstantConfigStrings,
                expectedScript,
                expectedMarker
            }
            .Concat(
                support.GameplayModelSupport?.OwnedAssetKeys.Where(
                    value => value != expectedConstantConfigStrings) ??
                [])
            .ToArray();
        expectedKeys = expectedKeys
            .OrderBy(value => value.Type)
            .ThenBy(
                value => value.LogicalName,
                StringComparer.Ordinal)
            .ToArray();
        byte[] levelScriptPayload =
            support.LevelScript.GetSerializedPayloadCopy();
        bool exactOwnedRows =
            support.OwnedAssetKeys.SequenceEqual(expectedKeys) &&
            support.OwnedBuildData.Count == expectedKeys.Length &&
            support.OwnedAssetKeys
                .Zip(support.OwnedBuildData)
                .All(pair =>
                    pair.First.Type == pair.Second.AssetType &&
                    pair.Second switch
                    {
                        IRawFileBuildData raw =>
                            string.Equals(
                                pair.First.LogicalName,
                                raw.OriginalName,
                                StringComparison.Ordinal),
                        IStringTableBuildData table =>
                            string.Equals(
                                pair.First.LogicalName,
                                table.Name,
                                StringComparison.Ordinal),
                        IMaterialBuildData material =>
                            material.Name is not null &&
                            pair.First ==
                                ZoneAssetKey.FromWireName(
                                    XAssetType.Material,
                                    material.Name),
                        IXModelBuildData model =>
                            model.Name is not null &&
                            pair.First ==
                                new ZoneAssetKey(
                                    XAssetType.XModel,
                                    model.Name),
                        _ => false
                    });
        MapGameplayModelSupportCompilation? gameplay =
            support.GameplayModelSupport;
        bool usesMinimumConfigStringTable =
            gameplay is null;
        int expectedStateOwnerMaterialCount =
            gameplay is { IncludesNestedDependencyDefinitions: true }
                ? MapGameplayModelSupportImporter
                    .RequiredStateOwnerMaterialCount
                : 0;
        bool exactGameplaySupport =
            gameplay is null ||
            string.Equals(
                gameplay.SourceZoneName,
                support.TargetZoneName,
                StringComparison.OrdinalIgnoreCase) &&
            gameplay.Authority ==
                MapGameplayModelSupportAuthority
                    .OfficialTargetZoneDefinitionImport &&
            gameplay.PreserveImportedScriptStringOrderRequired &&
            gameplay.OwnedAssets.Count ==
                MapGameplayModelSupportImporter.RequiredModelCount &&
            gameplay.StateOwnerMaterials.Count ==
                expectedStateOwnerMaterialCount &&
            gameplay.OwnedAssetKeys.Count ==
                MapGameplayModelSupportImporter.RequiredModelCount +
                expectedStateOwnerMaterialCount +
                1 &&
            gameplay.OwnedBuildData.Count ==
                gameplay.OwnedAssetKeys.Count &&
            gameplay.ConstantConfigStringTable.Key ==
                expectedConstantConfigStrings &&
            support.ConstantConfigStringTable is
                RetargetedRetailConstantConfigStringTableBuildData
                    retargetedTable &&
            ReferenceEquals(
                retargetedTable.Source,
                gameplay.ConstantConfigStringTable.BuildData) &&
            retargetedTable.HasExactMapIdentityRetarget(
                support.PrimaryChecksum) &&
            gameplay.ScriptStrings.Count != 0 &&
            gameplay.OwnedAssets.All(value =>
                value.BuildData.BoneNames.All(index =>
                    index < gameplay.ScriptStrings.Count));
        bool exactConstantConfigStringTable =
            usesMinimumConfigStringTable
                ? support.ConstantConfigStringTable is
                        MinimalMultiplayerMapConstantConfigStringTableBuildData
                            minimumTable &&
                    minimumTable.HasCanonicalMapIdentityRows()
                : support.ConstantConfigStringTable is
                        RetargetedRetailConstantConfigStringTableBuildData
                            retargeted &&
                    retargeted.HasExactMapIdentityRetarget(
                        support.PrimaryChecksum);
        if (!exactOwnedRows ||
            !exactGameplaySupport ||
            !exactConstantConfigStringTable ||
            support.StartupProfile !=
                MinimalMultiplayerMapTargetStartupProfile
                    .OfflineSplitScreenFreeForAll ||
            support.ConstantConfigStringDisposition !=
                (usesMinimumConfigStringTable
                    ? MinimalMultiplayerMapConstantConfigStringDisposition
                        .OwnedMinimumMapIdentityTable
                    : MinimalMultiplayerMapConstantConfigStringDisposition
                        .OwnedRetargetedRetailFreeForAllTable) ||
            support.RequiresSplitScreenDisabled ||
            !support.RequiresSplitScreenEnabled ||
            !support.RequiresOnlineGameDisabled ||
            support.RequiresLobbyHostState ||
            !support.RequiresConstantConfigStringTable ||
            !support.TargetLaunchReady ||
            !support.ConstantConfigStringTableCompiled ||
            support.ConstantConfigStringTable.AssetType !=
                XAssetType.StringTable ||
            !string.Equals(
                support.ConstantConfigStringTable.Name,
                expectedConstantConfigStrings.LogicalName,
                StringComparison.Ordinal) ||
            support.ConstantConfigStringTable.ColumnCount != 2 ||
            support.ConstantConfigStringTable.Cells.Count !=
                checked(
                    support.ConstantConfigStringTable.RowCount *
                    support.ConstantConfigStringTable.ColumnCount) ||
            usesMinimumConfigStringTable &&
                (support.ConstantConfigStringTable.RowCount != 2 ||
                 support.ConstantConfigStringTable.Cells.Count != 4) ||
            !support.LevelScriptCompiled ||
            support.LevelScriptDisposition !=
                MinimalMultiplayerMapLevelScriptDisposition
                    .OwnedMinimalMain ||
            support.LevelScript.AssetType != XAssetType.RawFile ||
            !string.Equals(
                support.LevelScript.OriginalName,
                expectedScript.LogicalName,
                StringComparison.Ordinal) ||
            !support.LevelScript.HasBuffer ||
            support.LevelScript.CompressedLength != 0 ||
            support.LevelScript.UncompressedLength !=
                levelScriptPayload.Length - 1 ||
            !support.LevelScript.HasCanonicalPayload ||
            support.DiagnosticMarker.AssetType != XAssetType.RawFile ||
            !string.Equals(
                support.DiagnosticMarker.OriginalName,
                support.TargetZoneName,
                StringComparison.Ordinal) ||
            !support.DiagnosticMarker.HasBuffer ||
            support.DiagnosticMarker.CompressedLength != 0 ||
            support.DiagnosticMarker.UncompressedLength != 0 ||
            !payload.SequenceEqual([(byte)0]))
        {
            throw new InvalidDataException(
                "The multiplayer runtime support graph does not contain " +
                "the exact sparse split-screen DM map-identity table, " +
                "minimal map main, and one-byte-NUL diagnostic RawFile " +
                "marker.");
        }
    }

    private static string NormalizeTargetZoneName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized =
            value.Trim().Replace('\\', '/').ToLowerInvariant();
        if (normalized.Contains('/') ||
            normalized.EndsWith(
                ".ff",
                StringComparison.Ordinal) ||
            !normalized.StartsWith(
                "mp_",
                StringComparison.Ordinal) ||
            normalized.Any(character =>
                character != '_' &&
                (character < 'a' || character > 'z') &&
                (character < '0' || character > '9')))
        {
            throw new ArgumentException(
                "A target zone name must be a bare normalized ASCII mp_ " +
                "identity containing only lowercase letters, digits, and " +
                "underscores.",
                nameof(value));
        }
        return normalized;
    }
}

/// <summary>
/// Exact retail free-for-all constant-configstring table with only the mapcrc
/// value cell retargeted to the generated map checksum. Every other cell keeps
/// its imported value and hash.
/// </summary>
internal sealed class
    RetargetedRetailConstantConfigStringTableBuildData :
        IStringTableBuildData
{
    private const string MapCrcLabelKey = "111";
    private const string MapCrcLabelValue = "mapcrc";
    private const string MapCrcValueKey = "311";

    private readonly IReadOnlyList<IStringTableCellBuildData> _cells;
    private readonly int _mapCrcLabelRow;
    private readonly int _mapCrcValueRow;

    internal RetargetedRetailConstantConfigStringTableBuildData(
        IStringTableBuildData source,
        MapPrimaryChecksum primaryChecksum)
    {
        Source = source ??
            throw new ArgumentNullException(nameof(source));
        if (source.Name is null ||
            source.RowCount <= 0 ||
            source.ColumnCount != 2 ||
            source.Cells.Count !=
                checked(source.RowCount * source.ColumnCount))
        {
            throw new InvalidDataException(
                "Retail constant-configstring retargeting requires a named, " +
                "nonempty two-column source table with an exact cell count.");
        }

        _mapCrcLabelRow = RequireUniqueRow(source, MapCrcLabelKey);
        _mapCrcValueRow = RequireUniqueRow(source, MapCrcValueKey);
        RequireSourceCell(
            source,
            _mapCrcLabelRow,
            column: 0,
            MapCrcLabelKey);
        RequireSourceCell(
            source,
            _mapCrcLabelRow,
            column: 1,
            MapCrcLabelValue);
        RequireSourceCell(
            source,
            _mapCrcValueRow,
            column: 0,
            MapCrcValueKey);

        Name = source.Name;
        RowCount = source.RowCount;
        ColumnCount = source.ColumnCount;
        MapCrcValue =
            unchecked((int)primaryChecksum.Value)
                .ToString(CultureInfo.InvariantCulture);
        IStringTableCellBuildData[] cells = source.Cells
            .Select(value =>
                (IStringTableCellBuildData)
                    new RetargetedRetailConstantConfigStringCellBuildData(
                        value.Value,
                        value.Hash))
            .ToArray();
        int valueIndex =
            checked(_mapCrcValueRow * ColumnCount + 1);
        cells[valueIndex] =
            new RetargetedRetailConstantConfigStringCellBuildData(
                MapCrcValue,
                ComputeNativeCellHash(MapCrcValue));
        _cells =
            new ReadOnlyCollection<IStringTableCellBuildData>(cells);

        if (!HasExactMapIdentityRetarget(primaryChecksum))
        {
            throw new InvalidDataException(
                "The retail constant-configstring table could not be " +
                "retargeted without changing unrelated cells.");
        }
    }

    public XAssetType AssetType => XAssetType.StringTable;

    public string Name { get; }

    public int RowCount { get; }

    public int ColumnCount { get; }

    public IReadOnlyList<IStringTableCellBuildData> Cells => _cells;

    internal IStringTableBuildData Source { get; }

    internal string MapCrcValue { get; }

    internal bool HasExactMapIdentityRetarget(
        MapPrimaryChecksum primaryChecksum)
    {
        string expectedValue =
            unchecked((int)primaryChecksum.Value)
                .ToString(CultureInfo.InvariantCulture);
        int valueIndex =
            checked(_mapCrcValueRow * ColumnCount + 1);
        if (!string.Equals(Name, Source.Name, StringComparison.Ordinal) ||
            RowCount != Source.RowCount ||
            ColumnCount != Source.ColumnCount ||
            Cells.Count != Source.Cells.Count ||
            !HasCell(
                _mapCrcLabelRow,
                column: 0,
                MapCrcLabelKey) ||
            !HasCell(
                _mapCrcLabelRow,
                column: 1,
                MapCrcLabelValue) ||
            !HasCell(
                _mapCrcValueRow,
                column: 0,
                MapCrcValueKey) ||
            !string.Equals(
                Cells[valueIndex].Value,
                expectedValue,
                StringComparison.Ordinal) ||
            Cells[valueIndex].Hash !=
                ComputeNativeCellHash(expectedValue))
        {
            return false;
        }

        for (int index = 0; index < Cells.Count; index++)
        {
            if (index == valueIndex)
            {
                continue;
            }

            if (!string.Equals(
                    Cells[index].Value,
                    Source.Cells[index].Value,
                    StringComparison.Ordinal) ||
                Cells[index].Hash != Source.Cells[index].Hash)
            {
                return false;
            }
        }
        return true;
    }

    private bool HasCell(int row, int column, string value)
    {
        IStringTableCellBuildData cell =
            Cells[checked(row * ColumnCount + column)];
        return string.Equals(
                   cell.Value,
                   value,
                   StringComparison.Ordinal) &&
               cell.Hash == ComputeNativeCellHash(value);
    }

    private static int RequireUniqueRow(
        IStringTableBuildData source,
        string key)
    {
        int[] rows = Enumerable.Range(0, source.RowCount)
            .Where(row =>
                string.Equals(
                    source.Cells[
                        checked(row * source.ColumnCount)].Value,
                    key,
                    StringComparison.Ordinal))
            .ToArray();
        if (rows.Length != 1)
        {
            throw new InvalidDataException(
                $"Retail constant-configstring table requires exactly one " +
                $"'{key}' key row; observed {rows.Length}.");
        }
        return rows[0];
    }

    private static void RequireSourceCell(
        IStringTableBuildData source,
        int row,
        int column,
        string value)
    {
        IStringTableCellBuildData cell =
            source.Cells[
                checked(row * source.ColumnCount + column)];
        if (!string.Equals(
                cell.Value,
                value,
                StringComparison.Ordinal) ||
            cell.Hash != ComputeNativeCellHash(value))
        {
            throw new InvalidDataException(
                $"Retail constant-configstring cell ({row}, {column}) must " +
                $"retain exact native value/hash '{value}'.");
        }
    }

    private static int ComputeNativeCellHash(string value)
    {
        int hash = 0;
        foreach (char character in value)
        {
            if (character > 0x7F)
            {
                throw new InvalidDataException(
                    "Constant-configstring hashes require ASCII values.");
            }

            char normalized =
                character is >= 'A' and <= 'Z'
                    ? (char)(character + ('a' - 'A'))
                    : character;
            hash = unchecked(hash * 31 + normalized);
        }
        return hash;
    }
}

internal sealed record
    RetargetedRetailConstantConfigStringCellBuildData(
        string? Value,
        int Hash) :
        IStringTableCellBuildData;

/// <summary>
/// Native-safe split-screen DM table with the required two-column schema and
/// only the map identity rows. Omitted rows disable most constant-string
/// compression; target acceptance must still prove the resulting gamestate
/// fits the retail message budget.
/// </summary>
internal sealed class
    MinimalMultiplayerMapConstantConfigStringTableBuildData :
        IStringTableBuildData
{
    private readonly IReadOnlyList<IStringTableCellBuildData> _cells;

    internal MinimalMultiplayerMapConstantConfigStringTableBuildData(
        string targetZoneName,
        MapPrimaryChecksum primaryChecksum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZoneName);
        Name =
            "mp/configstrings/configstrings_ps3_" +
            $"{targetZoneName}_dm.csv";
        MapCrcValue =
            unchecked((int)primaryChecksum.Value)
                .ToString(CultureInfo.InvariantCulture);
        _cells = Array.AsReadOnly<IStringTableCellBuildData>(
        [
            CreateCell("111"),
            CreateCell("mapcrc"),
            CreateCell("311"),
            CreateCell(MapCrcValue)
        ]);
    }

    public XAssetType AssetType => XAssetType.StringTable;

    public string Name { get; }

    public int RowCount => 2;

    public int ColumnCount => 2;

    public IReadOnlyList<IStringTableCellBuildData> Cells =>
        _cells;

    internal string MapCrcValue { get; }

    internal bool HasCanonicalMapIdentityRows() =>
        Cells.Count == 4 &&
        HasCell(0, "111", 0x0000BE11) &&
        HasCell(1, "mapcrc", unchecked((int)0xBF8B8EF8)) &&
        HasCell(2, "311", 0x0000C593) &&
        HasCell(3, MapCrcValue, ComputeNativeCellHash(MapCrcValue));

    private bool HasCell(int index, string value, int hash) =>
        string.Equals(
            Cells[index].Value,
            value,
            StringComparison.Ordinal) &&
        Cells[index].Hash == hash;

    private static IStringTableCellBuildData CreateCell(string value) =>
        new MinimalMultiplayerMapConstantConfigStringCellBuildData(
            value,
            ComputeNativeCellHash(value));

    private static int ComputeNativeCellHash(string value)
    {
        int hash = 0;
        foreach (char character in value)
        {
            if (character > 0x7F)
            {
                throw new InvalidDataException(
                    "The minimum constant-configstring table requires " +
                    "ASCII cell values.");
            }

            char normalized =
                character is >= 'A' and <= 'Z'
                    ? (char)(character + ('a' - 'A'))
                    : character;
            hash = unchecked(hash * 31 + normalized);
        }
        return hash;
    }
}

internal sealed record
    MinimalMultiplayerMapConstantConfigStringCellBuildData(
        string Value,
        int Hash) :
        IStringTableCellBuildData;

/// <summary>
/// Compiler-owned map entry point for the bounded target fixture. It invokes
/// only the resident generic multiplayer map initializer and assigns the two
/// team identities expected by common multiplayer scripts.
/// </summary>
internal sealed class MinimalMultiplayerMapLevelScriptBuildData :
    IRawFileBuildData
{
    private const string Source =
        "#include maps\\mp\\_utility;\n" +
        "#include maps\\mp\\gametypes\\_hud_util;\n" +
        "\n" +
        "main()\n" +
        "{\n" +
        "\tprintln(\"M7 map main enter\");\n" +
        "\tmaps\\mp\\_load::main();\n" +
        "\tprintln(\"M7 map load returned\");\n" +
        "\tlevel.m7OriginalPlayerDisconnect = level.callbackPlayerDisconnect;\n" +
        "\tlevel.callbackPlayerDisconnect = ::m7_player_disconnect_probe;\n" +
        "\tprintln(\"M7 PD HOOKED\");\n" +
        "\tlevel.m7OriginalCodeEndGame = level.callbackCodeEndGame;\n" +
        "\tlevel.callbackCodeEndGame = ::m7_code_end_game_probe;\n" +
        "\tprintln(\"M7 CODEEND HOOKED\");\n" +
        "\tthread m7_game_ended_probe();\n" +
        "\tthread m7_restarting_probe();\n" +
        "\tgame[\"attackers\"] = \"allies\";\n" +
        "\tgame[\"defenders\"] = \"axis\";\n" +
        "\tlevel.m7OnStartGameType = level.onStartGameType;\n" +
        "\tlevel.onStartGameType = ::m7_on_start_game_type_probe;\n" +
        "\tthread m7_frame_probe();\n" +
        "}\n" +
        "\n" +
        "m7_on_start_game_type_probe()\n" +
        "{\n" +
        "\tprintln(\"M7 dm onStart enter\");\n" +
        "\tsetClientNameMode(\"auto_change\");\n" +
        "\tprintln(\"M7 dm clientname returned\");\n" +
        "\n" +
        "\tsetObjectiveText(\"allies\", &\"OBJECTIVES_DM\");\n" +
        "\tsetObjectiveText(\"axis\", &\"OBJECTIVES_DM\");\n" +
        "\tif (level.splitscreen)\n" +
        "\t{\n" +
        "\t\tsetObjectiveScoreText(\"allies\", &\"OBJECTIVES_DM\");\n" +
        "\t\tsetObjectiveScoreText(\"axis\", &\"OBJECTIVES_DM\");\n" +
        "\t}\n" +
        "\telse\n" +
        "\t{\n" +
        "\t\tsetObjectiveScoreText(\"allies\", &\"OBJECTIVES_DM_SCORE\");\n" +
        "\t\tsetObjectiveScoreText(\"axis\", &\"OBJECTIVES_DM_SCORE\");\n" +
        "\t}\n" +
        "\tsetObjectiveHintText(\"allies\", &\"OBJECTIVES_DM_HINT\");\n" +
        "\tsetObjectiveHintText(\"axis\", &\"OBJECTIVES_DM_HINT\");\n" +
        "\tprintln(\"M7 dm objectives returned\");\n" +
        "\n" +
        "\tlevel.spawnMins = (0, 0, 0);\n" +
        "\tlevel.spawnMaxs = (0, 0, 0);\n" +
        "\tprintln(\"M7 dm allies begin\");\n" +
        "\tm7_add_spawn_points_probe(\"allies\", \"mp_dm_spawn\");\n" +
        "\tprintln(\"M7 dm allies returned\");\n" +
        "\tmaps\\mp\\gametypes\\_spawnlogic::addSpawnPoints(\"axis\", \"mp_dm_spawn\");\n" +
        "\tprintln(\"M7 dm axis returned\");\n" +
        "\tlevel.mapCenter = maps\\mp\\gametypes\\_spawnlogic::findBoxCenter(level.spawnMins, level.spawnMaxs);\n" +
        "\tsetMapCenter(level.mapCenter);\n" +
        "\tprintln(\"M7 dm center returned\");\n" +
        "\n" +
        "\tallowed[0] = \"dm\";\n" +
        "\tmaps\\mp\\gametypes\\_gameobjects::main(allowed);\n" +
        "\tprintln(\"M7 dm gameobjects returned\");\n" +
        "\n" +
        "\tmaps\\mp\\gametypes\\_rank::registerScoreInfo(\"kill\", 50);\n" +
        "\tmaps\\mp\\gametypes\\_rank::registerScoreInfo(\"headshot\", 50);\n" +
        "\tmaps\\mp\\gametypes\\_rank::registerScoreInfo(\"assist\", 10);\n" +
        "\tmaps\\mp\\gametypes\\_rank::registerScoreInfo(\"suicide\", 0);\n" +
        "\tmaps\\mp\\gametypes\\_rank::registerScoreInfo(\"teamkill\", 0);\n" +
        "\tlevel.QuickMessageToAll = true;\n" +
        "\tprintln(\"M7 dm onStart returned\");\n" +
        "}\n" +
        "\n" +
        "m7_add_spawn_points_probe(team, spawnPointName)\n" +
        "{\n" +
        "\tprintln(\"M7 add enter\");\n" +
        "\toldSpawnPoints = [];\n" +
        "\tprintln(\"M7 add old array ready\");\n" +
        "\tif (level.teamSpawnPoints[team].size)\n" +
        "\t\toldSpawnPoints = level.teamSpawnPoints[team];\n" +
        "\tprintln(\"M7 add team slot read\");\n" +
        "\n" +
        "\tlevel.teamSpawnPoints[team] = maps\\mp\\gametypes\\_spawnlogic::getSpawnpointArray(spawnPointName);\n" +
        "\tprintln(\"M7 add lookup returned\");\n" +
        "\tif (!level.teamSpawnPoints[team].size)\n" +
        "\t{\n" +
        "\t\tprintln(\"M7 add no spawnpoints\");\n" +
        "\t\tmaps\\mp\\gametypes\\_callbacksetup::AbortLevel();\n" +
        "\t\twait 1;\n" +
        "\t\treturn;\n" +
        "\t}\n" +
        "\tprintln(\"M7 add spawnpoints found\");\n" +
        "\n" +
        "\tif (!isDefined(level.spawnpoints))\n" +
        "\t\tlevel.spawnpoints = [];\n" +
        "\tprintln(\"M7 add level array ready\");\n" +
        "\n" +
        "\tfor (index = 0; index < level.teamSpawnPoints[team].size; index++)\n" +
        "\t{\n" +
        "\t\tspawnpoint = level.teamSpawnPoints[team][index];\n" +
        "\t\tprintln(\"M7 add spawnpoint selected\");\n" +
        "\t\tif (!isDefined(spawnpoint.inited))\n" +
        "\t\t{\n" +
        "\t\t\tprintln(\"M7 spawnPointInit begin\");\n" +
        "\t\t\tspawnpoint m7_spawn_point_init_probe();\n" +
        "\t\t\tprintln(\"M7 spawnPointInit returned\");\n" +
        "\t\t\tlevel.spawnpoints[level.spawnpoints.size] = spawnpoint;\n" +
        "\t\t}\n" +
        "\t}\n" +
        "\n" +
        "\tfor (index = 0; index < oldSpawnPoints.size; index++)\n" +
        "\t{\n" +
        "\t\torigin = oldSpawnPoints[index].origin;\n" +
        "\t\tlevel.spawnMins = maps\\mp\\gametypes\\_spawnlogic::expandMins(level.spawnMins, origin);\n" +
        "\t\tlevel.spawnMaxs = maps\\mp\\gametypes\\_spawnlogic::expandMaxs(level.spawnMaxs, origin);\n" +
        "\t\tlevel.teamSpawnPoints[team][level.teamSpawnPoints[team].size] = oldSpawnPoints[index];\n" +
        "\t}\n" +
        "\tprintln(\"M7 add returned\");\n" +
        "}\n" +
        "\n" +
        "m7_spawn_point_init_probe()\n" +
        "{\n" +
        "\tspawnpoint = self;\n" +
        "\torigin = spawnpoint.origin;\n" +
        "\tprintln(\"M7 spi bounds begin\");\n" +
        "\tlevel.spawnMins = maps\\mp\\gametypes\\_spawnlogic::expandMins(level.spawnMins, origin);\n" +
        "\tlevel.spawnMaxs = maps\\mp\\gametypes\\_spawnlogic::expandMaxs(level.spawnMaxs, origin);\n" +
        "\tprintln(\"M7 spi bounds returned\");\n" +
        "\n" +
        "\tprintln(\"M7 spi place begin\");\n" +
        "\tspawnpoint placeSpawnpoint();\n" +
        "\tprintln(\"M7 spi place returned\");\n" +
        "\tspawnpoint.forward = anglesToForward(spawnpoint.angles);\n" +
        "\tspawnpoint.sightTracePoint = spawnpoint.origin + (0, 0, 50);\n" +
        "\tspawnpoint.lastspawnedplayer = spawnpoint;\n" +
        "\tspawnpoint.lastspawntime = getTime();\n" +
        "\n" +
        "\tskyHeight = 1024;\n" +
        "\tspawnpoint.outside = true;\n" +
        "\tprintln(\"M7 spi trace1 bypassed\");\n" +
        "\ttrace1Passed = true;\n" +
        "\tif (!trace1Passed)\n" +
        "\t{\n" +
        "\t\tstartpoint = spawnpoint.sightTracePoint + spawnpoint.forward * 100;\n" +
        "\t\tprintln(\"M7 spi trace2 begin\");\n" +
        "\t\ttrace2Passed = bulletTracePassed(startpoint, startpoint + (0, 0, skyHeight), false, undefined);\n" +
        "\t\tprintln(\"M7 spi trace2 returned\");\n" +
        "\t\tif (!trace2Passed)\n" +
        "\t\t\tspawnpoint.outside = false;\n" +
        "\t}\n" +
        "\n" +
        "\tright = anglesToRight(spawnpoint.angles);\n" +
        "\tspawnpoint.alternates = [];\n" +
        "\tprintln(\"M7 spi alternate1 begin\");\n" +
        "\tmaps\\mp\\gametypes\\_spawnlogic::AddAlternateSpawnpoint(spawnpoint, spawnpoint.origin + right * 45);\n" +
        "\tprintln(\"M7 spi alternate1 returned\");\n" +
        "\tmaps\\mp\\gametypes\\_spawnlogic::AddAlternateSpawnpoint(spawnpoint, spawnpoint.origin - right * 45);\n" +
        "\tprintln(\"M7 spi alternate2 returned\");\n" +
        "\n" +
        "\tprintln(\"M7 spi update begin\");\n" +
        "\tmaps\\mp\\gametypes\\_spawnlogic::spawnPointUpdate(spawnpoint);\n" +
        "\tprintln(\"M7 spi update returned\");\n" +
        "\tspawnpoint.inited = true;\n" +
        "\tprintln(\"M7 spi returned\");\n" +
        "}\n" +
        "\n" +
        "m7_player_disconnect_probe()\n" +
        "{\n" +
        "\tprintln(\"M7 PD ENTER\");\n" +
        "\t[[level.m7OriginalPlayerDisconnect]]();\n" +
        "\tprintln(\"M7 PD RETURN\");\n" +
        "}\n" +
        "\n" +
        "m7_code_end_game_probe()\n" +
        "{\n" +
        "\tprintln(\"M7 CODEEND ENTER\");\n" +
        "\t[[level.m7OriginalCodeEndGame]]();\n" +
        "\tprintln(\"M7 CODEEND RETURN\");\n" +
        "}\n" +
        "\n" +
        "m7_game_ended_probe()\n" +
        "{\n" +
        "\tlevel waittill(\"game_ended\");\n" +
        "\tprintln(\"M7 GAME_ENDED\");\n" +
        "}\n" +
        "\n" +
        "m7_restarting_probe()\n" +
        "{\n" +
        "\tlevel waittill(\"restarting\");\n" +
        "\tprintln(\"M7 RESTARTING\");\n" +
        "}\n" +
        "\n" +
        "m7_frame_probe()\n" +
        "{\n" +
        "\twait 0.05;\n" +
        "\tprintln(\"M7 server frame 1\");\n" +
        "\twait 0.2;\n" +
        "\tprintln(\"M7 server frame 3\");\n" +
        "}\n";

    private static readonly byte[] CanonicalPayload =
        Encoding.Latin1.GetBytes(Source + "\0");

    internal MinimalMultiplayerMapLevelScriptBuildData(
        string targetZoneName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZoneName);
        OriginalName = $"maps/mp/{targetZoneName}.gsc";
    }

    public XAssetType AssetType => XAssetType.RawFile;

    public string OriginalName { get; }

    public bool HasBuffer => true;

    public int CompressedLength => 0;

    public int UncompressedLength => CanonicalPayload.Length - 1;

    internal bool HasCanonicalPayload =>
        GetSerializedPayloadCopy().SequenceEqual(CanonicalPayload);

    public byte[] GetSerializedPayloadCopy() =>
        CanonicalPayload.ToArray();
}

/// <summary>
/// Empty target diagnostic row consumed by the multiplayer startup error
/// reporter. IW4 represents logical length zero with an allocated terminal
/// NUL byte rather than a null RawFile buffer.
/// </summary>
internal sealed class
    MinimalMultiplayerMapFastFileDiagnosticMarkerBuildData :
        IRawFileBuildData
{
    private static readonly byte[] Payload = [0];

    internal MinimalMultiplayerMapFastFileDiagnosticMarkerBuildData(
        string targetZoneName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZoneName);
        OriginalName = targetZoneName;
    }

    public XAssetType AssetType => XAssetType.RawFile;

    public string OriginalName { get; }

    public bool HasBuffer => true;

    public int CompressedLength => 0;

    public int UncompressedLength => 0;

    public byte[] GetSerializedPayloadCopy() => Payload.ToArray();
}
