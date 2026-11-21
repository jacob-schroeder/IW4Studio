using IW4.Game.Assets.XModel;
using IW4.Render.EditorPreview;

namespace IW4.Studio.Desktop.Editors.Weapon;

internal static class WeaponAnimationAssembly
{
    internal static IReadOnlyList<XAnimPreviewModelComponent> Components(
        XModelAsset hands,
        XModelAsset viewModel,
        XModelAsset? rocket,
        XModelAsset? knife)
    {
        var components = new List<XAnimPreviewModelComponent>
        {
            new(hands),
            new(viewModel, "tag_weapon")
        };
        if (rocket is not null)
            components.Add(new XAnimPreviewModelComponent(rocket, "tag_clip"));
        if (knife is not null)
            components.Add(new XAnimPreviewModelComponent(knife, "tag_knife_attach"));
        return components;
    }
}
