using IW4.Formats.RawFile;
using IW4.Game.Assets.RawFile;
using IW4.Game.Assets.StringTable;
using IW4.Game.Zone;
using IW4.Runtime.Assets;

namespace IW4.Studio.Documents.MenuEditing;

public sealed partial class MenuEditingCoordinator
{
    private const int PreviewStringTableCacheCapacity = 64;
    private readonly object _previewStringTableGate = new();
    private readonly Dictionary<string, StringTableAsset?> _previewStringTables = new(StringComparer.Ordinal);
    private (long Editing, long? Pool) _previewStringTableRevision;

    public StringTableAsset? ResolvePreviewStringTable(string name)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string normalizedName = XAssetStableIdentity.NormalizeLookupName(name);
        lock (_previewStringTableGate)
        {
            XAssetPool? pool = _session.Workspace.LoadedZones.FirstOrDefault()?.LoadResult.Context.AssetPool;
            var revision = (_session.Revision, pool?.Revision);
            if (_previewStringTableRevision != revision)
            {
                _previewStringTables.Clear();
                _previewStringTableRevision = revision;
            }

            if (_previewStringTables.TryGetValue(normalizedName, out StringTableAsset? cached))
                return cached;

            StringTableAsset? table = ReadPreviewStringTable(normalizedName, pool);
            if (_previewStringTables.Count == PreviewStringTableCacheCapacity)
                _previewStringTables.Clear();
            _previewStringTables.Add(normalizedName, table);
            return table;
        }
    }

    private StringTableAsset? ReadPreviewStringTable(string normalizedName, XAssetPool? pool)
    {
        // Keep the first full target definition in serialized provider order,
        // then fall back to the active provider in the loaded database.
        var target = _session.Document.Rows.FirstOrDefault(entry =>
            entry.AssetType == XAssetType.StringTable &&
            entry.Access == WorkspaceAssetAccess.Editable &&
            entry.Definition is StringTableAsset &&
            string.Equals(entry.NormalizedName, normalizedName, StringComparison.Ordinal));
        if (target?.TargetRowIdentity is { } row)
            return _session.CaptureCurrentDefinition(row) as StringTableAsset;

        return pool is not null &&
            pool.TryResolve(XAssetType.StringTable, normalizedName, out StringTableAsset? table) &&
            table is not null &&
            pool.TryGetEntry(table, out XAssetPoolEntry entry) && !entry.IsReferencePlaceholder
            ? table
            : null;
    }

    // The PS3 multiplayer executable requests "exec default_mp.cfg". The
    // preview imports literal dvar assignments only; it never executes cfg code.
    public IReadOnlyDictionary<string, string>? ReadPreviewConfigDefaults()
    {
        ThrowIfDisposed();
        string? root = ReadConfig(MenuPreviewConfig.MultiplayerDefaultName);
        return root is null ? null : MenuPreviewConfig.ReadAssignments(root, ReadConfig);
    }

    private string? ReadConfig(string name)
    {
        string normalizedName = XAssetStableIdentity.NormalizeLookupName(name);
        var target = _session.Document.Rows.FirstOrDefault(entry =>
            entry.AssetType == XAssetType.RawFile &&
            entry.Access == WorkspaceAssetAccess.Editable &&
            entry.Definition is RawFileAsset &&
            string.Equals(entry.NormalizedName, normalizedName, StringComparison.Ordinal));
        RawFileAsset? source = null;
        if (target?.TargetRowIdentity is { } row)
            source = _session.CaptureCurrentDefinition(row) as RawFileAsset;
        else if (_session.Workspace.LoadedZones.FirstOrDefault() is { } zone)
        {
            XAssetPool pool = zone.LoadResult.Context.AssetPool;
            if (pool.TryResolve(XAssetType.RawFile, normalizedName, out RawFileAsset? candidate) &&
                candidate is not null && pool.TryGetEntry(candidate, out XAssetPoolEntry entry) &&
                !entry.IsReferencePlaceholder)
            {
                source = candidate;
            }
        }
        if (source?.Buffer is null)
            return null;

        var bytes = new RawFileDraft(source).GetLogicalContentCopy();
        var classification = RawFileContentClassifier.Classify(name, bytes);
        if (classification.TextEncoding is not { } encoding)
            throw new InvalidDataException($"RawFile '{name}' has no text encoding.");

        return RawFileContentClassifier.DecodeText(bytes, encoding);
    }
}
