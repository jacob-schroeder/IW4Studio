using IW4.Game.Assets;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

public sealed partial class FastFileEditingSession
{
    /// <summary>Captures the last applied definition of an editable owned row.</summary>
    public BaseAsset CaptureAssetSourceDefinition(TargetZoneRowIdentity identity)
    {
        lock (_gate)
        {
            ThrowIfDisposedCore();
            DraftState state = RequireAssetSourceRow(identity);
            return state.CreateCurrentDefinition();
        }
    }

    /// <summary>Menu sources may define target overrides for menus supplied by dependency fastfiles.</summary>
    public string? ValidateNewAssetSourceName(XAssetType type, string? name) =>
        ValidateNewAssetName(type, name, allowDependencyOverride: type is XAssetType.Menu or XAssetType.MenuFile);

    /// <summary>Finds the one editable owned row that may be explicitly replaced.</summary>
    public WorkspaceAssetCatalogEntry? FindAssetSourceReplacement(
        XAssetType type,
        string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !AssetSourceTransfer.CanImport(type))
            return null;

        lock (_gate)
        {
            ThrowIfDisposedCore();
            return FindAssetSourceReplacementCore(type, name);
        }
    }

    /// <summary>Adds a detached source definition or applies it to an explicit owned row.</summary>
    public (WorkspaceAssetCatalogEntry Entry, bool Changed) ImportAssetSource(
        BaseAsset definition,
        TargetZoneRowIdentity? replaceRow = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!AssetSourceTransfer.Supports(definition.SerializedAssetType))
            throw new NotSupportedException(
                $"Asset source transfer is unavailable for {definition.SerializedAssetType}.");
        IAssetAuthoringAdapter adapter = RequireHostedAdapter(definition);
        object draft = adapter.CreateDraft(definition);
        AssetValidationIssue? error = adapter.Validate(draft).FirstOrDefault(issue =>
            issue.Severity == AssetValidationSeverity.Error);
        if (error is not null)
            throw new InvalidDataException($"{error.FieldPath}: {error.Message}");

        if (replaceRow is null)
            return (AddAsset(definition, adapter), true);

        WorkspaceAssetCatalogEntry row;
        bool changed;
        lock (_gate)
        {
            ThrowIfDisposedCore();
            DraftState state = RequireAssetSourceRow(replaceRow.Value);
            row = state.Entry;
            if (row.AssetType != definition.SerializedAssetType ||
                FindAssetSourceReplacementCore(
                    definition.SerializedAssetType,
                    definition.SerializedAssetName ?? string.Empty)?.TargetRowIdentity != replaceRow)
            {
                throw new InvalidOperationException(
                    "The imported source does not uniquely identify the selected editable owned row.");
            }

            changed = !state.SemanticallyEqualsCurrent(draft);
            if (changed)
                PublishDefinitionCore(state, draft);
        }

        if (changed)
            AppliedAssetsChanged?.Invoke(this, EventArgs.Empty);
        return (row, changed);
    }

    private DraftState RequireAssetSourceRow(TargetZoneRowIdentity identity)
    {
        if (!_drafts.TryGetValue(identity, out DraftState? state) || !IsAssetSourceRow(state.Entry))
        {
            throw new InvalidOperationException(
                "Asset source requires an editable owned target row.");
        }

        return state;
    }

    private WorkspaceAssetCatalogEntry? FindAssetSourceReplacementCore(
        XAssetType type,
        string name)
    {
        if (name.Length == 0 || name[0] == ',' ||
            !string.Equals(name, name.Trim(), StringComparison.Ordinal))
            return null;

        AssetKey key;
        try
        {
            key = AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(type), name);
        }
        catch (ArgumentException)
        {
            return null;
        }

        WorkspaceAssetCatalogEntry[] rows = Document.Rows.Where(entry =>
            CanonicalAssetFamily.FromSerializedType(entry.AssetType) == key.Family &&
            entry.NormalizedName == key.NormalizedName).ToArray();
        if (rows.Length != 1 || rows[0].AssetType != type || !IsAssetSourceRow(rows[0]) ||
            rows[0].TargetRowIdentity is not { } identity ||
            !_drafts.ContainsKey(identity))
            return null;

        return rows[0];
    }

    private static bool IsAssetSourceRow(WorkspaceAssetCatalogEntry entry) =>
        entry.Origin == WorkspaceAssetOrigin.TargetOwnedDefinition &&
        entry.Access == WorkspaceAssetAccess.Editable &&
        !entry.IsGeneratedConfigStringBaseline &&
        AssetSourceTransfer.CanImport(entry.AssetType);
}
