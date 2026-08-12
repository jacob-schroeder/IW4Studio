using IW4.Assets.Assets.Menu;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.Studio.Documents;
using IW4.Studio.Documents.MenuEditing.Behavior;

namespace IW4.Studio.Documents.MenuEditing;

internal static partial class MenuDocumentCompiler
{
    private static MenuItemValue SnapshotItem(
        MenuDefAsset data,
        MenuDocumentIdentity identity,
        int index) =>
        MenuSnapshotFactory.Create(data, identity).Items[index].Value;

    private static IReadOnlyList<ItemDefReference> BuildItemsForImageTrack(
        MenuDefAsset source,
        MenuDocumentIdentity identity,
        int imageTrack)
    {
        MenuEditorSnapshot snapshot = MenuSnapshotFactory.Create(source, identity);
        var items = source.Items.ToArray();
        for (int index = 0; index < items.Length; index++)
        {
            ItemDefAsset? existing = source.Items[index].Item;
            if (existing is null)
                continue;

            ItemDefAsset item = BuildItem(
                existing,
                snapshot.Items[index].Value,
                rebuildPayload: false,
                imageTrack: imageTrack);
            ItemDefReference reference = items[index];
            items[index] = new ItemDefReference(
                reference.Index,
                reference.Pointer.Raw == 0
                    ? new XPointer<ItemDefAsset>(-1)
                    : reference.Pointer,
                item);
        }

        return Array.AsReadOnly(items);
    }

    private static MenuDefAsset ReplaceItem(
        MenuDefAsset source,
        int index,
        ItemDefAsset item)
    {
        var items = source.Items.ToArray();
        ItemDefReference reference = items[index];
        items[index] = new ItemDefReference(
            reference.Index,
            reference.Pointer.Raw == 0
                ? new XPointer<ItemDefAsset>(-1)
                : reference.Pointer,
            item);
        return BuildMenu(source, items: items);
    }

    private static MenuDefAsset BuildMenu(
        MenuDefAsset source,
        MenuSettingsValue? settings = null,
        WindowDef? window = null,
        IReadOnlyList<ItemDefReference>? items = null)
    {
        IReadOnlyList<ItemDefReference> effectiveItems = items ?? source.Items;
        return new MenuDefAsset
        {
            Window = window ?? source.Window,
            FontPointer = settings is null
                ? source.FontPointer
                : StringPointer(source.FontPointer, settings.Font),
            Font = settings is null ? source.Font : settings.Font,
            Fullscreen = settings?.Fullscreen ?? source.Fullscreen,
            ItemCount = effectiveItems.Count,
            FontIndex = settings?.FontIndex ?? source.FontIndex,
            CursorItems = settings is null
                ? source.CursorItems.ToArray()
                : settings.CursorItems.ToArray(),
            FadeCycle = settings?.FadeCycle ?? source.FadeCycle,
            FadeClamp = settings?.FadeClamp ?? source.FadeClamp,
            FadeAmount = settings?.FadeAmount ?? source.FadeAmount,
            FadeInAmount = settings?.FadeInAmount ?? source.FadeInAmount,
            BlurRadius = settings?.BlurRadius ?? source.BlurRadius,
            OnOpen = source.OnOpen,
            OnOpenSet = source.OnOpenSet,
            OnCloseRequest = source.OnCloseRequest,
            OnCloseRequestSet = source.OnCloseRequestSet,
            OnClose = source.OnClose,
            OnCloseSet = source.OnCloseSet,
            OnEsc = source.OnEsc,
            OnEscSet = source.OnEscSet,
            ExecKeys = source.ExecKeys,
            ExecKeyHandler = source.ExecKeyHandler,
            VisibleExpression = source.VisibleExpression,
            VisibleStatement = source.VisibleStatement,
            AllowedBinding = settings is null
                ? source.AllowedBinding
                : StringPointer(source.AllowedBinding, settings.AllowedBinding),
            AllowedBindingString = settings is null
                ? source.AllowedBindingString
                : settings.AllowedBinding,
            SoundName = settings is null
                ? source.SoundName
                : StringPointer(source.SoundName, settings.SoundName),
            SoundNameString = settings is null
                ? source.SoundNameString
                : settings.SoundName,
            ImageTrack = settings?.ImageTrack ?? source.ImageTrack,
            FocusColor = settings is null
                ? Copy(source.FocusColor)
                : Vec(settings.FocusColor),
            RectXExpression = source.RectXExpression,
            RectXStatement = source.RectXStatement,
            RectYExpression = source.RectYExpression,
            RectYStatement = source.RectYStatement,
            RectWExpression = source.RectWExpression,
            RectWStatement = source.RectWStatement,
            RectHExpression = source.RectHExpression,
            RectHStatement = source.RectHStatement,
            ItemsPointer = effectiveItems.Count == 0
                ? default
                : source.ItemsPointer.Raw == 0
                    ? new XPointer<XPointer<ItemDefAsset>[]>(-1)
                    : source.ItemsPointer,
            Items = effectiveItems.ToArray(),
            ScaleTransitions = settings is null
                ? Clone(source.ScaleTransitions)
                : Transitions(settings.ScaleTransitions),
            AlphaTransitions = settings is null
                ? Clone(source.AlphaTransitions)
                : Transitions(settings.AlphaTransitions),
            XTransitions = settings is null
                ? Clone(source.XTransitions)
                : Transitions(settings.XTransitions),
            YTransitions = settings is null
                ? Clone(source.YTransitions)
                : Transitions(settings.YTransitions),
            ExpressionData = source.ExpressionData,
            ExpressionDataValue = source.ExpressionDataValue
        };
    }

    private static WindowDef BuildWindow(
        WindowDef source,
        MenuWindowValue value) => new()
    {
        NamePointer = StringPointer(source.NamePointer, value.Name),
        Name = value.Name,
        Rect = Rect(source.Rect, value.Rect),
        RectClient = Rect(source.RectClient, value.RectClient),
        GroupPointer = StringPointer(source.GroupPointer, value.Group),
        Group = value.Group,
        Style = value.Style,
        Border = value.Border,
        OwnerDraw = value.OwnerDraw,
        OwnerDrawFlags = value.OwnerDrawFlags,
        BorderSize = value.BorderSize,
        StaticFlags = value.StaticFlags,
        DynamicFlags = value.DynamicFlags.ToArray(),
        NextTime = 0,
        ForeColor = Vec(value.ForeColor),
        BackColor = Vec(value.BackColor),
        BorderColor = Vec(value.BorderColor),
        OutlineColor = Vec(value.OutlineColor),
        DisableColor = Vec(value.DisableColor),
        Background = ReferencePointer(source.Background, value.BackgroundMaterialName),
        BackgroundMaterialName = LogicalReferenceName(value.BackgroundMaterialName)
    };

    private static ItemDefAsset BuildItem(
        ItemDefAsset? source,
        MenuItemValue value,
        bool rebuildPayload,
        int imageTrack,
        MenuItemBehaviorAssetBindings? behavior = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ItemPayloadBuildResult payload = rebuildPayload
            ? BuildPayload(source, value.Type, value.Payload)
            : PreservePayload(source);
        ListBoxDef? listBox = behavior is null
            ? payload.ListBox
            : WithDoubleClick(payload.ListBox, behavior.ListBoxDoubleClick);
        return new ItemDefAsset
        {
            Window = BuildWindow(source?.Window ?? new WindowDef(), value.Window),
            TextRect = value.TextRectangles
                .Select((rectangle, index) => Rect(
                    RectangleAt(source?.TextRect, index),
                    rectangle))
                .ToArray(),
            Type = value.Type,
            DataType = value.DataType,
            Align = value.Align,
            FontEnum = value.FontEnum,
            TextAlignMode = value.TextAlignMode,
            TextAlignX = value.TextAlignX,
            TextAlignY = value.TextAlignY,
            TextScale = value.TextScale,
            TextStyle = value.TextStyle,
            GameMsgWindowIndex = value.GameMessageWindowIndex,
            GameMsgWindowMode = value.GameMessageWindowMode,
            Text = StringPointer(source?.Text ?? default, value.Text),
            TextString = value.Text,
            ItemFlags = value.ItemFlags,
            RuntimeParentPointer = source?.RuntimeParentPointer ?? 0,
            MouseEnterText = behavior is null
                ? source?.MouseEnterText ?? default
                : behavior.MouseEnterText.Pointer,
            MouseEnterTextSet = behavior is null
                ? source?.MouseEnterTextSet
                : behavior.MouseEnterText.Handlers,
            MouseExitText = behavior is null
                ? source?.MouseExitText ?? default
                : behavior.MouseExitText.Pointer,
            MouseExitTextSet = behavior is null
                ? source?.MouseExitTextSet
                : behavior.MouseExitText.Handlers,
            MouseEnter = behavior is null
                ? source?.MouseEnter ?? default
                : behavior.MouseEnter.Pointer,
            MouseEnterSet = behavior is null
                ? source?.MouseEnterSet
                : behavior.MouseEnter.Handlers,
            MouseExit = behavior is null
                ? source?.MouseExit ?? default
                : behavior.MouseExit.Pointer,
            MouseExitSet = behavior is null
                ? source?.MouseExitSet
                : behavior.MouseExit.Handlers,
            Action = behavior is null
                ? source?.Action ?? default
                : behavior.Action.Pointer,
            ActionSet = behavior is null
                ? source?.ActionSet
                : behavior.Action.Handlers,
            Accept = behavior is null
                ? source?.Accept ?? default
                : behavior.Accept.Pointer,
            AcceptSet = behavior is null
                ? source?.AcceptSet
                : behavior.Accept.Handlers,
            OnFocus = behavior is null
                ? source?.OnFocus ?? default
                : behavior.OnFocus.Pointer,
            OnFocusSet = behavior is null
                ? source?.OnFocusSet
                : behavior.OnFocus.Handlers,
            LeaveFocus = behavior is null
                ? source?.LeaveFocus ?? default
                : behavior.LeaveFocus.Pointer,
            LeaveFocusSet = behavior is null
                ? source?.LeaveFocusSet
                : behavior.LeaveFocus.Handlers,
            Dvar = StringPointer(source?.Dvar ?? default, value.Dvar),
            DvarString = value.Dvar,
            DvarTest = StringPointer(source?.DvarTest ?? default, value.DvarTest),
            DvarTestString = value.DvarTest,
            OnKey = behavior is null
                ? source?.OnKey ?? default
                : behavior.OnKeyPointer,
            OnKeyHandler = behavior is null
                ? source?.OnKeyHandler
                : behavior.OnKeyHandler,
            EnableDvar = StringPointer(source?.EnableDvar ?? default, value.EnableDvar),
            EnableDvarString = value.EnableDvar,
            DvarFlags = value.DvarFlags,
            FocusSound = ReferencePointer(
                source?.FocusSound ?? default,
                value.FocusSoundName),
            FocusSoundName = LogicalReferenceName(value.FocusSoundName),
            Special = value.Special,
            CursorPos = value.CursorPositions.ToArray(),
            TypeData = payload.TypeData,
            EditField = payload.EditField,
            ListBox = listBox,
            Multi = payload.Multi,
            DvarEnumName = payload.DvarEnumName,
            NewsTicker = payload.NewsTicker,
            TextScroll = payload.TextScroll,
            ImageTrack = imageTrack,
            FloatExpressionCount = behavior is null
                ? source?.LoadedFloatExpressions.Count ?? 0
                : behavior.FloatExpressions.Count,
            FloatExpressions = behavior is null
                ? source?.FloatExpressions ?? default
                : behavior.FloatExpressionsPointer,
            LoadedFloatExpressions = behavior is null
                ? source?.LoadedFloatExpressions ?? []
                : behavior.FloatExpressions,
            VisibleExpression = behavior is null
                ? source?.VisibleExpression ?? default
                : behavior.Visible.Pointer,
            VisibleStatement = behavior is null
                ? source?.VisibleStatement
                : behavior.Visible.Statement,
            DisabledExpression = behavior is null
                ? source?.DisabledExpression ?? default
                : behavior.Disabled.Pointer,
            DisabledStatement = behavior is null
                ? source?.DisabledStatement
                : behavior.Disabled.Statement,
            TextExpression = behavior is null
                ? source?.TextExpression ?? default
                : behavior.Text.Pointer,
            TextStatement = behavior is null
                ? source?.TextStatement
                : behavior.Text.Statement,
            MaterialExpression = behavior is null
                ? source?.MaterialExpression ?? default
                : behavior.Material.Pointer,
            MaterialStatement = behavior is null
                ? source?.MaterialStatement
                : behavior.Material.Statement,
            GlowColor = Vec(value.GlowColor),
            DecayActive = value.DecayActive,
            DecayActivePad0 = source?.DecayActivePad0 ?? 0,
            DecayActivePad1 = source?.DecayActivePad1 ?? 0,
            DecayActivePad2 = source?.DecayActivePad2 ?? 0,
            // Birth time and last sound are runtime caches. The remaining
            // values are serialized text-FX configuration consumed by paint.
            FxBirthTime = 0,
            FxLetterTime = source?.FxLetterTime ?? 0,
            FxDecayStartTime = source?.FxDecayStartTime ?? 0,
            FxDecayDuration = source?.FxDecayDuration ?? 0,
            LastSoundPlayedTime = 0
        };
    }

    private static ListBoxDef? WithDoubleClick(
        ListBoxDef? source,
        MenuBehaviorNativeEventBinding doubleClick)
    {
        if (source is null)
            return null;

        return new ListBoxDef
        {
            StartPos = source.StartPos.ToArray(),
            EndPos = source.EndPos.ToArray(),
            DrawPadding = source.DrawPadding,
            ElementWidth = source.ElementWidth,
            ElementHeight = source.ElementHeight,
            ElementStyle = source.ElementStyle,
            NumColumns = source.NumColumns,
            ColumnInfo = source.ColumnInfo.Select(column => new ColumnInfo
            {
                Pos = column.Pos,
                Width = column.Width,
                MaxChars = column.MaxChars,
                Alignment = column.Alignment
            }).ToArray(),
            DoubleClick = doubleClick.Pointer,
            DoubleClickSet = doubleClick.Handlers,
            NotSelectable = source.NotSelectable,
            NoScrollbars = source.NoScrollbars,
            UsePaging = source.UsePaging,
            SelectBorder = Copy(source.SelectBorder),
            SelectIcon = source.SelectIcon,
            SelectIconMaterial = source.SelectIconMaterial,
            SelectIconMaterialName = source.SelectIconMaterialName
        };
    }
}
