using IW4.Assets.Assets.Font;
using IW4.Assets.Assets.Material;
using IW4.Render.Textures;
using IW4.Render.UI;
using IW4.Runtime.Assets.Images;
using IW4.Studio.Desktop.Rendering;

namespace IW4.Studio.Desktop.Editors.Font;

/// <summary>
/// Resolves a detached compiled Font closure immediately while delegating
/// unchanged workspace materials to the canonical runtime-pool resolver.
/// </summary>
internal sealed class FontPreviewMaterialResolver : IMenuPreviewMaterialResolver
{
    private readonly IMenuPreviewMaterialResolver _workspaceResolver;
    private readonly IReadOnlyDictionary<string, MaterialAsset> _localMaterials;
    private readonly long _localRevision;

    public FontPreviewMaterialResolver(
        IMenuPreviewMaterialResolver workspaceResolver,
        FontAsset font,
        long localRevision)
    {
        _workspaceResolver = workspaceResolver ??
            throw new ArgumentNullException(nameof(workspaceResolver));
        ArgumentNullException.ThrowIfNull(font);
        _localRevision = localRevision;
        _localMaterials = new[] { font.Material, font.GlowMaterial }
            .OfType<MaterialAsset>()
            .Where(material => !string.IsNullOrWhiteSpace(material.Info.Name))
            .GroupBy(material => material.Info.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
    }

    public long Revision => unchecked(
        (_workspaceResolver.Revision * 397) ^ _localRevision);

    public Task<MenuPreviewMaterialResolution> ResolveAsync(
        string materialName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialName);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_localMaterials.TryGetValue(materialName, out MaterialAsset? material) ||
            !HasInlineAtlas(material))
        {
            return _workspaceResolver.ResolveAsync(materialName, cancellationToken);
        }

        return Task.FromResult(ResolveDetached(material, materialName));
    }

    private MenuPreviewMaterialResolution ResolveDetached(
        MaterialAsset material,
        string requestedName)
    {
        try
        {
            UiMaterialPreviewPlan plan = UiMaterialPreviewPlanner.Plan(
                material,
                (_, row) => row.Image is { } image
                    ? UiMaterialPreviewImageResolution.Canonical(image)
                    : UiMaterialPreviewImageResolution.Unavailable(
                        "The detached font material row has no image."));
            if (!plan.CanAttemptTextureDecode ||
                plan.SelectedImage is not { } atlas)
            {
                string reason = plan.Blockers.Count == 0
                    ? "No detached 2D atlas image is available."
                    : string.Join(" ", plan.Blockers.Select(blocker => blocker.Message));
                return MenuPreviewMaterialResolution.Failed(
                    $"Compiled font material '{requestedName}' cannot be previewed: {reason}",
                    Revision,
                    plan.Diagnostics);
            }

            if (!GfxImagePreviewDecoder.TryDecodeBestAvailable(
                    atlas,
                    UnavailableGfxImagePayloadResolver.Instance,
                    out GfxImagePreviewSnapshot? preview,
                    out string decodeReason) ||
                preview is null)
            {
                return MenuPreviewMaterialResolution.Failed(
                    $"Compiled font atlas '{atlas.Name ?? "unnamed image"}' could not be decoded: {decodeReason}",
                    Revision,
                    plan.Diagnostics);
            }

            return MenuPreviewMaterialResolution.Resolved(
                new MenuPreviewMaterialSnapshot(plan, preview),
                Revision);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return MenuPreviewMaterialResolution.Failed(
                $"Compiled font material '{requestedName}' preview failed: {exception.Message}",
                Revision);
        }
    }

    private static bool HasInlineAtlas(MaterialAsset material) =>
        material.Textures.Any(texture =>
            texture.Image is { PayloadBytes.Count: > 0 });
}
