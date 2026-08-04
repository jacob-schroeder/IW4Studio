using IW4.Assets.Assets;
using IW4.Assets.Assets.Font;
using IW4.Assets.Assets.Localize;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Rendering;

/// <summary>
/// Resolves Menu localization from the live target authoring document before
/// falling back to the active workspace XAsset pool. Fonts remain canonical
/// runtime resources. No authored string or runtime provider is mutated.
/// </summary>
public sealed class MenuTextResourceResolver
    : IMenuTextResourceResolver,
      IDisposable
{
    private static readonly XAssetType[] CapturedAssetTypes =
    [
        XAssetType.Localize
    ];

    private static readonly LocalizeAuthoringAdapter LocalizeAdapter = new();

    private readonly FastFileEditingSession _editingSession;
    private readonly object _authoringGate = new();
    private AuthoredLocalizationIndex? _cachedAuthoring;
    private int _disposed;

    public MenuTextResourceResolver(FastFileEditingSession editingSession)
    {
        _editingSession = editingSession ?? throw new ArgumentNullException(
            nameof(editingSession));
        _editingSession.SemanticChanged += EditingSession_SemanticChanged;
    }

    public event EventHandler? Changed;

    public MenuTextResourceRevision Revision
    {
        get
        {
            ThrowIfDisposed();
            return CaptureRevision();
        }
    }

    public MenuLocalizedTextResolution ResolveText(string authoredText)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(authoredText);

        if (!authoredText.StartsWith('@'))
        {
            return MenuLocalizedTextResolution.Literal(
                authoredText,
                CaptureRevision());
        }

        string lookupName = authoredText[1..];
        if (lookupName.Length == 0)
        {
            return MenuLocalizedTextResolution.Missing(
                authoredText,
                lookupName,
                "The authored localization reference contains no key after '@'.",
                CaptureRevision());
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            MenuTextResourceRevision revision = CaptureRevision();
            if (!TryCaptureAuthoringAtRevision(
                    revision.EditingRevision,
                    out AuthoredLocalizationIndex? authoring))
            {
                continue;
            }

            MenuLocalizedTextResolution resolution = ResolveTextAtRevision(
                _editingSession.Workspace.Runtime.AssetPool,
                authoring!,
                authoredText,
                lookupName,
                revision);
            if (IsCurrent(revision))
                return resolution;
        }

        MenuTextResourceRevision currentRevision = CaptureRevision();
        return MenuLocalizedTextResolution.Missing(
            authoredText,
            lookupName,
            $"Localization '{lookupName}' changed while it was being resolved.",
            currentRevision);
    }

    public MenuFontAssetResolution ResolveFont(
        int fontEnum,
        MenuFontSelectionContext? context = null)
    {
        ThrowIfDisposed();
        MenuFontEnumResolution mapping = MenuFontEnumMapper.Resolve(
            fontEnum,
            context);
        XAssetPool pool = _editingSession.Workspace.Runtime.AssetPool;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            MenuTextResourceRevision revision = CaptureRevision();
            MenuFontAssetResolution resolution = mapping.IsKnown
                ? ResolveFontAtRevision(pool, mapping, revision)
                : MenuFontAssetResolution.Unknown(mapping, revision);
            if (IsCurrent(revision))
                return resolution;
        }

        return MenuFontAssetResolution.RevisionChanged(
            mapping,
            CaptureRevision());
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _editingSession.SemanticChanged -= EditingSession_SemanticChanged;
        lock (_authoringGate)
            _cachedAuthoring = null;
        Changed = null;
    }

    private void EditingSession_SemanticChanged(
        object? sender,
        FastFileEditingSessionChangedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            !args.Affects(XAssetType.Localize))
        {
            return;
        }

        lock (_authoringGate)
            _cachedAuthoring = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool TryCaptureAuthoringAtRevision(
        long expectedRevision,
        out AuthoredLocalizationIndex? authoring)
    {
        lock (_authoringGate)
        {
            ThrowIfDisposed();
            if (_cachedAuthoring is { } cached &&
                cached.EditingRevision == expectedRevision)
            {
                authoring = cached;
                return true;
            }

            FastFileEditingRowsSnapshot capture =
                _editingSession.CaptureRows(CapturedAssetTypes);
            if (capture.Revision != expectedRevision)
            {
                authoring = null;
                return false;
            }

            authoring = BuildAuthoringIndex(capture);
            _cachedAuthoring = authoring;
            return true;
        }
    }

    private static AuthoredLocalizationIndex BuildAuthoringIndex(
        FastFileEditingRowsSnapshot capture)
    {
        var values = new Dictionary<string, AuthoredLocalization>(
            StringComparer.Ordinal);
        foreach (FastFileEditingCapturedRow capturedRow in capture.Rows)
        {
            TargetZoneRowSource row = capturedRow.Row;
            if (row.State != TargetZoneRowSourceState.Definition)
                continue;

            LocalizeDraft draft = capturedRow.Draft switch
            {
                LocalizeDraft current => current,
                null => LocalizeAdapter.CreateDraft(
                    LocalizeAdapter.ImportAuthoredSnapshot(row)),
                { } incompatible => throw new InvalidDataException(
                    $"Localize target row {row.SerializedIndex} was captured " +
                    $"as '{incompatible.GetType().FullName}', not " +
                    $"'{typeof(LocalizeDraft).FullName}'.")
            };
            string? normalizedName = row.NormalizedKey;
            if (string.IsNullOrEmpty(normalizedName))
                continue;

            // Target traversal order matches serialized provider order. The
            // first full definition therefore retains canonical authority if
            // a malformed source contains duplicate Localize definitions.
            values.TryAdd(
                normalizedName,
                new AuthoredLocalization(
                    draft.Name ?? row.OriginalSerializedName ?? normalizedName,
                    draft.Value));
        }

        return new AuthoredLocalizationIndex(capture.Revision, values);
    }

    private static MenuLocalizedTextResolution ResolveTextAtRevision(
        XAssetPool pool,
        AuthoredLocalizationIndex authoring,
        string authoredText,
        string lookupName,
        MenuTextResourceRevision revision)
    {
        string normalizedName = XAssetStableIdentity.NormalizeLookupName(
            lookupName);
        if (authoring.Values.TryGetValue(
                normalizedName,
                out AuthoredLocalization? authored) &&
            authored is not null)
        {
            return authored.Value is { } value
                ? MenuLocalizedTextResolution.Resolved(
                    authoredText,
                    lookupName,
                    authored.Name,
                    value,
                    revision)
                : MenuLocalizedTextResolution.Missing(
                    authoredText,
                    lookupName,
                    $"Localization '{lookupName}' exists in target authoring but has no value.",
                    revision);
        }

        if (!pool.TryResolve(
                XAssetType.Localize,
                lookupName,
                out LocalizeAsset? localize) ||
            localize is null ||
            !HasCompleteCanonicalProvider(
                pool,
                localize,
                XAssetType.Localize) ||
            localize.Value is null)
        {
            return MenuLocalizedTextResolution.Missing(
                authoredText,
                lookupName,
                $"Localization '{lookupName}' is not available from target authoring or a complete active provider in the asset pool.",
                revision);
        }

        return MenuLocalizedTextResolution.Resolved(
            authoredText,
            lookupName,
            localize.Name ?? lookupName,
            localize.Value,
            revision);
    }

    private static MenuFontAssetResolution ResolveFontAtRevision(
        XAssetPool pool,
        MenuFontEnumResolution mapping,
        MenuTextResourceRevision revision)
    {
        string lookupName = mapping.LookupName ??
            throw new InvalidOperationException(
                "A known Font enum mapping must have a lookup identity.");
        if (!pool.TryResolve(
                XAssetType.Font,
                lookupName,
                out FontAsset? font) ||
            font is null ||
            !HasCompleteCanonicalProvider(pool, font, XAssetType.Font))
        {
            return MenuFontAssetResolution.Missing(mapping, revision);
        }

        return MenuFontAssetResolution.Resolved(mapping, font, revision);
    }

    private MenuTextResourceRevision CaptureRevision() => new(
        _editingSession.Workspace.Runtime.AssetPool.Revision,
        _editingSession.Revision);

    private bool IsCurrent(MenuTextResourceRevision revision) =>
        _editingSession.Workspace.Runtime.AssetPool.Revision ==
            revision.AssetPoolRevision &&
        _editingSession.Revision == revision.EditingRevision;

    private static bool HasCompleteCanonicalProvider(
        XAssetPool pool,
        BaseAsset asset,
        XAssetType expectedType)
    {
        if (asset.RuntimeAddress?.AssetPoolAddress is not { } address ||
            address.AssetType != expectedType ||
            !pool.TryGetSlot(address, out XAssetSlot? slot) ||
            slot is null ||
            slot.AssetType != expectedType ||
            slot.ActiveProvider.IsReferencePlaceholder)
        {
            return false;
        }

        return ReferenceEquals(slot.CanonicalAsset, asset);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(
        Volatile.Read(ref _disposed) != 0,
        this);

    private sealed record AuthoredLocalization(string Name, string? Value);

    private sealed record AuthoredLocalizationIndex(
        long EditingRevision,
        IReadOnlyDictionary<string, AuthoredLocalization> Values);
}
