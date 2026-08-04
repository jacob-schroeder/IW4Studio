using IW4.Assets.Assets;
using IW4.Assets.Assets.Font;
using IW4.Assets.Assets.Localize;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Rendering;

/// <summary>
/// Resolves Menu text resources through the active workspace XAsset pool.
/// The resolver never mutates authored strings and never falls back to a
/// non-canonical provider object retained by a Menu graph.
/// </summary>
public sealed class MenuTextResourceResolver : IMenuTextResourceResolver
{
    private readonly FastFileWorkspace _workspace;

    public MenuTextResourceResolver(FastFileWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(
            nameof(workspace));
    }

    public long Revision => _workspace.Runtime.AssetPool.Revision;

    public MenuLocalizedTextResolution ResolveText(string authoredText)
    {
        ArgumentNullException.ThrowIfNull(authoredText);

        XAssetPool pool = _workspace.Runtime.AssetPool;
        if (!authoredText.StartsWith('@'))
        {
            return MenuLocalizedTextResolution.Literal(
                authoredText,
                pool.Revision);
        }

        string lookupName = authoredText[1..];
        if (lookupName.Length == 0)
        {
            return MenuLocalizedTextResolution.Missing(
                authoredText,
                lookupName,
                "The authored localization reference contains no key after '@'.",
                pool.Revision);
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            long revision = pool.Revision;
            MenuLocalizedTextResolution resolution = ResolveTextAtRevision(
                pool,
                authoredText,
                lookupName,
                revision);
            if (pool.Revision == revision)
                return resolution;
        }

        long currentRevision = pool.Revision;
        return MenuLocalizedTextResolution.Missing(
            authoredText,
            lookupName,
            $"Localization '{lookupName}' changed providers while it was being resolved.",
            currentRevision);
    }

    public MenuFontAssetResolution ResolveFont(
        int fontEnum,
        MenuFontSelectionContext? context = null)
    {
        MenuFontEnumResolution mapping = MenuFontEnumMapper.Resolve(
            fontEnum,
            context);
        XAssetPool pool = _workspace.Runtime.AssetPool;
        if (!mapping.IsKnown)
            return MenuFontAssetResolution.Unknown(mapping, pool.Revision);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            long revision = pool.Revision;
            MenuFontAssetResolution resolution = ResolveFontAtRevision(
                pool,
                mapping,
                revision);
            if (pool.Revision == revision)
                return resolution;
        }

        return MenuFontAssetResolution.RevisionChanged(
            mapping,
            pool.Revision);
    }

    private static MenuLocalizedTextResolution ResolveTextAtRevision(
        XAssetPool pool,
        string authoredText,
        string lookupName,
        long revision)
    {
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
                $"Localization '{lookupName}' is not available from a complete active provider in the asset pool.",
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
        long revision)
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
}
