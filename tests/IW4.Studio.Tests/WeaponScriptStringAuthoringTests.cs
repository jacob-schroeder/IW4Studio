using IW4.Game.Assets.Weapon;
using IW4.Game.ScriptStrings;
using IW4.Game.Zone;
using IW4.Studio.Documents;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class WeaponScriptStringAuthoringTests
{
    [Fact]
    public void Hide_tag_edit_preserves_null_empty_and_named_values_through_definition_creation()
    {
        var source = new WeaponAsset
        {
            Variant = new WeaponVariantDef
            {
                InternalName = "test_weapon",
                HideTags = [Reference(null)]
            }
        };
        IAssetAuthoringAdapter adapter = AssetAuthoringAdapterRegistry.CreateDefault()
            .RequireAdapter(XAssetType.Weapon);
        var draft = Assert.IsType<WeaponDraft>(adapter.CreateDraft(source));

        draft.SetVariantHideTags(0, Reference(string.Empty));
        Assert.Equal(string.Empty, draft.Variant.HideTags[0].Text);
        Assert.Equal(string.Empty,
            Assert.IsType<WeaponAsset>(adapter.CreateDefinition(draft)).Variant.HideTags[0].Text);

        draft.SetVariantHideTags(0, Reference("tag_weapon"));
        Assert.Equal("tag_weapon", draft.Variant.HideTags[0].Text);

        draft.SetVariantHideTags(0, Reference(null));
        Assert.Null(draft.Variant.HideTags[0].Text);
        Assert.Null(Assert.IsType<WeaponAsset>(adapter.CreateDefinition(draft))
            .Variant.HideTags[0].Text);
        Assert.Null(source.Variant.HideTags[0].Text);
    }

    private static ScriptStringReference Reference(string? text) =>
        new(0, text, ScriptStringHandle.Null, default);
}
