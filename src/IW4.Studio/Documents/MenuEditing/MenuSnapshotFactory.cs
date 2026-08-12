using IW4.Assets.Assets.Menu;
using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;
using IW4.Studio.Documents.MenuEditing.Debugging;

namespace IW4.Studio.Documents.MenuEditing;

internal static class MenuSnapshotFactory
{
    public static MenuEditorSnapshot Create(
        MenuDefAsset definition,
        MenuDocumentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (definition.Items.Count != identity.Items.Count)
            throw new InvalidDataException(
                "Menu editor item identity count does not match the detached item table.");

        var expressionCodec = new MenuBehaviorExpressionCodec(
            definition.ExpressionDataValue);
        var behaviorCodec = new MenuItemBehaviorCodec(expressionCodec);
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
                    CreateMissingItem(definition.ImageTrack),
                    MenuItemBehaviorBindings.Empty)
                : new MenuItemSnapshot(
                    itemIdentity.Id,
                    itemIdentity.WindowId,
                    true,
                    Item(item),
                    behaviorCodec.Import(item));
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
            expressionCodec.Support,
            MenuDebugProgramFactory.Create(definition, identity),
            true);
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
        Value = Copy(value.Value),
        // Menu behavior values are deeply immutable; preserving this identity
        // also retains imported Statement sharing for copy-on-write edits.
        Behavior = value.Behavior
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
        value.ItemFlags,
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
            DvarEnumItemDefData => value.DvarEnumName is { } dvarName
                ? new MenuDvarEnumPayloadValue(dvarName)
                : MenuNoItemPayloadValue.Instance,
            NewsTickerItemDefData => value.NewsTicker is { } ticker
                ? new MenuNewsTickerPayloadValue(
                    ticker.FeedId,
                    ticker.Speed,
                    ticker.Spacing,
                    ticker.X)
                : MenuNoItemPayloadValue.Instance,
            TextScrollItemDefData => value.TextScroll is not null
                ? MenuTextScrollPayloadValue.Instance
                : MenuNoItemPayloadValue.Instance,
            NoItemDefData => MenuNoItemPayloadValue.Instance,
            _ => throw new InvalidDataException(
                $"Unsupported Menu item-data union arm '{value.TypeData.Value.GetType().Name}'.")
        };

    private static MenuItemValue CreateMissingItem(int imageTrack) =>
        MenuItemDefaults.CreateValue(
            ItemDefType.Text,
            imageTrack,
            null);

    private static MenuRectangleValue Rectangle(RectangleDef value) => new(
        value.X,
        value.Y,
        value.W,
        value.H,
        value.HorzAlign,
        value.VertAlign);

    private static MenuColorValue Color(IW4.Assets.Math.Vec4 value) =>
        // The generic asset Vec4 names its four serialized slots A/R/G/B,
        // while Window colors use the engine's R/G/B/A slot semantics.
        new(value.B, value.A, value.R, value.G);

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
