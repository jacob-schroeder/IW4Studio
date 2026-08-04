using IW4.Assets.Assets.Menu;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

internal static partial class MenuDocumentCompiler
{
    private static MenuItemValue SnapshotItem(
        MenuBuildData data,
        MenuDocumentIdentity identity,
        int index) =>
        MenuSnapshotFactory.Create(data, identity).Items[index].Value;

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
        bool rebuildPayload)
    {
        ArgumentNullException.ThrowIfNull(value);
        ItemPayloadBuildResult payload = rebuildPayload
            ? BuildPayload(source, value.Payload)
            : PreservePayload(source);
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
            TextSaveGameInfo = value.TextSaveGameInfo,
            RuntimeParentPointer = source?.RuntimeParentPointer ?? 0,
            MouseEnterText = source?.MouseEnterText ?? default,
            MouseEnterTextSet = source?.MouseEnterTextSet,
            MouseExitText = source?.MouseExitText ?? default,
            MouseExitTextSet = source?.MouseExitTextSet,
            MouseEnter = source?.MouseEnter ?? default,
            MouseEnterSet = source?.MouseEnterSet,
            MouseExit = source?.MouseExit ?? default,
            MouseExitSet = source?.MouseExitSet,
            Action = source?.Action ?? default,
            ActionSet = source?.ActionSet,
            Accept = source?.Accept ?? default,
            AcceptSet = source?.AcceptSet,
            OnFocus = source?.OnFocus ?? default,
            OnFocusSet = source?.OnFocusSet,
            LeaveFocus = source?.LeaveFocus ?? default,
            LeaveFocusSet = source?.LeaveFocusSet,
            Dvar = StringPointer(source?.Dvar ?? default, value.Dvar),
            DvarString = value.Dvar,
            DvarTest = StringPointer(source?.DvarTest ?? default, value.DvarTest),
            DvarTestString = value.DvarTest,
            OnKey = source?.OnKey ?? default,
            OnKeyHandler = source?.OnKeyHandler,
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
            ListBox = payload.ListBox,
            Multi = payload.Multi,
            DvarEnumName = payload.DvarEnumName,
            NewsTicker = payload.NewsTicker,
            TextScroll = payload.TextScroll,
            ImageTrack = value.ImageTrack,
            FloatExpressionCount = source?.LoadedFloatExpressions.Count ?? 0,
            FloatExpressions = source?.FloatExpressions ?? default,
            LoadedFloatExpressions = source?.LoadedFloatExpressions ?? [],
            VisibleExpression = source?.VisibleExpression ?? default,
            VisibleStatement = source?.VisibleStatement,
            DisabledExpression = source?.DisabledExpression ?? default,
            DisabledStatement = source?.DisabledStatement,
            TextExpression = source?.TextExpression ?? default,
            TextStatement = source?.TextStatement,
            MaterialExpression = source?.MaterialExpression ?? default,
            MaterialStatement = source?.MaterialStatement,
            GlowColor = Vec(value.GlowColor),
            DecayActive = value.DecayActive,
            DecayActivePad0 = source?.DecayActivePad0 ?? 0,
            DecayActivePad1 = source?.DecayActivePad1 ?? 0,
            DecayActivePad2 = source?.DecayActivePad2 ?? 0,
            FxBirthTime = 0,
            FxLetterTime = 0,
            FxDecayStartTime = 0,
            FxDecayDuration = 0,
            LastSoundPlayedTime = 0
        };
    }
}
