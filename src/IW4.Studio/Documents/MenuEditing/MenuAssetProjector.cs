using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Pointers;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>
/// Boundary facade between detached linker-native Menu assets and immutable
/// editor snapshots. Native lowering is delegated to the typed compilers.
/// </summary>
internal static class MenuAssetProjector
{
    public static MenuEditorSnapshot Project(
        MenuDefAsset definition,
        MenuDocumentIdentity identity) =>
        MenuSnapshotFactory.Create(definition, identity);

    public static MenuFileEditorSnapshot Project(
        MenuFileAsset definition,
        MenuFileDocumentIdentity identity) =>
        MenuFileSnapshotProjector.Create(definition, identity);

    public static MenuDefAsset Apply(
        MenuDefAsset definition,
        MenuDocumentIdentity identity,
        MenuEdit edit,
        out MenuDocumentIdentity nextIdentity)
    {
        MenuDocumentCompiler.MenuEditResult result =
            MenuDocumentCompiler.Apply(definition, identity, edit);
        nextIdentity = result.Identity;
        return result.Data;
    }

    public static MenuFileAsset Apply(
        MenuFileAsset definition,
        MenuFileDocumentIdentity identity,
        MenuFileEdit edit,
        out MenuFileDocumentIdentity nextIdentity)
    {
        MenuFileEditResultAsset result = MenuFileDocumentCompiler.Apply(
            definition,
            identity,
            edit);
        nextIdentity = result.Identity;
        return result.Definition;
    }

    public static MenuFileAsset Clone(MenuFileAsset definition) => new()
    {
        NamePointer = definition.NamePointer,
        Name = definition.Name,
        MenuCount = definition.MenuCount,
        MenusPointer = definition.MenusPointer,
        Menus = definition.Menus.Select(reference => new MenuDefReference(
            reference.Index,
            reference.Pointer,
            reference.CanonicalMenu is null
                ? null
                : new MenuGraphClone(false).CloneMenu(reference.CanonicalMenu))).ToArray()
    };

    public static bool SemanticallyEquals(MenuDefAsset left, MenuDefAsset right)
    {
        MenuEditorSnapshot leftSnapshot = Project(left, MenuDocumentIdentity.Create(left));
        MenuEditorSnapshot rightSnapshot = Project(right, MenuDocumentIdentity.Create(right));
        return leftSnapshot.Settings == rightSnapshot.Settings &&
            leftSnapshot.Window.Value == rightSnapshot.Window.Value &&
            leftSnapshot.Items.Select(item => (item.Value, item.Behavior))
                .SequenceEqual(rightSnapshot.Items.Select(item =>
                    (item.Value, item.Behavior)));
    }

    public static bool SemanticallyEquals(MenuFileAsset left, MenuFileAsset right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        left.MenuCount == right.MenuCount &&
        left.Menus.Count == right.Menus.Count &&
        left.Menus.Zip(right.Menus).All(pair =>
            pair.First.Index == pair.Second.Index &&
            pair.First.Pointer == pair.Second.Pointer &&
            (pair.First.CanonicalMenu, pair.Second.CanonicalMenu) switch
            {
                (null, null) => true,
                (MenuDefAsset first, MenuDefAsset second) =>
                    SemanticallyEquals(first, second),
                _ => false
            });

    public static IReadOnlyList<AssetValidationIssue> Validate(
        MenuEditorSnapshot snapshot) => MenuEditorValidation.Validate(snapshot);

    public static IReadOnlyList<AssetValidationIssue> Validate(
        MenuFileEditorSnapshot snapshot) => MenuEditorValidation.Validate(snapshot);
}
