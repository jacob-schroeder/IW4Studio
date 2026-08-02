using IW4.Assets.Assets.ComWorld;
using IW4.Runtime.Assets;
using IW4.Render.Scheduling;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// Static ComWorld-derived selector columns and eligibility bytes. Dynamic
/// shadow-map availability bits remain frame input and are not retained here.
/// </summary>
public sealed class MapRenderWorldSceneLightSource
{
    internal MapRenderWorldSceneLightSource(
        ComWorldAsset comWorld,
        XAssetHandle<ComWorldAsset> handle,
        XAssetActiveProviderSnapshot provider,
        MapRenderSceneLightSelectorAssetState selectorState)
    {
        ArgumentNullException.ThrowIfNull(comWorld);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(selectorState);
        if (handle.IsNone ||
            handle.Address != provider.SlotAddress ||
            provider.IsReferencePlaceholder ||
            !provider.IsActiveCanonicalProvider ||
            !provider.CanonicalProjectionMatchesProviderAsset ||
            selectorState.SceneLightCount != comWorld.PrimaryLightCount)
        {
            throw new ArgumentException(
                "Scene-light source does not describe one active canonical ComWorld projection.",
                nameof(provider));
        }

        ComWorld = comWorld;
        Handle = handle;
        Provider = provider with { };
        SelectorState = selectorState;
    }

    public ComWorldAsset ComWorld { get; }

    public XAssetHandle<ComWorldAsset> Handle { get; }

    public XAssetActiveProviderSnapshot Provider { get; }

    public MapRenderSceneLightSelectorAssetState SelectorState { get; }
}
