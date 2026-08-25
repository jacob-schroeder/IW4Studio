using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>Shape-correct defaults for a new top-level Menu definition.</summary>
internal static class MenuAuthoringDefaults
{
    public static MenuDefAsset CreateMenu(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new MenuDefAsset
        {
            Window = new WindowDef
            {
                Name = name,
                DynamicFlags = new WindowDynamicFlags[4]
            },
            CursorItems = new int[4],
            ImageTrack = (int)ImageTrackType.UI,
            ScaleTransitions = Transitions(),
            AlphaTransitions = Transitions(),
            XTransitions = Transitions(),
            YTransitions = Transitions(),
            Items = []
        };
    }

    private static IReadOnlyList<MenuTransition> Transitions() =>
        [new MenuTransition(), new MenuTransition(), new MenuTransition(),
            new MenuTransition()];
}

public sealed class MenuDraft
{
    internal MenuDraft(MenuDefAsset value) =>
        Definition = new MenuGraphClone(false).CloneMenu(value);

    internal MenuDefAsset Definition { get; }

    internal MenuDraft Clone() => new(Definition);

    internal MenuEditorSnapshot Snapshot =>
        MenuAssetProjector.Project(Definition, MenuDocumentIdentity.Create(Definition));
}

public sealed class MenuFileDraft
{
    internal MenuFileDraft(MenuFileAsset value)
    {
        Definition = new MenuFileAsset
        {
            Name = value.Name,
            MenuCount = value.MenuCount,
            Menus = value.Menus.Select(
                MenuAssetProjector.CloneRegistration).ToArray()
        };
    }

    internal MenuFileAsset Definition { get; }

    internal MenuFileDraft Clone() => new(Definition);

    internal MenuFileEditorSnapshot Snapshot =>
        MenuAssetProjector.Project(
            Definition,
            MenuFileDocumentIdentity.Create(Definition));
}

internal sealed class MenuAdapter : AssetAuthoringAdapter<MenuDefAsset, MenuDraft>
{
    public override XAssetType AssetType => XAssetType.Menu;

    public override MenuDraft CreateDraft(MenuDefAsset value) => new(value);

    public override MenuDraft CloneDraft(MenuDraft value) => value.Clone();

    public override MenuDefAsset CreateDefinition(MenuDraft value) =>
        new MenuGraphClone(false).CloneMenu(value.Definition);

    public override IReadOnlyList<AssetValidationIssue> Validate(MenuDraft value) =>
        MenuAssetProjector.Validate(value.Snapshot);

    public override bool SemanticallyEquals(MenuDraft left, MenuDraft right) =>
        MenuAssetProjector.SemanticallyEquals(left.Definition, right.Definition);
}

internal sealed class MenuFileAdapter
    : AssetAuthoringAdapter<MenuFileAsset, MenuFileDraft>
{
    public override XAssetType AssetType => XAssetType.MenuFile;

    public override MenuFileDraft CreateDraft(MenuFileAsset value) => new(value);

    public override MenuFileDraft CloneDraft(MenuFileDraft value) => value.Clone();

    public override MenuFileAsset CreateDefinition(MenuFileDraft value) =>
        value.Clone().Definition;

    public override IReadOnlyList<AssetValidationIssue> Validate(MenuFileDraft value) =>
        MenuAssetProjector.Validate(value.Snapshot);

    public override bool SemanticallyEquals(MenuFileDraft left, MenuFileDraft right) =>
        MenuAssetProjector.SemanticallyEquals(left.Definition, right.Definition);
}
