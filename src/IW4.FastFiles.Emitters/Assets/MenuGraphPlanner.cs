using IW4.Assets.Assets.Menu;
using IW4.Assets.Assets.Sound;
using IW4.Assets.Math;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>
/// Planner for the recursive UI payload graph.  It treats every managed
/// child identity as a source object: the first reference consumes its body
/// inline, while repeated references are encoded as a packed destination
/// address.  This mirrors the compact DB stream convention and never emits a
/// source segment for a pointer that has no payload.
/// </summary>
internal sealed class MenuGraphPlanner
{
    private readonly EmissionPlan _plan;
    private readonly List<EmissionBlockSegment> _all;
    private readonly Dictionary<object, Node> _nodes = new(ReferenceEqualityComparer.Instance);

    public MenuGraphPlanner(EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _all = all ?? throw new ArgumentNullException(nameof(all));
    }

    public MenuPlan PlanMenu(MenuDefAsset value)
    {
        PlannedNode planned = PlanMenuNode(value);
        return new MenuPlan(
            planned.Inline ? planned.Node.Root! : null,
            planned.Inline ? planned.Node.Source! : [],
            planned.Inline ? -1 : planned.Node.Address.ToPackedPointer());
    }

    private PlannedNode PlanMenuNode(MenuDefAsset value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        _plan.Push(XFileBlockType.TEMP);
        try
        {
            EmissionAddress address = _plan.Allocate(MenuDefAsset.SerializedSize, 4);
            Node node = Begin(value, address);
            _plan.Push(XFileBlockType.LARGE);
            try
            {
                var children = new List<EmissionBlockSegment>();
                PlannedNode? supporting = Plan(value.ExpressionDataValue, PlanSupportingDataNode); Add(children, supporting);
                WindowPlan window = PlanWindowChildren(
                    value.Window,
                    address);
                children.AddRange(window.Source);
                StringPlan font = PlanString(value.Font); Add(children, font);
                PlannedNode? onOpen = Plan(value.OnOpenSet, PlanEventSetNode); Add(children, onOpen);
                PlannedNode? onClose = Plan(value.OnCloseSet, PlanEventSetNode); Add(children, onClose);
                PlannedNode? onCloseRequest = Plan(value.OnCloseRequestSet, PlanEventSetNode); Add(children, onCloseRequest);
                PlannedNode? onEsc = Plan(value.OnEscSet, PlanEventSetNode); Add(children, onEsc);
                PlannedNode? keys = Plan(value.ExecKeyHandler, PlanKeyHandlerNode); Add(children, keys);
                PlannedNode? visible = Plan(value.VisibleStatement, PlanStatementNode); Add(children, visible);
                StringPlan allowedBinding = PlanString(value.AllowedBindingString); Add(children, allowedBinding);
                StringPlan soundName = PlanString(value.SoundNameString); Add(children, soundName);
                PlannedNode? rectX = Plan(value.RectXStatement, PlanStatementNode); Add(children, rectX);
                PlannedNode? rectY = Plan(value.RectYStatement, PlanStatementNode); Add(children, rectY);
                PlannedNode? rectW = Plan(value.RectWStatement, PlanStatementNode); Add(children, rectW);
                PlannedNode? rectH = Plan(value.RectHStatement, PlanStatementNode); Add(children, rectH);
                PointerTablePlan items = PlanItems(value.Items); children.AddRange(items.Source);

                var writer = new XSourceWriter();
                WriteWindow(writer, value.Window, window);
                writer.WriteInt32(Pointer(font)); writer.WriteInt32(value.Fullscreen); writer.WriteInt32(value.ItemCount); writer.WriteInt32(value.FontIndex);
                WriteInts(writer, value.CursorItems, 4, "Menu.cursorItems"); writer.WriteInt32(value.FadeCycle); writer.WriteSingle(value.FadeClamp); writer.WriteSingle(value.FadeAmount); writer.WriteSingle(value.FadeInAmount); writer.WriteSingle(value.BlurRadius);
                writer.WriteInt32(Pointer(onOpen)); writer.WriteInt32(Pointer(onCloseRequest)); writer.WriteInt32(Pointer(onClose)); writer.WriteInt32(Pointer(onEsc)); writer.WriteInt32(Pointer(keys)); writer.WriteInt32(Pointer(visible));
                writer.WriteInt32(Pointer(allowedBinding)); writer.WriteInt32(Pointer(soundName)); writer.WriteInt32(value.ImageTrack); WriteVec4(writer, value.FocusColor);
                writer.WriteInt32(Pointer(rectX)); writer.WriteInt32(Pointer(rectY)); writer.WriteInt32(Pointer(rectW)); writer.WriteInt32(Pointer(rectH)); writer.WriteInt32(Pointer(items.Table));
                WriteTransitions(writer, value.ScaleTransitions); WriteTransitions(writer, value.AlphaTransitions); WriteTransitions(writer, value.XTransitions); WriteTransitions(writer, value.YTransitions); writer.WriteInt32(Pointer(supporting));
                Exact(writer, MenuDefAsset.SerializedSize, "MenuDef");
                Complete(node, writer.ToArray(), children);
                return new PlannedNode(node, true);
            }
            finally
            {
                _plan.Pop(XFileBlockType.LARGE);
            }
        }
        finally
        {
            _plan.Pop(XFileBlockType.TEMP);
        }
    }

    private PlannedNode PlanItemNode(ItemDefAsset value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        Node node = Begin(value, _plan.Allocate(ItemDefAsset.SerializedSize, 4));
        var children = new List<EmissionBlockSegment>();
        WindowPlan window = PlanWindowChildren(value.Window, node.Address); children.AddRange(window.Source);
        StringPlan text = PlanString(value.TextString); Add(children, text);
        PlannedNode? mouseEnterText = Plan(value.MouseEnterTextSet, PlanEventSetNode); Add(children, mouseEnterText);
        PlannedNode? mouseExitText = Plan(value.MouseExitTextSet, PlanEventSetNode); Add(children, mouseExitText);
        PlannedNode? mouseEnter = Plan(value.MouseEnterSet, PlanEventSetNode); Add(children, mouseEnter);
        PlannedNode? mouseExit = Plan(value.MouseExitSet, PlanEventSetNode); Add(children, mouseExit);
        PlannedNode? action = Plan(value.ActionSet, PlanEventSetNode); Add(children, action);
        PlannedNode? accept = Plan(value.AcceptSet, PlanEventSetNode); Add(children, accept);
        PlannedNode? onFocus = Plan(value.OnFocusSet, PlanEventSetNode); Add(children, onFocus);
        PlannedNode? leaveFocus = Plan(value.LeaveFocusSet, PlanEventSetNode); Add(children, leaveFocus);
        StringPlan dvar = PlanString(value.DvarString); Add(children, dvar);
        StringPlan dvarTest = PlanString(value.DvarTestString); Add(children, dvarTest);
        PlannedNode? onKey = Plan(value.OnKeyHandler, PlanKeyHandlerNode); Add(children, onKey);
        StringPlan enableDvar = PlanString(value.EnableDvarString); Add(children, enableDvar);
        ExternalPlan? focusSound = PlanExternal(
            value.FocusSoundName,
            XAssetType.Sound,
            SoundAliasListAsset.SerializedSize,
            Offset(node.Address, 0x16c)); Add(children, focusSound);
        ItemDataPlan typeData = PlanItemData(value); children.AddRange(typeData.Source);
        PointerTablePlan floats = PlanFloatExpressions(
            value.LoadedFloatExpressions,
            value.FloatExpressions.Raw != 0); children.AddRange(floats.Source);
        PlannedNode? visible = Plan(value.VisibleStatement, PlanStatementNode); Add(children, visible);
        PlannedNode? disabled = Plan(value.DisabledStatement, PlanStatementNode); Add(children, disabled);
        PlannedNode? textStatement = Plan(value.TextStatement, PlanStatementNode); Add(children, textStatement);
        PlannedNode? material = Plan(value.MaterialStatement, PlanStatementNode); Add(children, material);

        var writer = new XSourceWriter();
        WriteWindow(writer, value.Window, window); foreach (RectangleDef rectangle in value.TextRect) WriteRectangle(writer, rectangle);
        writer.WriteInt32((int)value.Type); writer.WriteInt32(value.DataType); writer.WriteInt32((int)value.Align); writer.WriteInt32((int)value.FontEnum); writer.WriteInt32(value.TextAlignMode); writer.WriteSingle(value.TextAlignX); writer.WriteSingle(value.TextAlignY); writer.WriteSingle(value.TextScale); writer.WriteInt32((int)value.TextStyle); writer.WriteInt32(value.GameMsgWindowIndex); writer.WriteInt32(value.GameMsgWindowMode);
        writer.WriteInt32(Pointer(text)); writer.WriteInt32((int)value.ItemFlags); writer.WriteInt32(value.RuntimeParentPointer);
        writer.WriteInt32(Pointer(mouseEnterText)); writer.WriteInt32(Pointer(mouseExitText)); writer.WriteInt32(Pointer(mouseEnter)); writer.WriteInt32(Pointer(mouseExit)); writer.WriteInt32(Pointer(action)); writer.WriteInt32(Pointer(accept)); writer.WriteInt32(Pointer(onFocus)); writer.WriteInt32(Pointer(leaveFocus));
        writer.WriteInt32(Pointer(dvar)); writer.WriteInt32(Pointer(dvarTest)); writer.WriteInt32(Pointer(onKey)); writer.WriteInt32(Pointer(enableDvar)); writer.WriteInt32((int)value.DvarFlags); writer.WriteInt32(Pointer(focusSound)); writer.WriteSingle(value.Special); WriteInts(writer, value.CursorPos, 4, "ItemDef.cursorPos");
        writer.WriteInt32(typeData.Pointer); writer.WriteInt32(value.ImageTrack); writer.WriteInt32(value.FloatExpressionCount); writer.WriteInt32(Pointer(floats)); writer.WriteInt32(Pointer(visible)); writer.WriteInt32(Pointer(disabled)); writer.WriteInt32(Pointer(textStatement)); writer.WriteInt32(Pointer(material)); WriteVec4(writer, value.GlowColor);
        writer.WriteByte(value.DecayActive); writer.WriteByte(value.DecayActivePad0); writer.WriteByte(value.DecayActivePad1); writer.WriteByte(value.DecayActivePad2); writer.WriteInt32(value.FxBirthTime); writer.WriteInt32(value.FxLetterTime); writer.WriteInt32(value.FxDecayStartTime); writer.WriteInt32(value.FxDecayDuration); writer.WriteInt32(value.LastSoundPlayedTime);
        Exact(writer, ItemDefAsset.SerializedSize, "ItemDef");
        Complete(node, writer.ToArray(), children);
        return new PlannedNode(node, true);
    }

    private PlannedNode PlanEventSetNode(MenuEventHandlerSet value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        Node node = Begin(value, _plan.Allocate(MenuEventHandlerSet.SerializedSize, 4));
        PointerTablePlan handlers = PlanEventHandlers(value.Handlers);
        var writer = new XSourceWriter(); writer.WriteInt32(value.EventHandlerCount); writer.WriteInt32(Pointer(handlers.Table)); Exact(writer, MenuEventHandlerSet.SerializedSize, "MenuEventHandlerSet");
        Complete(node, writer.ToArray(), handlers.Source);
        return new PlannedNode(node, true);
    }

    private PlannedNode PlanEventHandlerNode(MenuEventHandler value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        Node node = Begin(value, _plan.Allocate(MenuEventHandler.SerializedSize, 4));
        var children = new List<EmissionBlockSegment>();
        int dataPointer = value.EventType switch
        {
            MenuEventHandlerType.UnconditionalScript => Pointer(PlanString(value.UnconditionalScript, children)),
            MenuEventHandlerType.ConditionalScript => Pointer(Plan(value.ConditionalScript, PlanConditionalNode, children)),
            MenuEventHandlerType.ElseScript => Pointer(Plan(value.ElseScriptSet, PlanEventSetNode, children)),
            MenuEventHandlerType.SetLocalVarBool or MenuEventHandlerType.SetLocalVarInt or MenuEventHandlerType.SetLocalVarFloat or MenuEventHandlerType.SetLocalVarString => Pointer(Plan(value.SetLocalVarData, PlanLocalVarNode, children)),
            _ => value.EventData.Value is IgnoredEventData ignored ? ignored.Reserved : throw new InvalidDataException($"Unsupported Menu event type '{value.EventType}'.")
        };
        var writer = new XSourceWriter(); writer.WriteInt32(dataPointer); writer.WriteByte((byte)value.EventType); writer.WriteByte(value.Pad05); writer.WriteByte(value.Pad06); writer.WriteByte(value.Pad07); Exact(writer, MenuEventHandler.SerializedSize, "MenuEventHandler");
        Complete(node, writer.ToArray(), children);
        return new PlannedNode(node, true);
    }

    private PlannedNode PlanConditionalNode(ConditionalScript value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        Node node = Begin(value, _plan.Allocate(ConditionalScript.SerializedSize, 4));
        var children = new List<EmissionBlockSegment>();
        PlannedNode? statement = Plan(value.EventStatement, PlanStatementNode); Add(children, statement);
        PlannedNode? events = Plan(value.EventHandlers, PlanEventSetNode); Add(children, events);
        var writer = new XSourceWriter(); writer.WriteInt32(Pointer(events)); writer.WriteInt32(Pointer(statement)); Exact(writer, ConditionalScript.SerializedSize, "ConditionalScript");
        Complete(node, writer.ToArray(), children);
        return new PlannedNode(node, true);
    }

    private PlannedNode PlanLocalVarNode(SetLocalVarData value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        Node node = Begin(value, _plan.Allocate(SetLocalVarData.SerializedSize, 4));
        var children = new List<EmissionBlockSegment>(); StringPlan name = PlanString(value.LocalVarNameString); Add(children, name); PlannedNode? expression = Plan(value.ExpressionStatement, PlanStatementNode); Add(children, expression);
        var writer = new XSourceWriter(); writer.WriteInt32(Pointer(name)); writer.WriteInt32(Pointer(expression)); Exact(writer, SetLocalVarData.SerializedSize, "SetLocalVarData");
        Complete(node, writer.ToArray(), children); return new PlannedNode(node, true);
    }

    private PlannedNode PlanKeyHandlerNode(ItemKeyHandler value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        Node node = Begin(value, _plan.Allocate(ItemKeyHandler.SerializedSize, 4));
        var children = new List<EmissionBlockSegment>(); PlannedNode? action = Plan(value.ActionSet, PlanEventSetNode); Add(children, action); PlannedNode? next = Plan(value.NextHandler, PlanKeyHandlerNode); Add(children, next);
        var writer = new XSourceWriter(); writer.WriteInt32(value.Key); writer.WriteInt32(Pointer(action)); writer.WriteInt32(Pointer(next)); Exact(writer, ItemKeyHandler.SerializedSize, "ItemKeyHandler"); Complete(node, writer.ToArray(), children); return new PlannedNode(node, true);
    }

    private PlannedNode PlanStatementNode(Statement value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        Node node = Begin(value, _plan.Allocate(Statement.SerializedSize, 4));
        PointerTablePlan entries = PlanExpressionEntries(value.LoadedEntries);
        var children = new List<EmissionBlockSegment>(entries.Source); PlannedNode? supporting = Plan(value.SupportingDataValue, PlanSupportingDataNode); Add(children, supporting);
        var writer = new XSourceWriter(); writer.WriteInt32(value.NumEntries); writer.WriteInt32(Pointer(entries.Table)); writer.WriteInt32(Pointer(supporting)); writer.WriteInt32(value.LastExecuteTime); WriteOperand(writer, value.LastResult, 0); Exact(writer, Statement.SerializedSize, "Statement"); Complete(node, writer.ToArray(), children); return new PlannedNode(node, true);
    }

    private PlannedNode PlanSupportingDataNode(ExpressionSupportingData value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        Node node = Begin(value, _plan.Allocate(ExpressionSupportingData.SerializedSize, 4));
        PointerTablePlan functions = PlanStatementReferences(value.UiFunctions.LoadedFunctions);
        PointerTablePlan dvars = PlanStaticDvars(value.StaticDvarList.LoadedStaticDvars);
        PointerTablePlan strings = PlanStringReferences(value.UiStrings.LoadedStrings);
        var writer = new XSourceWriter(); writer.WriteInt32(value.UiFunctions.TotalFunctions); writer.WriteInt32(Pointer(functions.Table)); writer.WriteInt32(value.StaticDvarList.NumStaticDvars); writer.WriteInt32(Pointer(dvars.Table)); writer.WriteInt32(value.UiStrings.TotalStrings); writer.WriteInt32(Pointer(strings.Table)); Exact(writer, ExpressionSupportingData.SerializedSize, "ExpressionSupportingData");
        Complete(node, writer.ToArray(), [.. functions.Source, .. dvars.Source, .. strings.Source]); return new PlannedNode(node, true);
    }

    // The remaining item-data, pointer-table, and primitive writers are
    // below.  They are intentionally centralized so every union arm shares
    // the same inline-vs-packed graph rule.

    private PointerTablePlan PlanItems(IReadOnlyList<ItemDefReference> values)
    {
        if (values.Count == 0) return new PointerTablePlan(null, []);
        EmissionAddress address = _plan.Allocate(checked(values.Count * sizeof(int)), 4);
        var pointers = new int[values.Count]; var children = new List<EmissionBlockSegment>();
        for (int index = 0; index < values.Count; index++) { PlannedNode? item = Plan(values[index].Item, PlanItemNode); pointers[index] = Pointer(item); Add(children, item); }
        var writer = new XSourceWriter(); foreach (int pointer in pointers) writer.WriteInt32(pointer); EmissionBlockSegment table = new(address, writer.ToArray()); _all.Add(table);
        return new PointerTablePlan(table, [table, .. children]);
    }

    private PointerTablePlan PlanEventHandlers(IReadOnlyList<MenuEventHandlerReference> values)
    {
        if (values.Count == 0) return new PointerTablePlan(null, []);
        EmissionAddress address = _plan.Allocate(checked(values.Count * sizeof(int)), 4);
        var pointers = new int[values.Count]; var children = new List<EmissionBlockSegment>();
        for (int index = 0; index < values.Count; index++) { PlannedNode? handler = Plan(values[index].Handler, PlanEventHandlerNode); pointers[index] = Pointer(handler); Add(children, handler); }
        var writer = new XSourceWriter(); foreach (int pointer in pointers) writer.WriteInt32(pointer); EmissionBlockSegment table = new(address, writer.ToArray()); _all.Add(table);
        return new PointerTablePlan(table, [table, .. children]);
    }

    private PointerTablePlan PlanExpressionEntries(IReadOnlyList<ExpressionEntry> values)
    {
        if (values.Count == 0) return new PointerTablePlan(null, []);
        EmissionAddress address = _plan.Allocate(checked(values.Count * ExpressionEntry.SerializedSize), 4);
        var children = new List<EmissionBlockSegment>(); var entryWriter = new XSourceWriter();
        foreach (ExpressionEntry entry in values)
        {
            entryWriter.WriteInt32((int)entry.Kind);
            if (entry.Kind == ExpressionEntryKind.Operator)
            {
                entryWriter.WriteInt32(entry.OperationCode);
                entryWriter.WriteInt32(entry.OperatorTail);
            }
            else
            {
                int encoded = PlanOperand(entry.Operand, entry.StringValue, entry.FunctionStatement, children);
                entryWriter.WriteInt32((int)entry.Operand.DataType);
                entryWriter.WriteInt32(encoded);
            }
        }
        Exact(entryWriter, checked(values.Count * ExpressionEntry.SerializedSize), "ExpressionEntry[]");
        EmissionBlockSegment table = new(address, entryWriter.ToArray()); _all.Add(table);
        return new PointerTablePlan(table, [table, .. children]);
    }

    private PointerTablePlan PlanStatementReferences(IReadOnlyList<StatementReference> values)
    {
        if (values.Count == 0) return new PointerTablePlan(null, []);
        EmissionAddress address = _plan.Allocate(checked(values.Count * sizeof(int)), 4); var children = new List<EmissionBlockSegment>(); var pointers = new int[values.Count];
        for (int index = 0; index < values.Count; index++) { PlannedNode? statement = Plan(values[index].Statement, PlanStatementNode); pointers[index] = Pointer(statement); Add(children, statement); }
        var writer = new XSourceWriter(); foreach (int pointer in pointers) writer.WriteInt32(pointer); EmissionBlockSegment table = new(address, writer.ToArray()); _all.Add(table); return new PointerTablePlan(table, [table, .. children]);
    }

    private PointerTablePlan PlanStaticDvars(IReadOnlyList<StaticDvarReference> values)
    {
        if (values.Count == 0) return new PointerTablePlan(null, []);
        EmissionAddress address = _plan.Allocate(checked(values.Count * sizeof(int)), 4); var children = new List<EmissionBlockSegment>(); var pointers = new int[values.Count];
        for (int index = 0; index < values.Count; index++) { PlannedNode? dvar = Plan(values[index].StaticDvar, PlanStaticDvarNode); pointers[index] = Pointer(dvar); Add(children, dvar); }
        var writer = new XSourceWriter(); foreach (int pointer in pointers) writer.WriteInt32(pointer); EmissionBlockSegment table = new(address, writer.ToArray()); _all.Add(table); return new PointerTablePlan(table, [table, .. children]);
    }

    private PointerTablePlan PlanStringReferences(IReadOnlyList<XStringReference> values)
    {
        if (values.Count == 0) return new PointerTablePlan(null, []);
        EmissionAddress address = _plan.Allocate(checked(values.Count * sizeof(int)), 4); var children = new List<EmissionBlockSegment>(); var pointers = new int[values.Count];
        for (int index = 0; index < values.Count; index++) { StringPlan text = PlanString(values[index].Value); pointers[index] = Pointer(text); Add(children, text); }
        var writer = new XSourceWriter(); foreach (int pointer in pointers) writer.WriteInt32(pointer); EmissionBlockSegment table = new(address, writer.ToArray()); _all.Add(table); return new PointerTablePlan(table, [table, .. children]);
    }

    private PlannedNode PlanStaticDvarNode(StaticDvar value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        Node node = Begin(value, _plan.Allocate(StaticDvar.SerializedSize, 4)); StringPlan name = PlanString(value.DvarNameString);
        var writer = new XSourceWriter(); writer.WriteInt32(0); writer.WriteInt32(Pointer(name)); Exact(writer, StaticDvar.SerializedSize, "StaticDvar"); Complete(node, writer.ToArray(), name.Inline ? name.Source : []); return new PlannedNode(node, true);
    }

    private PointerTablePlan PlanFloatExpressions(
        IReadOnlyList<ItemFloatExpression> values,
        bool preserveInlinePresence)
    {
        if (values.Count == 0)
        {
            if (preserveInlinePresence)
                _plan.Align(4);
            return new PointerTablePlan(null, [], preserveInlinePresence);
        }
        EmissionAddress address = _plan.Allocate(checked(values.Count * ItemFloatExpression.SerializedSize), 4); var children = new List<EmissionBlockSegment>(); var writer = new XSourceWriter();
        foreach (ItemFloatExpression value in values) { PlannedNode? statement = Plan(value.Statement, PlanStatementNode); writer.WriteInt32((int)value.Target); writer.WriteInt32(Pointer(statement)); Add(children, statement); }
        Exact(writer, checked(values.Count * ItemFloatExpression.SerializedSize), "ItemFloatExpression[]"); EmissionBlockSegment table = new(address, writer.ToArray()); _all.Add(table); return new PointerTablePlan(table, [table, .. children]);
    }

    private ItemDataPlan PlanItemData(ItemDefAsset item)
    {
        return item.TypeData.Value switch
        {
            EditFieldItemDefData => Plan(item.EditField, PlanEditFieldNode, EditFieldDef.SerializedSize),
            ListBoxItemDefData => Plan(item.ListBox, PlanListBoxNode, ListBoxDef.SerializedSize),
            MultiItemDefData => Plan(item.Multi, PlanMultiNode, MultiDef.SerializedSize),
            DvarEnumItemDefData => PlanDvarEnum(item.DvarEnumName),
            NewsTickerItemDefData => Plan(item.NewsTicker, PlanNewsTickerNode, NewsTickerDef.SerializedSize),
            TextScrollItemDefData => Plan(item.TextScroll, PlanTextScrollNode, TextScrollDef.SerializedSize),
            NoItemDefData none => new ItemDataPlan(none.Reserved, []),
            _ => throw new InvalidDataException($"Unsupported Menu item-data union arm '{item.TypeData.Value.GetType().Name}'.")
        };
    }

    private ItemDataPlan Plan<T>(T? value, Func<T, PlannedNode> planner, int _) where T : class
    {
        PlannedNode? node = Plan(value, planner); return new ItemDataPlan(Pointer(node), node is { Inline: true } plan ? plan.Node.Source! : []);
    }

    private ItemDataPlan PlanDvarEnum(string? value)
    {
        StringPlan name = PlanString(value);
        return new ItemDataPlan(Pointer(name), name.Inline ? name.Source : []);
    }

    private PlannedNode PlanEditFieldNode(EditFieldDef value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        Node node = Begin(value, _plan.Allocate(EditFieldDef.SerializedSize, 4)); var writer = new XSourceWriter(); writer.WriteSingle(value.MinVal); writer.WriteSingle(value.MaxVal); writer.WriteSingle(value.DefVal); writer.WriteSingle(value.Range); writer.WriteInt32(value.MaxChars); writer.WriteInt32(value.MaxCharsGotoNext); writer.WriteInt32(value.MaxPaintChars); writer.WriteInt32(value.PaintOffset); Exact(writer, EditFieldDef.SerializedSize, "EditFieldDef"); Complete(node, writer.ToArray(), []); return new PlannedNode(node, true);
    }

    private PlannedNode PlanListBoxNode(ListBoxDef value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        Node node = Begin(value, _plan.Allocate(ListBoxDef.SerializedSize, 4)); var children = new List<EmissionBlockSegment>(); PlannedNode? doubleClick = Plan(value.DoubleClickSet, PlanEventSetNode); Add(children, doubleClick); ExternalPlan? icon = PlanExternal(value.SelectIconMaterialName, XAssetType.Material, 0xa8, Offset(node.Address, 0x154)); Add(children, icon);
        var writer = new XSourceWriter(); WriteInts(writer, value.StartPos, 4, "ListBox.startPos"); WriteInts(writer, value.EndPos, 4, "ListBox.endPos"); writer.WriteInt32(value.DrawPadding); writer.WriteSingle(value.ElementWidth); writer.WriteSingle(value.ElementHeight); writer.WriteInt32(value.ElementStyle); writer.WriteInt32(value.NumColumns); WriteColumns(writer, value.ColumnInfo); writer.WriteInt32(Pointer(doubleClick)); writer.WriteInt32(value.NotSelectable); writer.WriteInt32(value.NoScrollbars); writer.WriteInt32(value.UsePaging); WriteVec4(writer, value.SelectBorder); writer.WriteInt32(Pointer(icon)); Exact(writer, ListBoxDef.SerializedSize, "ListBoxDef"); Complete(node, writer.ToArray(), children); return new PlannedNode(node, true);
    }

    private PlannedNode PlanMultiNode(MultiDef value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        Node node = Begin(value, _plan.Allocate(MultiDef.SerializedSize, 4)); var children = new List<EmissionBlockSegment>(); var list = value.DvarListStrings.Select(PlanString).ToArray(); var str = value.DvarStrStrings.Select(PlanString).ToArray(); foreach (StringPlan text in list) Add(children, text); foreach (StringPlan text in str) Add(children, text);
        var writer = new XSourceWriter(); foreach (StringPlan text in list) writer.WriteInt32(Pointer(text)); foreach (StringPlan text in str) writer.WriteInt32(Pointer(text)); foreach (float number in value.DvarValue) writer.WriteSingle(number); writer.WriteInt32(value.Count); writer.WriteInt32(value.StrDef); Exact(writer, MultiDef.SerializedSize, "MultiDef"); Complete(node, writer.ToArray(), children); return new PlannedNode(node, true);
    }

    private PlannedNode PlanNewsTickerNode(NewsTickerDef value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        Node node = Begin(value, _plan.Allocate(NewsTickerDef.SerializedSize, 4)); var writer = new XSourceWriter(); writer.WriteInt32(value.FeedId); writer.WriteInt32(value.Speed); writer.WriteInt32(value.Spacing); writer.WriteInt32(value.LastTime); writer.WriteInt32(value.Start); writer.WriteInt32(value.End); writer.WriteSingle(value.X); Exact(writer, NewsTickerDef.SerializedSize, "NewsTickerDef"); Complete(node, writer.ToArray(), []); return new PlannedNode(node, true);
    }

    private PlannedNode PlanTextScrollNode(TextScrollDef value)
    {
        if (TryExisting(value, out PlannedNode existing)) return existing;
        Node node = Begin(value, _plan.Allocate(TextScrollDef.SerializedSize, 4)); var writer = new XSourceWriter(); writer.WriteInt32(value.StartTime); Exact(writer, TextScrollDef.SerializedSize, "TextScrollDef"); Complete(node, writer.ToArray(), []); return new PlannedNode(node, true);
    }

    private WindowPlan PlanWindowChildren(
        WindowDef value,
        EmissionAddress ownerAddress)
    {
        StringPlan name = PlanString(value.Name);
        StringPlan group = PlanString(value.Group);
        ExternalPlan? background = PlanExternal(
            value.BackgroundMaterialName,
            XAssetType.Material,
            0xa8,
            Offset(ownerAddress, 0xac));
        var source = new List<EmissionBlockSegment>();
        Add(source, name);
        Add(source, group);
        Add(source, background);
        return new WindowPlan(name, group, background, source);
    }

    private StringPlan PlanString(string? value)
    {
        int before = _all.Count; PlannedString? stringPlan = AssetBodyEmitterHelpers.PlanString(value, _plan, _all, _plan.StringAliases); EmissionBlockSegment[] source = _all.Skip(before).ToArray();
        return new StringPlan(stringPlan, source, stringPlan is { } planned && !planned.IsExistingMaterialization);
    }

    private StringPlan PlanString(string? value, List<EmissionBlockSegment> destination)
    {
        StringPlan result = PlanString(value); Add(destination, result); return result;
    }

    private ExternalPlan? PlanExternal(
        string? name,
        XAssetType type,
        int rootSize,
        EmissionAddress ownerCell)
    {
        if (name is null) return null;
        string serialized = name.StartsWith(",", StringComparison.Ordinal) ? name : $",{name}";
        if (!AssetBodyEmitterHelpers.IsLatin1CString(serialized)) throw new InvalidDataException($"Menu external {type} name is not a Latin-1 C string.");
        string aliasKey = AssetBodyEmitterHelpers.XAssetAliasKey(
            type,
            serialized);
        if (_plan.PersistentXAssetAliasCells.TryGetValue(aliasKey, out EmissionAddress existingCell))
            return new ExternalPlan(null, [], existingCell.ToPackedPointer());
        _plan.Push(XFileBlockType.TEMP); EmissionAddress rootAddress = _plan.Allocate(rootSize, 4); _plan.Push(XFileBlockType.LARGE); StringPlan stringPlan = PlanString(serialized); _plan.Pop(XFileBlockType.LARGE); _plan.Pop(XFileBlockType.TEMP);
        var writer = new XSourceWriter(); writer.WriteInt32(Pointer(stringPlan)); writer.Reserve(rootSize - sizeof(int)); EmissionBlockSegment root = new(rootAddress, writer.ToArray()); _all.Add(root);
        if (ownerCell.Block != XFileBlockType.TEMP)
            _plan.PersistentXAssetAliasCells.TryAdd(aliasKey, ownerCell);
        return new ExternalPlan(root, [root, .. stringPlan.Source], -1);
    }

    private int PlanOperand(Operand value, string? text, Statement? function, List<EmissionBlockSegment> destination)
    {
        if (value.DataType == ExpDataType.VAL_STRING) { StringPlan result = PlanString(text); Add(destination, result); return Pointer(result); }
        if (value.DataType == ExpDataType.VAL_FUNCTION) { PlannedNode? result = Plan(function, PlanStatementNode); Add(destination, result); return Pointer(result); }
        return value.EncodedValue;
    }

    private static void WriteWindow(XSourceWriter writer, WindowDef value, WindowPlan plan)
    {
        writer.WriteInt32(Pointer(plan.Name)); WriteRectangle(writer, value.Rect); WriteRectangle(writer, value.RectClient); writer.WriteInt32(Pointer(plan.Group)); writer.WriteInt32((int)value.Style); writer.WriteInt32((int)value.Border); writer.WriteInt32((int)value.OwnerDraw); writer.WriteInt32(value.OwnerDrawFlags); writer.WriteSingle(value.BorderSize); writer.WriteInt32((int)value.StaticFlags); WriteInts(writer, value.DynamicFlags.Select(flag => (int)flag).ToArray(), 4, "Window.dynamicFlags"); writer.WriteInt32(value.NextTime); WriteVec4(writer, value.ForeColor); WriteVec4(writer, value.BackColor); WriteVec4(writer, value.BorderColor); WriteVec4(writer, value.OutlineColor); WriteVec4(writer, value.DisableColor); writer.WriteInt32(Pointer(plan.Background));
    }
    private static void WriteRectangle(XSourceWriter writer, RectangleDef value) { writer.WriteSingle(value.X); writer.WriteSingle(value.Y); writer.WriteSingle(value.W); writer.WriteSingle(value.H); writer.WriteByte((byte)value.HorzAlign); writer.WriteByte((byte)value.VertAlign); writer.WriteUInt16(value.Pad12); }
    private static void WriteVec4(XSourceWriter writer, Vec4 value) { writer.WriteSingle(value.A); writer.WriteSingle(value.R); writer.WriteSingle(value.G); writer.WriteSingle(value.B); }
    private static void WriteInts(XSourceWriter writer, IReadOnlyList<int> values, int count, string path) { if (values.Count != count) throw new InvalidDataException($"{path} requires exactly {count} values."); foreach (int value in values) writer.WriteInt32(value); }
    private static void WriteColumns(XSourceWriter writer, IReadOnlyList<ColumnInfo> values) { if (values.Count != 16) throw new InvalidDataException("ListBox.columnInfo requires exactly 16 values."); foreach (ColumnInfo value in values) { writer.WriteInt32(value.Pos); writer.WriteInt32(value.Width); writer.WriteInt32(value.MaxChars); writer.WriteInt32(value.Alignment); } }
    private static void WriteTransitions(XSourceWriter writer, IReadOnlyList<MenuTransition> values) { if (values.Count != 4) throw new InvalidDataException("Menu transition groups require exactly four values."); foreach (MenuTransition value in values) { writer.WriteInt32((int)value.TransitionType); writer.WriteInt32(value.TargetField); writer.WriteInt32(value.StartTime); writer.WriteSingle(value.StartValue); writer.WriteSingle(value.EndValue); writer.WriteSingle(value.Time); writer.WriteInt32((int)value.EndTriggerType); } }
    private static void WriteOperand(XSourceWriter writer, Operand value, int encoded) { writer.WriteInt32((int)value.DataType); writer.WriteInt32(encoded); }
    private static void Exact(XSourceWriter writer, int expected, string name) { if (writer.Position != expected) throw new InvalidDataException($"{name} emission produced 0x{writer.Position:X} bytes instead of 0x{expected:X}."); }

    private bool TryExisting(object value, out PlannedNode planned)
    {
        if (_nodes.TryGetValue(value, out Node? node)) { planned = new PlannedNode(node, false); return true; }
        if (_plan.TryGetPersistentObjectAlias(value, out EmissionAddress address))
        {
            node = new Node(address) { Planning = false };
            _nodes.Add(value, node);
            planned = new PlannedNode(node, false);
            return true;
        }
        planned = default; return false;
    }

    private Node Begin(object value, EmissionAddress address)
    {
        var node = new Node(address);
        _nodes.Add(value, node);
        _plan.RegisterPersistentObjectAlias(value, address);
        return node;
    }

    private void Complete(Node node, byte[] bytes, IEnumerable<EmissionBlockSegment> children)
    {
        node.Root = new EmissionBlockSegment(node.Address, bytes); _all.Add(node.Root);
        node.Source = [node.Root, .. children]; node.Planning = false;
    }

    private PlannedNode? Plan<T>(T? value, Func<T, PlannedNode> planner) where T : class => value is null ? null : planner(value);
    private PlannedNode? Plan<T>(T? value, Func<T, PlannedNode> planner, List<EmissionBlockSegment> destination) where T : class { PlannedNode? result = Plan(value, planner); Add(destination, result); return result; }
    private static int Pointer(PlannedNode? value) => value is null ? 0 : value.Value.Inline ? -1 : value.Value.Node.Address.ToPackedPointer();
    private static int Pointer(StringPlan value) => value.Value is null ? 0 : value.Inline ? -1 : value.Value.Value.Address.ToPackedPointer();
    private static int Pointer(ExternalPlan? value) => value?.PointerRaw ?? 0;
    private static int Pointer(PointerTablePlan value) =>
        value.Table is not null || value.PreserveInlinePresence ? -1 : 0;
    private static int Pointer(EmissionBlockSegment? value) => value is null ? 0 : -1;
    private static void Add(List<EmissionBlockSegment> target, PlannedNode? value) { if (value is { Inline: true } plan) target.AddRange(plan.Node.Source!); }
    private static void Add(List<EmissionBlockSegment> target, StringPlan value) { if (value.Inline) target.AddRange(value.Source); }
    private static void Add(List<EmissionBlockSegment> target, ExternalPlan? value) { if (value is not null) target.AddRange(value.Source); }

    private sealed class Node(EmissionAddress address) { public EmissionAddress Address { get; } = address; public bool Planning { get; set; } = true; public EmissionBlockSegment? Root { get; set; } public IReadOnlyList<EmissionBlockSegment>? Source { get; set; } }
    private readonly record struct PlannedNode(Node Node, bool Inline);
    private sealed record StringPlan(PlannedString? Value, IReadOnlyList<EmissionBlockSegment> Source, bool Inline);
    private sealed record WindowPlan(StringPlan Name, StringPlan Group, ExternalPlan? Background, IReadOnlyList<EmissionBlockSegment> Source);
    private static EmissionAddress Offset(EmissionAddress owner, int byteOffset) =>
        new(owner.Block, checked(owner.Offset + byteOffset));
    private sealed record PointerTablePlan(
        EmissionBlockSegment? Table,
        IReadOnlyList<EmissionBlockSegment> Source,
        bool PreserveInlinePresence = false);
    private sealed record ItemDataPlan(int Pointer, IReadOnlyList<EmissionBlockSegment> Source);
}
