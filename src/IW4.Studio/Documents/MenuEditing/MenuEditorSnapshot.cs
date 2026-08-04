using IW4.Assets.Assets.Menu;
using IW4.Assets.Assets;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>Immutable view of one selectable root Window.</summary>
public sealed record MenuWindowSnapshot(MenuNodeId Id, MenuWindowValue Value);

/// <summary>Immutable view of one ItemDef occurrence and its Window.</summary>
public sealed record MenuItemSnapshot(
    MenuNodeId Id,
    MenuNodeId WindowId,
    bool IsResolved,
    MenuItemValue Value);

/// <summary>
/// Detached, immutable Menu view consumed by Desktop and preview projection.
/// It does not expose the mutable serialized graph retained by the draft.
/// </summary>
public sealed class MenuEditorSnapshot
{
    private readonly IReadOnlyList<MenuItemSnapshot> _items;

    internal MenuEditorSnapshot(
        MenuNodeId id,
        MenuSettingsValue settings,
        MenuWindowSnapshot window,
        IEnumerable<MenuItemSnapshot> items,
        MenuBehaviorSummary behavior,
        bool isComplete)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(behavior);

        Id = id;
        Settings = MenuSnapshotFactory.Copy(settings);
        Window = new MenuWindowSnapshot(window.Id, MenuSnapshotFactory.Copy(window.Value));
        _items = Array.AsReadOnly(items.Select(MenuSnapshotFactory.Copy).ToArray());
        Behavior = behavior;
        IsComplete = isComplete;
    }

    public MenuNodeId Id { get; }
    public string? Name => Window.Value.Name;
    public bool IsComplete { get; }
    public MenuSettingsValue Settings { get; }
    public MenuWindowSnapshot Window { get; }
    public IReadOnlyList<MenuItemSnapshot> Items => _items;
    public MenuBehaviorSummary Behavior { get; }
}

public sealed record MenuFileRegistrationSnapshot(
    MenuRegistrationId Id,
    int Index,
    bool IsEditableDefinition,
    string? Name,
    MenuEditorSnapshot? Menu);

/// <summary>Immutable ordered MenuFile registration view.</summary>
public sealed class MenuFileEditorSnapshot
{
    private readonly IReadOnlyList<MenuFileRegistrationSnapshot> _registrations;

    internal MenuFileEditorSnapshot(
        string? name,
        IEnumerable<MenuFileRegistrationSnapshot> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        Name = name;
        _registrations = Array.AsReadOnly(registrations.ToArray());
    }

    public string? Name { get; }
    public IReadOnlyList<MenuFileRegistrationSnapshot> Registrations => _registrations;
}

/// <summary>
/// Detached read-only Menu provider capture for dependency/reference tabs.
/// </summary>
public sealed class MenuReadOnlySnapshot
{
    private MenuReadOnlySnapshot(MenuEditorSnapshot menu) => Menu = menu;

    public MenuEditorSnapshot Menu { get; }

    public static MenuReadOnlySnapshot CaptureResolvedProvider(
        AssetEditorSession editorSession)
    {
        MenuDefAsset menu = ResolvedMenuProvider.Capture<MenuDefAsset>(
            editorSession,
            XAssetType.Menu,
            "Menu");
        MenuBuildData detached = MenuBuildData.FromLoaded(menu);
        return new MenuReadOnlySnapshot(
            MenuSnapshotFactory.Create(detached, MenuDocumentIdentity.Create(detached)));
    }
}

/// <summary>
/// Detached read-only MenuFile provider capture for dependency/reference tabs.
/// </summary>
public sealed class MenuFileReadOnlySnapshot
{
    private MenuFileReadOnlySnapshot(MenuFileEditorSnapshot menuFile) =>
        MenuFile = menuFile;

    public MenuFileEditorSnapshot MenuFile { get; }

    public static MenuFileReadOnlySnapshot CaptureResolvedProvider(
        AssetEditorSession editorSession)
    {
        MenuFileAsset menuFile = ResolvedMenuProvider.Capture<MenuFileAsset>(
            editorSession,
            XAssetType.MenuFile,
            "MenuFile");
        MenuFileBuildData detached = MenuFileBuildData.FromLoaded(
            menuFile,
            new MenuGraphClone());
        var identity = MenuFileDocumentIdentity.Create(detached);
        return new MenuFileReadOnlySnapshot(
            MenuSnapshotFactory.Create(detached, identity));
    }
}

internal sealed record MenuItemIdentity(MenuNodeId Id, MenuNodeId WindowId);

internal sealed class MenuDocumentIdentity
{
    private readonly MenuItemIdentity[] _items;

    private MenuDocumentIdentity(
        MenuNodeId id,
        MenuNodeId windowId,
        IEnumerable<MenuItemIdentity> items)
    {
        Id = id;
        WindowId = windowId;
        _items = items.ToArray();
    }

    public MenuNodeId Id { get; }
    public MenuNodeId WindowId { get; }
    public IReadOnlyList<MenuItemIdentity> Items => _items;

    public static MenuDocumentIdentity Create(MenuBuildData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new MenuDocumentIdentity(
            MenuNodeId.New(),
            MenuNodeId.New(),
            Enumerable.Range(0, data.Definition.Items.Count)
                .Select(_ => new MenuItemIdentity(MenuNodeId.New(), MenuNodeId.New())));
    }

    public MenuDocumentIdentity Clone() =>
        new(Id, WindowId, _items);

    public MenuDocumentIdentity WithItems(IEnumerable<MenuItemIdentity> items) =>
        new(Id, WindowId, items);
}

internal sealed record MenuFileRegistrationIdentity(
    MenuRegistrationId Id,
    MenuDocumentIdentity? MenuIdentity);

internal sealed class MenuFileDocumentIdentity
{
    private readonly MenuFileRegistrationIdentity[] _registrations;

    private MenuFileDocumentIdentity(
        IEnumerable<MenuFileRegistrationIdentity> registrations) =>
        _registrations = registrations.ToArray();

    public IReadOnlyList<MenuFileRegistrationIdentity> Registrations =>
        _registrations;

    public static MenuFileDocumentIdentity Create(MenuFileBuildData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new MenuFileDocumentIdentity(
            data.GetRegistrationDefinitions()
                .Select(menu => new MenuFileRegistrationIdentity(
                    MenuRegistrationId.New(),
                    menu is null ? null : MenuDocumentIdentity.Create(menu))));
    }

    public MenuFileDocumentIdentity Clone() =>
        new(_registrations.Select(value => value with
        {
            MenuIdentity = value.MenuIdentity?.Clone()
        }));

    public MenuFileDocumentIdentity WithRegistrations(
        IEnumerable<MenuFileRegistrationIdentity> registrations) =>
        new(registrations);
}

internal static class ResolvedMenuProvider
{
    public static TAsset Capture<TAsset>(
        AssetEditorSession editorSession,
        XAssetType expectedType,
        string displayName)
        where TAsset : BaseAsset
    {
        ArgumentNullException.ThrowIfNull(editorSession);
        WorkspaceAssetResolvedProvider provider = editorSession.Entry.ResolvedProvider
            ?? throw new InvalidDataException(
                $"{displayName} read-only viewing requires a catalog-resolved full-definition provider.");
        XAssetProviderContribution contribution = editorSession.Workspace.Runtime.AssetPool.Slots
            .SelectMany(slot => slot.Providers)
            .SingleOrDefault(candidate => candidate.Id == provider.ProviderId)
            ?? throw new InvalidDataException(
                $"The catalog-resolved {displayName} provider is no longer present in this workspace runtime.");
        if (contribution.AssetType != expectedType ||
            contribution.IsReferencePlaceholder ||
            contribution.Owner != provider.Zone.Handle ||
            contribution.Asset is not TAsset asset)
        {
            throw new InvalidDataException(
                $"The catalog-resolved provider no longer matches a readable {displayName} full definition.");
        }

        return asset;
    }
}

internal static class MenuSnapshotFactory
{
    public static MenuEditorSnapshot Create(
        MenuBuildData data,
        MenuDocumentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(identity);
        MenuDefAsset definition = data.Definition;
        if (definition.Items.Count != identity.Items.Count)
            throw new InvalidDataException(
                "Menu editor item identity count does not match the detached item table.");

        var items = new MenuItemSnapshot[definition.Items.Count];
        for (int index = 0; index < items.Length; index++)
        {
            ItemDefAsset? item = definition.Items[index].Item;
            MenuItemIdentity itemIdentity = identity.Items[index];
            items[index] = item is null
                ? new MenuItemSnapshot(
                    itemIdentity.Id,
                    itemIdentity.WindowId,
                    false,
                    CreateMissingItem())
                : new MenuItemSnapshot(
                    itemIdentity.Id,
                    itemIdentity.WindowId,
                    true,
                    Item(item));
        }

        return new MenuEditorSnapshot(
            identity.Id,
            Settings(definition),
            new MenuWindowSnapshot(identity.WindowId, Window(definition.Window)),
            items,
            new MenuBehaviorSummary(
                definition.OnOpenSet is not null,
                definition.OnCloseRequestSet is not null,
                definition.OnCloseSet is not null,
                definition.OnEscSet is not null,
                definition.ExecKeyHandler is not null,
                definition.VisibleStatement is not null,
                definition.RectXStatement is not null,
                definition.RectYStatement is not null,
                definition.RectWStatement is not null,
                definition.RectHStatement is not null,
                definition.ExpressionDataValue is not null),
            data.IsComplete);
    }

    public static MenuFileEditorSnapshot Create(
        MenuFileBuildData data,
        MenuFileDocumentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(identity);
        IReadOnlyList<MenuBuildData?> menus = data.GetRegistrationDefinitions();
        if (menus.Count != identity.Registrations.Count)
            throw new InvalidDataException(
                "MenuFile editor registration identity count does not match its detached registration table.");

        return new MenuFileEditorSnapshot(
            data.Name,
            menus.Select((menu, index) =>
            {
                MenuFileRegistrationIdentity registration = identity.Registrations[index];
                MenuEditorSnapshot? snapshot = menu is null || registration.MenuIdentity is null
                    ? null
                    : Create(menu, registration.MenuIdentity);
                return new MenuFileRegistrationSnapshot(
                    registration.Id,
                    index,
                    menu is not null,
                    menu?.Definition.Window.Name ?? LogicalReferenceName(
                        data.MenuLinks[index].Reference.OriginalSerializedName),
                    snapshot);
            }));
    }

    public static MenuWindowValue Copy(MenuWindowValue value) => value with
    {
        DynamicFlags = ReadOnly(value.DynamicFlags)
    };

    public static MenuSettingsValue Copy(MenuSettingsValue value) => value with
    {
        CursorItems = ReadOnly(value.CursorItems),
        ScaleTransitions = ReadOnly(value.ScaleTransitions),
        AlphaTransitions = ReadOnly(value.AlphaTransitions),
        XTransitions = ReadOnly(value.XTransitions),
        YTransitions = ReadOnly(value.YTransitions)
    };

    public static MenuItemSnapshot Copy(MenuItemSnapshot value) => value with
    {
        Value = Copy(value.Value)
    };

    public static MenuItemValue Copy(MenuItemValue value) => value with
    {
        Window = Copy(value.Window),
        TextRectangles = ReadOnly(value.TextRectangles),
        CursorPositions = ReadOnly(value.CursorPositions),
        Payload = Copy(value.Payload)
    };

    public static MenuItemPayloadValue Copy(MenuItemPayloadValue value) =>
        value switch
        {
            MenuNoItemPayloadValue => MenuNoItemPayloadValue.Instance,
            MenuEditFieldPayloadValue edit => edit,
            MenuListBoxPayloadValue list => list with
            {
                Columns = ReadOnly(list.Columns)
            },
            MenuMultiPayloadValue multi => multi with
            {
                Entries = ReadOnly(multi.Entries)
            },
            MenuDvarEnumPayloadValue dvar => dvar,
            MenuNewsTickerPayloadValue ticker => ticker,
            MenuTextScrollPayloadValue => MenuTextScrollPayloadValue.Instance,
            _ => throw new InvalidDataException(
                $"Unsupported Menu editor payload '{value.GetType().Name}'.")
        };

    private static MenuSettingsValue Settings(MenuDefAsset value) => new(
        value.Font,
        value.Fullscreen,
        value.FontIndex,
        ReadOnly(value.CursorItems),
        value.FadeCycle,
        value.FadeClamp,
        value.FadeAmount,
        value.FadeInAmount,
        value.BlurRadius,
        value.AllowedBindingString,
        value.SoundNameString,
        value.ImageTrack,
        Color(value.FocusColor),
        ReadOnly(value.ScaleTransitions.Select(Transition)),
        ReadOnly(value.AlphaTransitions.Select(Transition)),
        ReadOnly(value.XTransitions.Select(Transition)),
        ReadOnly(value.YTransitions.Select(Transition)));

    private static MenuWindowValue Window(WindowDef value) => new(
        value.Name,
        Rectangle(value.Rect),
        Rectangle(value.RectClient),
        value.Group,
        value.Style,
        value.Border,
        value.OwnerDraw,
        value.OwnerDrawFlags,
        value.BorderSize,
        value.StaticFlags,
        ReadOnly(value.DynamicFlags),
        Color(value.ForeColor),
        Color(value.BackColor),
        Color(value.BorderColor),
        Color(value.OutlineColor),
        Color(value.DisableColor),
        LogicalReferenceName(value.BackgroundMaterialName));

    private static MenuItemValue Item(ItemDefAsset value) => new(
        Window(value.Window),
        ReadOnly(value.TextRect.Select(Rectangle)),
        value.Type,
        value.DataType,
        value.Align,
        value.FontEnum,
        value.TextAlignMode,
        value.TextAlignX,
        value.TextAlignY,
        value.TextScale,
        value.TextStyle,
        value.GameMsgWindowIndex,
        value.GameMsgWindowMode,
        value.TextString,
        value.TextSaveGameInfo,
        value.DvarString,
        value.DvarTestString,
        value.EnableDvarString,
        value.DvarFlags,
        LogicalReferenceName(value.FocusSoundName),
        value.Special,
        ReadOnly(value.CursorPos),
        value.ImageTrack,
        Color(value.GlowColor),
        value.DecayActive,
        Payload(value),
        new MenuItemBehaviorSummary(
            value.MouseEnterTextSet is not null,
            value.MouseExitTextSet is not null,
            value.MouseEnterSet is not null,
            value.MouseExitSet is not null,
            value.ActionSet is not null,
            value.AcceptSet is not null,
            value.OnFocusSet is not null,
            value.LeaveFocusSet is not null,
            value.OnKeyHandler is not null,
            value.VisibleStatement is not null,
            value.DisabledStatement is not null,
            value.TextStatement is not null,
            value.MaterialStatement is not null,
            value.LoadedFloatExpressions.Count));

    private static MenuItemPayloadValue Payload(ItemDefAsset value) =>
        value.TypeData.Value switch
        {
            EditFieldItemDefData => value.EditField is { } edit
                ? new MenuEditFieldPayloadValue(
                    edit.MinVal,
                    edit.MaxVal,
                    edit.DefVal,
                    edit.Range,
                    edit.MaxChars,
                    edit.MaxCharsGotoNext,
                    edit.MaxPaintChars,
                    edit.PaintOffset)
                : MenuNoItemPayloadValue.Instance,
            ListBoxItemDefData => value.ListBox is { } list
                ? new MenuListBoxPayloadValue(
                    list.DrawPadding,
                    list.ElementWidth,
                    list.ElementHeight,
                    list.ElementStyle,
                    list.NumColumns,
                    ReadOnly(list.ColumnInfo.Select(column =>
                        new MenuListBoxColumnValue(
                            column.Pos,
                            column.Width,
                            column.MaxChars,
                            column.Alignment))),
                    list.NotSelectable != 0,
                    list.NoScrollbars != 0,
                    list.UsePaging,
                    Color(list.SelectBorder),
                    LogicalReferenceName(list.SelectIconMaterialName),
                    list.DoubleClickSet is not null)
                : MenuNoItemPayloadValue.Instance,
            MultiItemDefData => value.Multi is { } multi
                ? new MenuMultiPayloadValue(
                    multi.Count,
                    multi.StrDef,
                    ReadOnly(Enumerable.Range(0, MultiDef.EntryCapacity)
                        .Select(index => new MenuMultiEntryValue(
                            ValueAt(multi.DvarListStrings, index),
                            ValueAt(multi.DvarStrStrings, index),
                            ValueAt(multi.DvarValue, index)))))
                : MenuNoItemPayloadValue.Instance,
            DvarEnumItemDefData => new MenuDvarEnumPayloadValue(value.DvarEnumName),
            NewsTickerItemDefData => value.NewsTicker is { } ticker
                ? new MenuNewsTickerPayloadValue(
                    ticker.FeedId,
                    ticker.Speed,
                    ticker.Spacing,
                    ticker.X)
                : MenuNoItemPayloadValue.Instance,
            TextScrollItemDefData => MenuTextScrollPayloadValue.Instance,
            NoItemDefData => MenuNoItemPayloadValue.Instance,
            _ => throw new InvalidDataException(
                $"Unsupported Menu item-data union arm '{value.TypeData.Value.GetType().Name}'.")
        };

    private static MenuItemValue CreateMissingItem() =>
        MenuItemDefaults.CreateValue(ItemDefType.Text, null);

    private static MenuRectangleValue Rectangle(RectangleDef value) => new(
        value.X,
        value.Y,
        value.W,
        value.H,
        value.HorzAlign,
        value.VertAlign);

    private static MenuColorValue Color(IW4.Assets.Math.Vec4 value) =>
        new(value.A, value.R, value.G, value.B);

    private static MenuTransitionValue Transition(MenuTransition value) => new(
        value.TransitionType,
        value.TargetField,
        value.StartTime,
        value.StartValue,
        value.EndValue,
        value.Time,
        value.EndTriggerType);

    private static string? LogicalReferenceName(string? value) =>
        string.IsNullOrEmpty(value) ? value : value.TrimStart(',');

    private static T? ValueAt<T>(IReadOnlyList<T> values, int index) =>
        index < values.Count ? values[index] : default;

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());
}
