using System.Runtime.CompilerServices;
using IW4.Assets.Assets.Menu;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents;

/// <summary>
/// Capture-time copier for the Menu graph.  It keeps authored source values,
/// strings, discriminants, and managed graph identities, while deliberately
/// dropping runtime pool/cache addresses.  The mutable references in this
/// group are all copied through identity maps so a nested handler/statement
/// remains shared in a detached draft without retaining a loaded object.
/// </summary>
internal sealed class MenuGraphClone
{
    // The table is process-local and thread-safe, while its weak keys avoid
    // extending the lifetime of captured or draft graphs. Every clone carries
    // its source token forward so a later shared clone context can re-intern
    // equivalent occurrences without conflating distinct value-equal nodes.
    private static readonly ConditionalWeakTable<object, ProvenanceToken> Provenance = new();
    private readonly bool _preserveSourceProvenance;
    private readonly Dictionary<object, ProvenanceToken>
        _independentProvenance = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<ProvenanceToken, MenuDefAsset> _menuDefinitions = [];
    private readonly Dictionary<ProvenanceToken, MenuEventHandlerSet> _eventSets = [];
    private readonly Dictionary<ProvenanceToken, MenuEventHandler> _eventHandlers = [];
    private readonly Dictionary<ProvenanceToken, Statement> _statements = [];
    private readonly Dictionary<ProvenanceToken, ExpressionSupportingData> _supportingData = [];
    private readonly Dictionary<ProvenanceToken, ItemKeyHandler> _keyHandlers = [];
    private readonly Dictionary<ProvenanceToken, ItemDefAsset> _items = [];
    private readonly Dictionary<ProvenanceToken, ConditionalScript> _conditionalScripts = [];
    private readonly Dictionary<ProvenanceToken, SetLocalVarData> _localVars = [];
    private readonly Dictionary<ProvenanceToken, StaticDvar> _staticDvars = [];
    private readonly Dictionary<ProvenanceToken, EditFieldDef> _editFields = [];
    private readonly Dictionary<ProvenanceToken, ListBoxDef> _listBoxes = [];
    private readonly Dictionary<ProvenanceToken, MultiDef> _multiDefs = [];
    private readonly Dictionary<ProvenanceToken, NewsTickerDef> _newsTickers = [];
    private readonly Dictionary<ProvenanceToken, TextScrollDef> _textScrolls = [];

    internal MenuGraphClone(bool preserveSourceProvenance = true)
    {
        _preserveSourceProvenance = preserveSourceProvenance;
    }

    public MenuDefAsset CloneMenu(MenuDefAsset value)
    {
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_menuDefinitions.TryGetValue(provenance, out MenuDefAsset? existing)) return existing;
        var clone = new MenuDefAsset
        {
            Window = CloneWindow(value.Window), FontPointer = Ptr(value.FontPointer), Font = value.Font,
            Fullscreen = value.Fullscreen, ItemCount = value.ItemCount, FontIndex = value.FontIndex,
            CursorItems = value.CursorItems.ToArray(), FadeCycle = value.FadeCycle, FadeClamp = value.FadeClamp,
            FadeAmount = value.FadeAmount, FadeInAmount = value.FadeInAmount, BlurRadius = value.BlurRadius,
            OnOpen = Ptr(value.OnOpen), OnCloseRequest = Ptr(value.OnCloseRequest), OnClose = Ptr(value.OnClose), OnEsc = Ptr(value.OnEsc),
            ExecKeys = Ptr(value.ExecKeys), VisibleExpression = Ptr(value.VisibleExpression), AllowedBinding = Ptr(value.AllowedBinding),
            AllowedBindingString = value.AllowedBindingString, SoundName = Ptr(value.SoundName), SoundNameString = value.SoundNameString,
            ImageTrack = value.ImageTrack, FocusColor = Vec(value.FocusColor), RectXExpression = Ptr(value.RectXExpression),
            RectYExpression = Ptr(value.RectYExpression), RectWExpression = Ptr(value.RectWExpression), RectHExpression = Ptr(value.RectHExpression),
            ItemsPointer = Ptr(value.ItemsPointer), ScaleTransitions = Transitions(value.ScaleTransitions), AlphaTransitions = Transitions(value.AlphaTransitions),
            XTransitions = Transitions(value.XTransitions), YTransitions = Transitions(value.YTransitions), ExpressionData = Ptr(value.ExpressionData)
        };
        PropagateProvenance(clone, provenance);
        _menuDefinitions.Add(provenance, clone);
        clone.ExpressionDataValue = CloneSupporting(value.ExpressionDataValue);
        clone.OnOpenSet = CloneEventSet(value.OnOpenSet);
        clone.OnCloseRequestSet = CloneEventSet(value.OnCloseRequestSet);
        clone.OnCloseSet = CloneEventSet(value.OnCloseSet);
        clone.OnEscSet = CloneEventSet(value.OnEscSet);
        clone.ExecKeyHandler = CloneKeyHandler(value.ExecKeyHandler);
        clone.VisibleStatement = CloneStatement(value.VisibleStatement);
        clone.RectXStatement = CloneStatement(value.RectXStatement);
        clone.RectYStatement = CloneStatement(value.RectYStatement);
        clone.RectWStatement = CloneStatement(value.RectWStatement);
        clone.RectHStatement = CloneStatement(value.RectHStatement);
        clone.Items = value.Items.Select(reference => new ItemDefReference(reference.Index, Ptr(reference.Pointer), CloneItem(reference.Item))).ToArray();
        return clone;
    }

    internal ItemDefAsset? CloneItem(ItemDefAsset? value)
    {
        if (value is null) return null;
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_items.TryGetValue(provenance, out ItemDefAsset? existing)) return existing;
        var clone = new ItemDefAsset
        {
            Window = CloneWindow(value.Window), TextRect = value.TextRect.Select(Rect).ToArray(), Type = value.Type, DataType = value.DataType,
            Align = value.Align, FontEnum = value.FontEnum, TextAlignMode = value.TextAlignMode, TextAlignX = value.TextAlignX,
            TextAlignY = value.TextAlignY, TextScale = value.TextScale, TextStyle = value.TextStyle, GameMsgWindowIndex = value.GameMsgWindowIndex,
            GameMsgWindowMode = value.GameMsgWindowMode, Text = Ptr(value.Text), TextString = value.TextString, TextSaveGameInfo = value.TextSaveGameInfo,
            // Preserve the serialized source value. DB_AddXAsset patches only
            // the runtime destination cell; this field remains linker input.
            RuntimeParentPointer = value.RuntimeParentPointer,
            MouseEnterText = Ptr(value.MouseEnterText), MouseExitText = Ptr(value.MouseExitText), MouseEnter = Ptr(value.MouseEnter), MouseExit = Ptr(value.MouseExit),
            Action = Ptr(value.Action), Accept = Ptr(value.Accept), OnFocus = Ptr(value.OnFocus), LeaveFocus = Ptr(value.LeaveFocus),
            Dvar = Ptr(value.Dvar), DvarString = value.DvarString, DvarTest = Ptr(value.DvarTest), DvarTestString = value.DvarTestString,
            OnKey = Ptr(value.OnKey), EnableDvar = Ptr(value.EnableDvar), EnableDvarString = value.EnableDvarString, DvarFlags = value.DvarFlags,
            FocusSound = Ptr(value.FocusSound), FocusSoundName = value.FocusSoundName ?? value.FocusSoundAsset?.AliasName, Special = value.Special,
            CursorPos = value.CursorPos.ToArray(), TypeData = CloneItemData(value.TypeData), ImageTrack = value.ImageTrack,
            FloatExpressionCount = value.FloatExpressionCount, FloatExpressions = Ptr(value.FloatExpressions), VisibleExpression = Ptr(value.VisibleExpression),
            DisabledExpression = Ptr(value.DisabledExpression), TextExpression = Ptr(value.TextExpression), MaterialExpression = Ptr(value.MaterialExpression),
            GlowColor = Vec(value.GlowColor), DecayActive = value.DecayActive, DecayActivePad0 = value.DecayActivePad0,
            DecayActivePad1 = value.DecayActivePad1, DecayActivePad2 = value.DecayActivePad2,
            // FX timing fields are post-load animation/sound cache state.
            FxBirthTime = 0, FxLetterTime = 0, FxDecayStartTime = 0, FxDecayDuration = 0, LastSoundPlayedTime = 0
        };
        PropagateProvenance(clone, provenance);
        _items.Add(provenance, clone);
        clone.MouseEnterTextSet = CloneEventSet(value.MouseEnterTextSet);
        clone.MouseExitTextSet = CloneEventSet(value.MouseExitTextSet);
        clone.MouseEnterSet = CloneEventSet(value.MouseEnterSet);
        clone.MouseExitSet = CloneEventSet(value.MouseExitSet);
        clone.ActionSet = CloneEventSet(value.ActionSet);
        clone.AcceptSet = CloneEventSet(value.AcceptSet);
        clone.OnFocusSet = CloneEventSet(value.OnFocusSet);
        clone.LeaveFocusSet = CloneEventSet(value.LeaveFocusSet);
        clone.OnKeyHandler = CloneKeyHandler(value.OnKeyHandler);
        clone.EditField = CloneEditField(value.EditField);
        clone.ListBox = CloneListBox(value.ListBox);
        clone.Multi = CloneMulti(value.Multi);
        clone.DvarEnumName = value.DvarEnumName;
        clone.NewsTicker = CloneNewsTicker(value.NewsTicker);
        clone.TextScroll = CloneTextScroll(value.TextScroll);
        clone.LoadedFloatExpressions = value.LoadedFloatExpressions.Select(CloneFloatExpression).ToArray();
        clone.VisibleStatement = CloneStatement(value.VisibleStatement);
        clone.DisabledStatement = CloneStatement(value.DisabledStatement);
        clone.TextStatement = CloneStatement(value.TextStatement);
        clone.MaterialStatement = CloneStatement(value.MaterialStatement);
        return clone;
    }

    private MenuEventHandlerSet? CloneEventSet(MenuEventHandlerSet? value)
    {
        if (value is null) return null;
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_eventSets.TryGetValue(provenance, out MenuEventHandlerSet? existing)) return existing;
        var clone = new MenuEventHandlerSet { EventHandlerCount = value.EventHandlerCount, EventHandlers = Ptr(value.EventHandlers) };
        PropagateProvenance(clone, provenance);
        _eventSets.Add(provenance, clone);
        clone.Handlers = value.Handlers.Select(reference => new MenuEventHandlerReference(reference.Index, Ptr(reference.Pointer), CloneEventHandler(reference.Handler))).ToArray();
        return clone;
    }

    private MenuEventHandler? CloneEventHandler(MenuEventHandler? value)
    {
        if (value is null) return null;
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_eventHandlers.TryGetValue(provenance, out MenuEventHandler? existing)) return existing;
        var clone = new MenuEventHandler { EventData = CloneEventData(value.EventData), EventType = value.EventType, Pad05 = value.Pad05, Pad06 = value.Pad06, Pad07 = value.Pad07 };
        PropagateProvenance(clone, provenance);
        _eventHandlers.Add(provenance, clone);
        clone.UnconditionalScript = value.UnconditionalScript;
        clone.ConditionalScript = CloneConditional(value.ConditionalScript);
        clone.ElseScriptSet = CloneEventSet(value.ElseScriptSet);
        clone.SetLocalVarData = CloneLocalVar(value.SetLocalVarData);
        return clone;
    }

    private EventData CloneEventData(EventData value) => new() { Value = value.Value switch
    {
        UnconditionalScriptEventData data => new UnconditionalScriptEventData { Script = Ptr(data.Script) },
        ConditionalScriptEventData data => new ConditionalScriptEventData { ConditionalScriptPointer = Ptr(data.ConditionalScriptPointer) },
        ElseScriptEventData data => new ElseScriptEventData { EventHandlerSetPointer = Ptr(data.EventHandlerSetPointer) },
        SetLocalVarEventData data => new SetLocalVarEventData { SetLocalVarDataPointer = Ptr(data.SetLocalVarDataPointer) },
        IgnoredEventData data => new IgnoredEventData { Reserved = data.Reserved },
        _ => throw new InvalidDataException($"Unsupported Menu event-data union arm '{value.Value.GetType().Name}'.")
    }};

    private ConditionalScript? CloneConditional(ConditionalScript? value)
    {
        if (value is null) return null;
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_conditionalScripts.TryGetValue(provenance, out ConditionalScript? existing)) return existing;
        var clone = new ConditionalScript { EventHandlerSet = Ptr(value.EventHandlerSet), EventExpression = Ptr(value.EventExpression) };
        PropagateProvenance(clone, provenance);
        _conditionalScripts.Add(provenance, clone);
        clone.EventHandlers = CloneEventSet(value.EventHandlers);
        clone.EventStatement = CloneStatement(value.EventStatement);
        return clone;
    }

    private SetLocalVarData? CloneLocalVar(SetLocalVarData? value)
    {
        if (value is null) return null;
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_localVars.TryGetValue(provenance, out SetLocalVarData? existing)) return existing;
        var clone = new SetLocalVarData { LocalVarName = Ptr(value.LocalVarName), LocalVarNameString = value.LocalVarNameString, Expression = Ptr(value.Expression) };
        PropagateProvenance(clone, provenance);
        _localVars.Add(provenance, clone);
        clone.ExpressionStatement = CloneStatement(value.ExpressionStatement);
        return clone;
    }

    private ItemKeyHandler? CloneKeyHandler(ItemKeyHandler? value)
    {
        if (value is null) return null;
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_keyHandlers.TryGetValue(provenance, out ItemKeyHandler? existing)) return existing;
        var clone = new ItemKeyHandler { Key = value.Key, Action = Ptr(value.Action), Next = Ptr(value.Next) };
        PropagateProvenance(clone, provenance);
        _keyHandlers.Add(provenance, clone);
        clone.ActionSet = CloneEventSet(value.ActionSet);
        clone.NextHandler = CloneKeyHandler(value.NextHandler);
        return clone;
    }

    private Statement? CloneStatement(Statement? value)
    {
        if (value is null) return null;
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_statements.TryGetValue(provenance, out Statement? existing)) return existing;
        var clone = new Statement
        {
            NumEntries = value.NumEntries, Entries = Ptr(value.Entries), SupportingData = Ptr(value.SupportingData),
            LastExecuteTime = 0, LastResult = new Operand { DataType = ExpDataType.VAL_INT, Value = new IntOperandValue(0) }
        };
        PropagateProvenance(clone, provenance);
        _statements.Add(provenance, clone);
        clone.LoadedEntries = value.LoadedEntries.Select(CloneExpressionEntry).ToArray();
        clone.SupportingDataValue = CloneSupporting(value.SupportingDataValue);
        return clone;
    }

    private ExpressionEntry CloneExpressionEntry(ExpressionEntry value)
    {
        var clone = new ExpressionEntry
        {
            Kind = value.Kind,
            OperationCode = value.OperationCode,
            OperatorTail = value.OperatorTail,
            Operand = value.Kind == ExpressionEntryKind.Operand ? CloneOperand(value.Operand) : new Operand(),
            StringValue = value.StringValue
        };
        clone.FunctionStatement = CloneStatement(value.FunctionStatement);
        return clone;
    }

    private Operand CloneOperand(Operand value) => new() { DataType = value.DataType, Value = value.Value switch
    {
        IntOperandValue integer => new IntOperandValue(integer.Value),
        FloatOperandValue number => new FloatOperandValue(number.Value, number.EncodedBits),
        StringOperandValue text => new StringOperandValue(Ptr(text.StringPointer)),
        FunctionOperandValue function => new FunctionOperandValue(Ptr(function.StatementPointer)),
        ReservedOperandValue reserved => new ReservedOperandValue(reserved.Reserved),
        _ => throw new InvalidDataException($"Unsupported Menu operand union arm '{value.Value.GetType().Name}'.")
    }};

    private ExpressionSupportingData? CloneSupporting(ExpressionSupportingData? value)
    {
        if (value is null) return null;
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_supportingData.TryGetValue(provenance, out ExpressionSupportingData? existing)) return existing;
        var clone = new ExpressionSupportingData
        {
            UiFunctions = new UIFunctionList { TotalFunctions = value.UiFunctions.TotalFunctions, Functions = Ptr(value.UiFunctions.Functions) },
            StaticDvarList = new StaticDvarList { NumStaticDvars = value.StaticDvarList.NumStaticDvars, StaticDvars = Ptr(value.StaticDvarList.StaticDvars) },
            UiStrings = new StringList { TotalStrings = value.UiStrings.TotalStrings, Strings = Ptr(value.UiStrings.Strings) }
        };
        PropagateProvenance(clone, provenance);
        _supportingData.Add(provenance, clone);
        clone.UiFunctions.LoadedFunctions = value.UiFunctions.LoadedFunctions.Select(reference => new StatementReference(reference.Index, Ptr(reference.Pointer), CloneStatement(reference.Statement))).ToArray();
        clone.StaticDvarList.LoadedStaticDvars = value.StaticDvarList.LoadedStaticDvars.Select(reference => new StaticDvarReference(reference.Index, Ptr(reference.Pointer), CloneStaticDvar(reference.StaticDvar))).ToArray();
        clone.UiStrings.LoadedStrings = value.UiStrings.LoadedStrings.Select(reference => new XStringReference(reference.Index, Ptr(reference.Pointer), reference.Value)).ToArray();
        return clone;
    }

    private StaticDvar? CloneStaticDvar(StaticDvar? value)
    {
        if (value is null) return null;
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_staticDvars.TryGetValue(provenance, out StaticDvar? existing)) return existing;
        var clone = new StaticDvar { Dvar = default, DvarName = Ptr(value.DvarName), DvarNameString = value.DvarNameString };
        PropagateProvenance(clone, provenance);
        _staticDvars.Add(provenance, clone);
        return clone;
    }

    private static WindowDef CloneWindow(WindowDef value) => new()
    {
        NamePointer = Ptr(value.NamePointer), Name = value.Name, Rect = Rect(value.Rect), RectClient = Rect(value.RectClient), GroupPointer = Ptr(value.GroupPointer), Group = value.Group,
        Style = value.Style, Border = value.Border, OwnerDraw = value.OwnerDraw, OwnerDrawFlags = value.OwnerDrawFlags, BorderSize = value.BorderSize,
        StaticFlags = value.StaticFlags, DynamicFlags = value.DynamicFlags.ToArray(), NextTime = 0, ForeColor = Vec(value.ForeColor),
        BackColor = Vec(value.BackColor), BorderColor = Vec(value.BorderColor), OutlineColor = Vec(value.OutlineColor), DisableColor = Vec(value.DisableColor),
        Background = Ptr(value.Background), BackgroundMaterialName = value.BackgroundMaterialName ?? value.BackgroundMaterial?.Info.Name
    };

    private static ItemDefData CloneItemData(ItemDefData value) => new() { Value = value.Value switch
    {
        EditFieldItemDefData data => new EditFieldItemDefData { EditFieldPointer = Ptr(data.EditFieldPointer) },
        ListBoxItemDefData data => new ListBoxItemDefData { ListBoxPointer = Ptr(data.ListBoxPointer) },
        MultiItemDefData data => new MultiItemDefData { MultiPointer = Ptr(data.MultiPointer) },
        DvarEnumItemDefData data => new DvarEnumItemDefData { DvarEnumNamePointer = Ptr(data.DvarEnumNamePointer) },
        NewsTickerItemDefData data => new NewsTickerItemDefData { NewsTickerPointer = Ptr(data.NewsTickerPointer) },
        TextScrollItemDefData data => new TextScrollItemDefData { TextScrollPointer = Ptr(data.TextScrollPointer) },
        NoItemDefData data => new NoItemDefData { Reserved = data.Reserved },
        _ => throw new InvalidDataException($"Unsupported Menu item-data union arm '{value.Value.GetType().Name}'.")
    }};

    private EditFieldDef? CloneEditField(EditFieldDef? value)
    {
        if (value is null) return null;
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_editFields.TryGetValue(provenance, out EditFieldDef? existing)) return existing;
        var clone = new EditFieldDef { MinVal = value.MinVal, MaxVal = value.MaxVal, DefVal = value.DefVal, Range = value.Range, MaxChars = value.MaxChars, MaxCharsGotoNext = value.MaxCharsGotoNext, MaxPaintChars = value.MaxPaintChars, PaintOffset = value.PaintOffset };
        PropagateProvenance(clone, provenance);
        _editFields.Add(provenance, clone);
        return clone;
    }

    private ListBoxDef? CloneListBox(ListBoxDef? value)
    {
        if (value is null) return null;
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_listBoxes.TryGetValue(provenance, out ListBoxDef? existing)) return existing;
        var clone = new ListBoxDef
        {
            StartPos = Enumerable.Repeat(0, value.StartPos.Count).ToArray(), EndPos = Enumerable.Repeat(0, value.EndPos.Count).ToArray(), DrawPadding = value.DrawPadding,
            ElementWidth = value.ElementWidth, ElementHeight = value.ElementHeight, ElementStyle = value.ElementStyle, NumColumns = value.NumColumns,
            ColumnInfo = value.ColumnInfo.Select(column => new ColumnInfo { Pos = column.Pos, Width = column.Width, MaxChars = column.MaxChars, Alignment = column.Alignment }).ToArray(),
            DoubleClick = Ptr(value.DoubleClick), NotSelectable = value.NotSelectable, NoScrollbars = value.NoScrollbars, UsePaging = value.UsePaging,
            SelectBorder = Vec(value.SelectBorder), SelectIcon = Ptr(value.SelectIcon), SelectIconMaterialName = value.SelectIconMaterialName ?? value.SelectIconMaterial?.Info.Name,
        };
        PropagateProvenance(clone, provenance);
        _listBoxes.Add(provenance, clone);
        clone.DoubleClickSet = CloneEventSet(value.DoubleClickSet);
        return clone;
    }

    private MultiDef? CloneMulti(MultiDef? value)
    {
        if (value is null) return null;
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_multiDefs.TryGetValue(provenance, out MultiDef? existing)) return existing;
        var clone = new MultiDef { DvarList = value.DvarList.Select(Ptr).ToArray(), DvarListStrings = value.DvarListStrings.ToArray(), DvarStr = value.DvarStr.Select(Ptr).ToArray(), DvarStrStrings = value.DvarStrStrings.ToArray(), DvarValue = value.DvarValue.ToArray(), Count = value.Count, StrDef = value.StrDef };
        PropagateProvenance(clone, provenance);
        _multiDefs.Add(provenance, clone);
        return clone;
    }

    private NewsTickerDef? CloneNewsTicker(NewsTickerDef? value)
    {
        if (value is null) return null;
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_newsTickers.TryGetValue(provenance, out NewsTickerDef? existing)) return existing;
        var clone = new NewsTickerDef { FeedId = value.FeedId, Speed = value.Speed, Spacing = value.Spacing, LastTime = 0, Start = 0, End = 0, X = value.X };
        PropagateProvenance(clone, provenance);
        _newsTickers.Add(provenance, clone);
        return clone;
    }

    private TextScrollDef? CloneTextScroll(TextScrollDef? value)
    {
        if (value is null) return null;
        ProvenanceToken provenance = CloneProvenanceOf(value);
        if (_textScrolls.TryGetValue(provenance, out TextScrollDef? existing)) return existing;
        var clone = new TextScrollDef { StartTime = 0 };
        PropagateProvenance(clone, provenance);
        _textScrolls.Add(provenance, clone);
        return clone;
    }

    private static ProvenanceToken ProvenanceOf(object value) =>
        Provenance.GetValue(value, static _ => new ProvenanceToken());

    private ProvenanceToken CloneProvenanceOf(object value)
    {
        if (_preserveSourceProvenance)
            return ProvenanceOf(value);
        if (_independentProvenance.TryGetValue(
                value,
                out ProvenanceToken? provenance))
        {
            return provenance;
        }

        provenance = new ProvenanceToken();
        _independentProvenance.Add(value, provenance);
        return provenance;
    }

    private static void PropagateProvenance(
        object clone,
        ProvenanceToken provenance) =>
        Provenance.Add(clone, provenance);

    private sealed class ProvenanceToken
    {
    }

    private ItemFloatExpression CloneFloatExpression(ItemFloatExpression value) => new() { Target = value.Target, Expression = Ptr(value.Expression), Statement = CloneStatement(value.Statement) };

    private static RectangleDef Rect(RectangleDef value) => new() { X = value.X, Y = value.Y, W = value.W, H = value.H, HorzAlign = value.HorzAlign, VertAlign = value.VertAlign, Pad12 = value.Pad12 };
    private static Vec4 Vec(Vec4 value) => new() { A = value.A, R = value.R, G = value.G, B = value.B };
    private static IReadOnlyList<MenuTransition> Transitions(IReadOnlyList<MenuTransition> values) => values.Select(value => new MenuTransition { TransitionType = value.TransitionType, TargetField = value.TargetField, StartTime = value.StartTime, StartValue = value.StartValue, EndValue = value.EndValue, Time = value.Time, EndTriggerType = value.EndTriggerType }).ToArray();
    private static XPointer<T> Ptr<T>(XPointer<T> value) => new(value.Raw, value.ResolutionMode);
}
