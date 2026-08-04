using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;
using IW4.Runtime.Assets;
using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Documents;

/// <summary>Detached Menu root state.  Recursive item/event/expression
/// payloads are cloned as an identity-preserving detached graph; packed
/// addresses are retained only as source provenance, never as runtime data.</summary>
public sealed class MenuAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal MenuAuthoredSnapshot(MenuBuildData data) => Data = data.Copy();
    private MenuAuthoredSnapshot(MenuBuildData data, bool takeOwnership)
    {
        if (!takeOwnership)
            throw new ArgumentException("Detached Menu snapshot ownership must be explicit.", nameof(takeOwnership));
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }
    internal MenuBuildData Data { get; }
    public XAssetType AssetType => XAssetType.Menu;
    internal static MenuAuthoredSnapshot Import(TargetZoneRowSource source) =>
        source.AuthoredDefinition?.SemanticSnapshot is MenuAuthoredSnapshot snapshot
            ? snapshot : throw new InvalidDataException("Menu editing requires a capture-time detached semantic snapshot.");
    internal static MenuAuthoredSnapshot FromLoaded(MenuDefAsset value) =>
        FromLoaded(value, new MenuGraphClone());
    internal static MenuAuthoredSnapshot FromLoaded(MenuDefAsset value, MenuGraphClone graph) =>
        new(MenuBuildData.FromLoaded(value, graph), takeOwnership: true);
}

public sealed class MenuBuildData : IMenuBuildData
{
    private MenuBuildData(MenuDefAsset definition, bool isComplete)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        IsComplete = isComplete;
    }

    public XAssetType AssetType => XAssetType.Menu;
    public bool IsComplete { get; }
    public MenuDefAsset Definition { get; }

    internal static MenuBuildData FromLoaded(MenuDefAsset value) => FromLoaded(value, new MenuGraphClone());

    internal static MenuBuildData FromLoaded(MenuDefAsset value, MenuGraphClone graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        MenuDefAsset definition = graph.CloneMenu(value);
        return new(definition, true);
    }

    internal static MenuBuildData CreateOwned(MenuDefAsset definition, bool isComplete = true) =>
        new(definition, isComplete);

    internal MenuBuildData Copy() => Copy(new MenuGraphClone());

    internal MenuBuildData Copy(MenuGraphClone graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        MenuDefAsset definition = graph.CloneMenu(Definition);
        return new(definition, IsComplete);
    }
}

public sealed class MenuFileAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal MenuFileAuthoredSnapshot(MenuFileBuildData data) => Data = data.Copy();
    private MenuFileAuthoredSnapshot(MenuFileBuildData data, bool takeOwnership)
    {
        if (!takeOwnership)
            throw new ArgumentException("Detached MenuFile snapshot ownership must be explicit.", nameof(takeOwnership));
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }
    internal MenuFileBuildData Data { get; }
    public XAssetType AssetType => XAssetType.MenuFile;
    internal static MenuFileAuthoredSnapshot Import(TargetZoneRowSource source) => source.AuthoredDefinition?.SemanticSnapshot is MenuFileAuthoredSnapshot snapshot ? snapshot : throw new InvalidDataException("MenuFile editing requires a capture-time detached semantic snapshot.");
    internal static MenuFileAuthoredSnapshot FromLoaded(MenuFileAsset value) =>
        FromLoaded(value, new MenuGraphClone());
    internal static MenuFileAuthoredSnapshot FromLoaded(MenuFileAsset value, MenuGraphClone graph) =>
        new(MenuFileBuildData.FromLoaded(value, graph), takeOwnership: true);
}

public sealed class MenuFileBuildData : IMenuFileBuildData
{
    private readonly NestedXAssetBuildLink[] _menuLinks;
    private readonly IReadOnlyList<NestedXAssetBuildLink> _menuLinkView;

    internal MenuFileBuildData(
        string? name,
        IEnumerable<NestedXAssetBuildLink> menuLinks)
        : this(
            name,
            CloneLinks(menuLinks, new MenuGraphClone()),
            takeOwnership: true)
    {
    }

    private MenuFileBuildData(
        string? name,
        NestedXAssetBuildLink[] menuLinks,
        bool takeOwnership)
    {
        if (!takeOwnership)
            throw new ArgumentException("Detached MenuFile build-data ownership must be explicit.", nameof(takeOwnership));
        Name = name;
        _menuLinks = menuLinks ?? throw new ArgumentNullException(nameof(menuLinks));
        _menuLinkView = Array.AsReadOnly(_menuLinks);
    }

    public XAssetType AssetType => XAssetType.MenuFile;
    public string? Name { get; }
    public IReadOnlyList<NestedXAssetBuildLink> MenuLinks => _menuLinkView;

    internal static MenuFileBuildData CreateOwned(
        string? name,
        IEnumerable<NestedXAssetBuildLink> menuLinks) =>
        new(name, menuLinks.ToArray(), takeOwnership: true);

    internal MenuFileBuildData Copy() => Copy(new MenuGraphClone());
    internal MenuFileBuildData Copy(MenuGraphClone graph) =>
        new(Name, CloneLinks(_menuLinks, graph), takeOwnership: true);

    internal static MenuFileBuildData FromLoaded(MenuFileAsset value, MenuGraphClone graph)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(graph);
        return new MenuFileBuildData(
            value.Name,
            value.Menus.Select(entry => Link(entry, graph))
                .ToArray(),
            takeOwnership: true);
    }

    internal IReadOnlyList<MenuBuildData?> GetRegistrationDefinitions() =>
        Array.AsReadOnly(_menuLinks
            .Select(link => link.IncomingDefinition as MenuBuildData)
            .ToArray());

    private static NestedXAssetBuildLink Link(
        MenuDefReference entry,
        MenuGraphClone graph)
    {
        MenuDefAsset? identitySource = entry.IncomingDefinition ?? entry.CanonicalMenu;
        string name = identitySource?.Window.Name
            ?? throw new InvalidDataException(
                $"MenuFile registration {entry.Index} has no logical Menu identity.");
        bool isExternalReferenceStub = entry.IncomingDefinition?.Window.Name is { } incomingName &&
            XAssetStableIdentity.IsReferenceName(incomingName);
        NestedXAssetPointerSourceForm sourceForm = entry.Pointer.Type switch
        {
            PointerType.Inline => NestedXAssetPointerSourceForm.Inline,
            PointerType.Insert => NestedXAssetPointerSourceForm.Insert,
            PointerType.Offset => NestedXAssetPointerSourceForm.PackedAlias,
            _ => throw new InvalidDataException(
                $"MenuFile registration {entry.Index} has unsupported pointer form '{entry.Pointer.Type}'.")
        };
        if (isExternalReferenceStub)
            sourceForm = NestedXAssetPointerSourceForm.PackedAlias;
        MenuBuildData? incoming = entry.IncomingDefinition is null || isExternalReferenceStub
            ? null
            : MenuBuildData.FromLoaded(entry.IncomingDefinition, graph);
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(XAssetType.Menu, name),
            sourceForm,
            incoming,
            sourceForm == NestedXAssetPointerSourceForm.PackedAlias &&
            entry.Pointer.Type == PointerType.Offset
                ? entry.Pointer.Raw
                : null,
            entry.Pointer.CellAddress is { } ownerCell
                ? XPointerCodec.Encode(ownerCell)
                : null);
    }

    private static NestedXAssetBuildLink[] CloneLinks(
        IEnumerable<NestedXAssetBuildLink> links,
        MenuGraphClone graph)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(graph);
        return links.Select(link =>
        {
            ArgumentNullException.ThrowIfNull(link);
            IXAssetBuildData? incoming = link.IncomingDefinition switch
            {
                null => null,
                MenuBuildData menu => menu.Copy(graph),
                _ => throw new InvalidDataException(
                    $"MenuFile link contains non-Menu definition '{link.IncomingDefinition.AssetType}'.")
            };
            return link with { IncomingDefinition = incoming };
        }).ToArray();
    }
}

public sealed class MenuFileDraft
{
    private readonly MenuFileWorkingDocument _document;

    internal MenuFileDraft(MenuFileBuildData data) =>
        _document = new MenuFileWorkingDocument(data);

    private MenuFileDraft(MenuFileWorkingDocument document) =>
        _document = document;

    internal MenuFileBuildData Data => _document.Export();
    public MenuFileEditorSnapshot Snapshot => _document.Snapshot;

    public void Apply(MenuFileEdit edit) => _document.Apply(edit);

    internal void Replace(MenuFileBuildData value) => _document.Replace(value);

    internal MenuFileDraft Clone() => new(_document.Clone());
}

public sealed class MenuFileAuthoringAdapter : AssetAuthoringAdapter<MenuFileAuthoredSnapshot, MenuFileDraft, MenuFileBuildData>
{
    private static readonly MenuFileBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.MenuFile;
    public override MenuFileAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => MenuFileAuthoredSnapshot.Import(source);
    public override MenuFileDraft CreateDraft(MenuFileAuthoredSnapshot snapshot) => new(snapshot.Data);
    public override MenuFileDraft CloneDraft(MenuFileDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(MenuFileDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        MenuFileBuildData data = draft.Data;
        return MenuEditorValidation.Combine(
            MenuEditorValidation.Validate(draft.Snapshot),
            Validator.Validate(data));
    }

    public override bool SemanticallyEquals(MenuFileDraft left, MenuFileDraft right)
    {
        MenuFileBuildData a = left.Data, b = right.Data;
        return a.Name == b.Name &&
            a.MenuLinks.Count == b.MenuLinks.Count &&
            a.MenuLinks.Zip(b.MenuLinks).All(pair =>
                SameLink(pair.First, pair.Second));
    }

    public override MenuFileBuildData ExportBuildData(MenuFileDraft draft)
    {
        MenuFileBuildData data = draft.Data;
        if (ValidateDraft(draft).Any(issue => issue.Severity == AssetValidationSeverity.Error))
            throw new InvalidOperationException("MenuFile draft has validation errors and cannot produce build data.");
        return data;
    }

    private static bool SameLink(
        NestedXAssetBuildLink left,
        NestedXAssetBuildLink right)
    {
        if (left.Reference != right.Reference || left.SourceForm != right.SourceForm)
            return false;
        if (left.IncomingDefinition is null || right.IncomingDefinition is null)
            return left.IncomingDefinition is null && right.IncomingDefinition is null;
        return left.IncomingDefinition is MenuBuildData leftMenu &&
            right.IncomingDefinition is MenuBuildData rightMenu &&
            MenuSemanticProjection.Serialize(leftMenu.Definition) ==
            MenuSemanticProjection.Serialize(rightMenu.Definition);
    }
}

public sealed class MenuDraft
{
    private readonly MenuWorkingDocument _document;

    internal MenuDraft(MenuBuildData data) =>
        _document = new MenuWorkingDocument(data);

    private MenuDraft(MenuWorkingDocument document) =>
        _document = document;

    internal MenuBuildData Data => _document.Export();
    public MenuEditorSnapshot Snapshot => _document.Snapshot;

    public void Apply(MenuEdit edit) => _document.Apply(edit);

    internal void Replace(MenuBuildData value) => _document.Replace(value);

    internal MenuDraft Clone() => new(_document.Clone());
}

public sealed class MenuAuthoringAdapter : AssetAuthoringAdapter<MenuAuthoredSnapshot, MenuDraft, MenuBuildData>
{
    private static readonly MenuBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.Menu;
    public override MenuAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => MenuAuthoredSnapshot.Import(source);
    public override MenuDraft CreateDraft(MenuAuthoredSnapshot snapshot) => new(snapshot.Data);
    public override MenuDraft CloneDraft(MenuDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(MenuDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        MenuBuildData data = draft.Data;
        return MenuEditorValidation.Combine(
            MenuEditorValidation.Validate(draft.Snapshot),
            Validator.Validate(data));
    }

    public override bool SemanticallyEquals(MenuDraft left, MenuDraft right) => MenuSemanticProjection.Serialize(left.Data.Definition) == MenuSemanticProjection.Serialize(right.Data.Definition);

    public override MenuBuildData ExportBuildData(MenuDraft draft)
    {
        MenuBuildData data = draft.Data;
        if (ValidateDraft(draft).Any(issue => issue.Severity == AssetValidationSeverity.Error))
            throw new InvalidOperationException("Menu draft has validation errors and cannot produce build data.");
        return data;
    }
}
