using IW4.Formats.SourceFormat.XAnim;
using IW4.Game.Assets.Weapon;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Render;
using IW4.Render.EditorPreview;
using IW4.Runtime.Assets;
using IW4.Studio.Desktop.Editors.Weapon;
using IW4.Studio.Desktop.Editors.XModel;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.XAnim;

internal sealed class XAnimWeaponPreview
{
    private readonly FastFileWorkspace _workspace;
    private readonly WeaponAsset _weapon;
    private readonly bool _includeKnife;

    private XAnimWeaponPreview(
        FastFileWorkspace workspace,
        WeaponAsset weapon,
        bool includeKnife)
    {
        _workspace = workspace;
        _weapon = weapon;
        _includeKnife = includeKnife;
    }

    internal static IEnumerable<XAnimPreviewReference> Find(
        FastFileWorkspace workspace,
        string animationName)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(animationName);
        if (workspace.IsBlank)
            yield break;

        string clipName = XAssetStableIdentity.NormalizeLookupName(animationName);
        foreach (var entry in workspace.LoadedZone.Context.AssetPool.Entries)
        {
            if (entry.AssetType != XAssetType.Weapon || entry.IsReferencePlaceholder ||
                entry.Asset is not WeaponAsset weapon)
                continue;

            bool matched = false;
            bool includeKnife = false;
            foreach (IReadOnlyList<string?> names in AnimationNames(weapon))
            {
                for (int index = 0; index < names.Count; index++)
                {
                    if (names[index] is not { Length: > 0 } name ||
                        XAssetStableIdentity.NormalizeLookupName(name) != clipName)
                        continue;
                    matched = true;
                    includeKnife |= index is (int)WeaponAnimationSlot.Melee or (int)WeaponAnimationSlot.MeleeCharge;
                }
            }
            if (matched)
            {
                var assembly = new XAnimWeaponPreview(workspace, weapon, includeKnife);
                yield return new XAnimPreviewReference(entry.Name, "Weapon", XModelCameraPreset.Side, assembly.Build);
            }
        }
    }

    internal (XAnimPreviewScene? PoseScene, XModelRenderScene? ModelScene, string? Reason)
        Build(XAnimPlaybackClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        WeaponDef? definition = _weapon.Definition;
        if (definition is null)
            return (null, null, "The weapon definition is unavailable.");

        if (!TryResolveModel(definition.HandModel, out XModelAsset? hands) || hands is null)
            return (null, null, "The first-person hand/arms model is unresolved.");
        XModelAsset? viewReference = definition.GunModels.FirstOrDefault();
        if (!TryResolveModel(viewReference, out XModelAsset? viewModel) || viewModel is null)
            return (null, null, "View model index 0 is empty or unresolved.");

        if (!TryResolveOptional(definition.RocketModel, "Rocket model",
                out XModelAsset? rocket, out string? reason))
            return (null, null, reason);
        XModelAsset? knife = null;
        if (_includeKnife && !TryResolveOptional(definition.KnifeModel, "Knife model",
                out knife, out reason))
            return (null, null, reason);

        return XAnimModelPreview.Build(
            _workspace,
            clip,
            WeaponAnimationAssembly.Components(hands, viewModel, rocket, knife),
            []);
    }

    private static IEnumerable<IReadOnlyList<string?>> AnimationNames(WeaponAsset weapon)
    {
        yield return weapon.Variant.AnimationNames;
        if (weapon.Definition is not { } definition)
            yield break;
        yield return definition.RightHandAnimationNames;
        yield return definition.LeftHandAnimationNames;
    }

    private bool TryResolveModel(XModelAsset? reference,
        out XModelAsset? resolved)
    {
        resolved = null;
        return !string.IsNullOrWhiteSpace(reference?.Name) &&
            XAnimModelPreview.TryResolveModel(
                _workspace, reference.Name, out resolved, out _);
    }

    private bool TryResolveOptional(XModelAsset? reference,
        string role, out XModelAsset? model, out string? reason)
    {
        model = null;
        reason = null;
        if (reference is null)
            return true;
        if (!TryResolveModel(reference, out model) || model is null)
        {
            reason = $"{role} is present but unresolved.";
            return false;
        }
        return true;
    }
}
