using IW4.Assets.Assets.Menu;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

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
    private MenuBuildData(MenuDefAsset definition, MenuReferenceBuildData references, bool isComplete) { Definition = definition; References = references; IsComplete = isComplete; }
    public XAssetType AssetType => XAssetType.Menu;
    public bool IsComplete { get; }
    public MenuDefAsset Definition { get; }
    public MenuReferenceBuildData References { get; }

    internal static MenuBuildData FromLoaded(MenuDefAsset value) => FromLoaded(value, new MenuGraphClone());

    internal static MenuBuildData FromLoaded(MenuDefAsset value, MenuGraphClone graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        MenuDefAsset definition = graph.CloneMenu(value);
        return new(definition, new MenuReferenceBuildData
        {
            WindowBackgroundMaterial = Reference(definition.Window.BackgroundMaterialName)
        }, true);
    }
    internal static MenuBuildData Unresolved() => new(new MenuDefAsset(), new MenuReferenceBuildData(), false);
    internal MenuBuildData Copy() => Copy(new MenuGraphClone());

    internal MenuBuildData Copy(MenuGraphClone graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        MenuDefAsset definition = graph.CloneMenu(Definition);
        return new(definition, new MenuReferenceBuildData { WindowBackgroundMaterial = References.WindowBackgroundMaterial }, IsComplete);
    }

    private static MenuDefAsset Clone(MenuDefAsset value) => new()
    {
        Window = Window(value.Window), FontPointer = Ptr(value.FontPointer), Font = value.Font, Fullscreen = value.Fullscreen, ItemCount = value.ItemCount,
        FontIndex = value.FontIndex, CursorItems = value.CursorItems.ToArray(), FadeCycle = value.FadeCycle, FadeClamp = value.FadeClamp,
        FadeAmount = value.FadeAmount, FadeInAmount = value.FadeInAmount, BlurRadius = value.BlurRadius,
        OnOpen = Ptr(value.OnOpen), OnCloseRequest = Ptr(value.OnCloseRequest), OnClose = Ptr(value.OnClose), OnEsc = Ptr(value.OnEsc),
        ExecKeys = Ptr(value.ExecKeys), VisibleExpression = Ptr(value.VisibleExpression), AllowedBinding = Ptr(value.AllowedBinding),
        AllowedBindingString = value.AllowedBindingString, SoundName = Ptr(value.SoundName), SoundNameString = value.SoundNameString,
        ImageTrack = value.ImageTrack, FocusColor = Vec(value.FocusColor), RectXExpression = Ptr(value.RectXExpression),
        RectYExpression = Ptr(value.RectYExpression), RectWExpression = Ptr(value.RectWExpression), RectHExpression = Ptr(value.RectHExpression),
        ItemsPointer = Ptr(value.ItemsPointer), ScaleTransitions = Transitions(value.ScaleTransitions), AlphaTransitions = Transitions(value.AlphaTransitions),
        XTransitions = Transitions(value.XTransitions), YTransitions = Transitions(value.YTransitions), ExpressionData = Ptr(value.ExpressionData)
    };

    private static WindowDef Window(WindowDef value) => new()
    {
        NamePointer = Ptr(value.NamePointer), Name = value.Name, Rect = Rect(value.Rect), RectClient = Rect(value.RectClient), GroupPointer = Ptr(value.GroupPointer), Group = value.Group,
        Style = value.Style, Border = value.Border, OwnerDraw = value.OwnerDraw, OwnerDrawFlags = value.OwnerDrawFlags, BorderSize = value.BorderSize,
        StaticFlags = value.StaticFlags, DynamicFlags = value.DynamicFlags.ToArray(), NextTime = value.NextTime, ForeColor = Vec(value.ForeColor),
        BackColor = Vec(value.BackColor), BorderColor = Vec(value.BorderColor), OutlineColor = Vec(value.OutlineColor), DisableColor = Vec(value.DisableColor), Background = Ptr(value.Background)
    };
    private static RectangleDef Rect(RectangleDef value) => new() { X = value.X, Y = value.Y, W = value.W, H = value.H, HorzAlign = value.HorzAlign, VertAlign = value.VertAlign, Pad12 = value.Pad12 };
    private static Vec4 Vec(Vec4 value) => new() { A = value.A, R = value.R, G = value.G, B = value.B };
    private static IReadOnlyList<MenuTransition> Transitions(IReadOnlyList<MenuTransition> values) => values.Select(value => new MenuTransition { TransitionType = value.TransitionType, TargetField = value.TargetField, StartTime = value.StartTime, StartValue = value.StartValue, EndValue = value.EndValue, Time = value.Time, EndTriggerType = value.EndTriggerType }).ToArray();
    private static XPointer<T> Ptr<T>(XPointer<T> value) => new(value.Raw, value.ResolutionMode);
    private static SymbolicXAssetReference? Reference(string? value) => value is null ? null : new(XAssetType.Material, value.StartsWith(",", StringComparison.Ordinal) ? value : $",{value}");
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
    private readonly MenuBuildData[] _menus;
    private readonly IReadOnlyList<IMenuBuildData> _menuView;
    internal MenuFileBuildData(string? name, IEnumerable<MenuBuildData> menus)
        : this(name, CloneMenus(menus, new MenuGraphClone()), takeOwnership: true)
    {
    }

    private MenuFileBuildData(string? name, MenuBuildData[] menus, bool takeOwnership)
    {
        if (!takeOwnership)
            throw new ArgumentException("Detached MenuFile build-data ownership must be explicit.", nameof(takeOwnership));
        Name = name;
        _menus = menus ?? throw new ArgumentNullException(nameof(menus));
        _menuView = Array.AsReadOnly(_menus.Select(value => (IMenuBuildData)value).ToArray());
    }

    public XAssetType AssetType => XAssetType.MenuFile;
    public string? Name { get; }
    public IReadOnlyList<IMenuBuildData> Menus => _menuView;
    internal MenuFileBuildData Copy() => Copy(new MenuGraphClone());
    internal MenuFileBuildData Copy(MenuGraphClone graph) =>
        new(Name, CloneMenus(_menus, graph), takeOwnership: true);

    internal static MenuFileBuildData FromLoaded(MenuFileAsset value, MenuGraphClone graph)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(graph);
        return new MenuFileBuildData(
            value.Name,
            value.Menus.Select(entry => entry.Menu is null
                    ? MenuBuildData.Unresolved()
                    : MenuBuildData.FromLoaded(entry.Menu, graph))
                .ToArray(),
            takeOwnership: true);
    }

    private static MenuBuildData[] CloneMenus(
        IEnumerable<MenuBuildData> menus,
        MenuGraphClone graph)
    {
        ArgumentNullException.ThrowIfNull(menus);
        ArgumentNullException.ThrowIfNull(graph);
        return menus.Select(value => value.Copy(graph)).ToArray();
    }
}

public sealed class MenuFileDraft
{
    private MenuFileBuildData _data;
    internal MenuFileDraft(MenuFileBuildData data) => _data = data.Copy();
    public MenuFileBuildData Data => _data.Copy();
    public void Replace(MenuFileBuildData value) { ArgumentNullException.ThrowIfNull(value); _data = value.Copy(); }
    internal MenuFileDraft Clone() => new(_data);
}

public sealed class MenuFileAuthoringAdapter : AssetAuthoringAdapter<MenuFileAuthoredSnapshot, MenuFileDraft, MenuFileBuildData>
{
    private static readonly MenuFileBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.MenuFile;
    public override MenuFileAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => MenuFileAuthoredSnapshot.Import(source);
    public override MenuFileDraft CreateDraft(MenuFileAuthoredSnapshot snapshot) => new(snapshot.Data);
    public override MenuFileDraft CloneDraft(MenuFileDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(MenuFileDraft draft) => Validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(MenuFileDraft left, MenuFileDraft right)
    {
        MenuFileBuildData a = left.Data, b = right.Data;
        return a.Name == b.Name && a.Menus.Count == b.Menus.Count &&
            a.Menus.Zip(b.Menus).All(pair =>
                MenuSemanticProjection.Serialize(pair.First.Definition) == MenuSemanticProjection.Serialize(pair.Second.Definition));
    }
    public override MenuFileBuildData ExportBuildData(MenuFileDraft draft) { MenuFileBuildData data = draft.Data; if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("MenuFile draft has validation errors and cannot produce build data."); return data; }
}

public sealed class MenuDraft
{
    private MenuBuildData _data;
    internal MenuDraft(MenuBuildData data) => _data = data.Copy();
    public MenuBuildData Data => _data.Copy();
    public void Replace(MenuBuildData value) { ArgumentNullException.ThrowIfNull(value); _data = value.Copy(); }
    internal MenuDraft Clone() => new(_data);
}

public sealed class MenuAuthoringAdapter : AssetAuthoringAdapter<MenuAuthoredSnapshot, MenuDraft, MenuBuildData>
{
    private static readonly MenuBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.Menu;
    public override MenuAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => MenuAuthoredSnapshot.Import(source);
    public override MenuDraft CreateDraft(MenuAuthoredSnapshot snapshot) => new(snapshot.Data);
    public override MenuDraft CloneDraft(MenuDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(MenuDraft draft) => Validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(MenuDraft left, MenuDraft right) => MenuSemanticProjection.Serialize(left.Data.Definition) == MenuSemanticProjection.Serialize(right.Data.Definition);
    public override MenuBuildData ExportBuildData(MenuDraft draft) { MenuBuildData data = draft.Data; if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("Menu draft has validation errors and cannot produce build data."); return data; }
}
