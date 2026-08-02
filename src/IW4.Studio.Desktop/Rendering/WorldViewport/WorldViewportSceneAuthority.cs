using IW4.Runtime.Assets;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.SavePlanning;
using IW4.Studio.Rendering;

namespace IW4.Studio.Desktop.Rendering.WorldViewport;

/// <summary>
/// Immutable authority expected by one map-editor viewport. The source
/// document identifies the loaded fastfile; the semantic document and
/// normalized asset names identify the imported map within that document.
/// </summary>
internal readonly record struct WorldViewportSceneAuthority
{
    internal WorldViewportSceneAuthority(
        Guid sourceDocumentId,
        MapDocumentId semanticDocumentId,
        string gfxMapIdentity,
        string? clipMapIdentity)
    {
        if (sourceDocumentId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceDocumentId));
        if (semanticDocumentId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(semanticDocumentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(gfxMapIdentity);

        SourceDocumentId = sourceDocumentId;
        SemanticDocumentId = semanticDocumentId;
        GfxMapIdentity = Normalize(gfxMapIdentity);
        ClipMapIdentity = clipMapIdentity is null
            ? null
            : Normalize(clipMapIdentity);
    }

    internal Guid SourceDocumentId { get; }

    internal MapDocumentId SemanticDocumentId { get; }

    internal string GfxMapIdentity { get; }

    internal string? ClipMapIdentity { get; }

    internal static WorldViewportSceneAuthority From(
        CompiledMapBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        CompiledMapAssetDescriptor gfx = bundle.Assets.Single(
            asset => asset.Kind == MapAssetKind.GfxMap);
        CompiledMapAssetDescriptor[] collision = bundle.Assets
            .Where(asset =>
                asset.Kind is
                    MapAssetKind.ColMapSp or
                    MapAssetKind.ColMapMp)
            .ToArray();
        if (collision.Length > 1)
        {
            throw new InvalidDataException(
                "A viewport authority cannot contain multiple collision-map roots.");
        }
        string mapIdentity = Normalize(bundle.MapIdentity);
        if (!Matches(gfx.AssetName, mapIdentity) ||
            (collision.Length == 1 &&
             !Matches(collision[0].AssetName, mapIdentity)))
        {
            throw new InvalidDataException(
                "The compiled-map root descriptors do not match the bundle map identity.");
        }

        return new WorldViewportSceneAuthority(
            bundle.SourceDocumentId,
            bundle.DocumentId,
            mapIdentity,
            collision.SingleOrDefault()?.AssetName);
    }

    internal bool TryValidate(
        RenderViewSceneBuildResult result,
        MapEditorLivePreviewBridge bridge,
        out string failure)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bridge);

        return TryValidate(
            result.SourceDocumentId,
            result.SourceZone.IsTarget,
            bridge.Document.Id,
            bridge.Document.MapIdentity,
            result.GfxWorld?.Name,
            result.ClipMap?.Name,
            result.Scene?.Name,
            out failure);
    }

    internal bool TryValidate(
        Guid sourceDocumentId,
        bool sourceZoneIsTarget,
        MapDocumentId semanticDocumentId,
        string? semanticMapIdentity,
        string? gfxMapName,
        string? clipMapName,
        string? sceneName,
        out string failure)
    {
        if (sourceDocumentId != SourceDocumentId)
        {
            failure =
                "The prepared render scene belongs to a different Studio source document.";
            return false;
        }
        if (!sourceZoneIsTarget)
        {
            failure =
                "The prepared render scene was not built from the Studio target zone.";
            return false;
        }
        if (semanticDocumentId != SemanticDocumentId)
        {
            failure =
                "The Live Preview bridge belongs to a different semantic map document.";
            return false;
        }
        if (!Matches(semanticMapIdentity, GfxMapIdentity))
        {
            failure =
                "The Live Preview bridge belongs to a different semantic map identity.";
            return false;
        }
        if (!Matches(gfxMapName, GfxMapIdentity))
        {
            failure =
                "The prepared render scene has no authoritative matching GfxMap identity.";
            return false;
        }
        if (!Matches(sceneName, GfxMapIdentity))
        {
            failure =
                "The prepared render scene name does not match the imported GfxMap identity.";
            return false;
        }
        if (ClipMapIdentity is null)
        {
            if (clipMapName is not null)
            {
                failure =
                    "The prepared render scene contains collision authority absent from the imported map bundle.";
                return false;
            }
        }
        else if (!Matches(clipMapName, ClipMapIdentity))
        {
            failure =
                "The prepared render scene has no authoritative matching ClipMap identity.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static bool Matches(string? value, string expected) =>
        !string.IsNullOrWhiteSpace(value) &&
        string.Equals(
            Normalize(value),
            expected,
            StringComparison.Ordinal);

    private static string Normalize(string value) =>
        XAssetStableIdentity.NormalizeLookupName(value);
}
