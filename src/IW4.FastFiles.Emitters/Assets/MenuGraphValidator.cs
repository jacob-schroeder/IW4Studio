using IW4.Assets.Assets.Menu;
using IW4.Assets.Math;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>
/// Validates the fully detached recursive portion of a Menu definition.
/// A non-null source pointer must have a managed child; the emitter is then
/// free to choose inline source for the first identity and a packed address
/// for every later reference.  This deliberately rejects a raw pointer whose
/// source was not captured instead of quietly serializing a null.
/// </summary>
internal sealed class MenuGraphValidator
{
    private readonly List<EmissionError> _diagnostics = [];
    private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);
    private readonly int? _rowIndex;

    private MenuGraphValidator(int? rowIndex) => _rowIndex = rowIndex;

    public static IReadOnlyList<EmissionError> Validate(MenuDefAsset definition, string path, int? rowIndex)
    {
        var validator = new MenuGraphValidator(rowIndex);
        validator.Menu(definition, path);
        return validator._diagnostics;
    }

    private void Menu(MenuDefAsset value, string path)
    {
        if (!Visit(value)) return;
        EventSet(value.OnOpen.Raw, value.OnOpenSet, $"{path}.onOpen");
        EventSet(value.OnCloseRequest.Raw, value.OnCloseRequestSet, $"{path}.onCloseRequest");
        EventSet(value.OnClose.Raw, value.OnCloseSet, $"{path}.onClose");
        EventSet(value.OnEsc.Raw, value.OnEscSet, $"{path}.onEsc");
        KeyHandler(value.ExecKeys.Raw, value.ExecKeyHandler, $"{path}.execKeys");
        Statement(value.VisibleExpression.Raw, value.VisibleStatement, $"{path}.visibleExpression");
        Statement(value.RectXExpression.Raw, value.RectXStatement, $"{path}.rectXExpression");
        Statement(value.RectYExpression.Raw, value.RectYStatement, $"{path}.rectYExpression");
        Statement(value.RectWExpression.Raw, value.RectWStatement, $"{path}.rectWExpression");
        Statement(value.RectHExpression.Raw, value.RectHStatement, $"{path}.rectHExpression");
        Supporting(value.ExpressionData.Raw, value.ExpressionDataValue, $"{path}.expressionData");
        for (int index = 0; index < value.Items.Count; index++)
        {
            var row = value.Items[index];
            Item(row.Pointer.Raw, row.Item, $"{path}.items[{index}]");
        }
    }

    private void Item(int raw, ItemDefAsset? value, string path)
    {
        if (!Require(raw, value, path) || !Visit(value!)) return;
        if (!Enum.IsDefined(value!.Type)) Error($"{path}.type", "Unknown item type discriminator.");
        if (value.TextRect.Count != 4) Error($"{path}.textRect", "Item requires exactly four text rectangles.");
        if (value.CursorPos.Count != 4) Error($"{path}.cursorPos", "Item requires exactly four cursor positions.");
        Window(value.Window, $"{path}.window");
        String(value.Text.Raw, value.TextString, $"{path}.text");
        EventSet(value.MouseEnterText.Raw, value.MouseEnterTextSet, $"{path}.mouseEnterText");
        EventSet(value.MouseExitText.Raw, value.MouseExitTextSet, $"{path}.mouseExitText");
        EventSet(value.MouseEnter.Raw, value.MouseEnterSet, $"{path}.mouseEnter");
        EventSet(value.MouseExit.Raw, value.MouseExitSet, $"{path}.mouseExit");
        EventSet(value.Action.Raw, value.ActionSet, $"{path}.action");
        EventSet(value.Accept.Raw, value.AcceptSet, $"{path}.accept");
        EventSet(value.OnFocus.Raw, value.OnFocusSet, $"{path}.onFocus");
        EventSet(value.LeaveFocus.Raw, value.LeaveFocusSet, $"{path}.leaveFocus");
        String(value.Dvar.Raw, value.DvarString, $"{path}.dvar");
        String(value.DvarTest.Raw, value.DvarTestString, $"{path}.dvarTest");
        KeyHandler(value.OnKey.Raw, value.OnKeyHandler, $"{path}.onKey");
        String(value.EnableDvar.Raw, value.EnableDvarString, $"{path}.enableDvar");
        String(value.FocusSound.Raw, value.FocusSoundName, $"{path}.focusSound");
        ItemData(value, path);
        Count(value.FloatExpressions.Raw, value.FloatExpressionCount, value.LoadedFloatExpressions.Count, $"{path}.floatExpressions");
        for (int index = 0; index < value.LoadedFloatExpressions.Count; index++)
        {
            ItemFloatExpression expression = value.LoadedFloatExpressions[index];
            if (!Enum.IsDefined(expression.Target)) Error($"{path}.floatExpressions[{index}].target", "Unknown float-expression target.");
            Statement(expression.Expression.Raw, expression.Statement, $"{path}.floatExpressions[{index}].statement");
        }
        Statement(value.VisibleExpression.Raw, value.VisibleStatement, $"{path}.visibleExpression");
        Statement(value.DisabledExpression.Raw, value.DisabledStatement, $"{path}.disabledExpression");
        Statement(value.TextExpression.Raw, value.TextStatement, $"{path}.textExpression");
        Statement(value.MaterialExpression.Raw, value.MaterialStatement, $"{path}.materialExpression");
        Finite(value.TextAlignX, $"{path}.textAlignX"); Finite(value.TextAlignY, $"{path}.textAlignY"); Finite(value.TextScale, $"{path}.textScale"); Finite(value.Special, $"{path}.special"); Vec(value.GlowColor, $"{path}.glowColor");
    }

    private void ItemData(ItemDefAsset item, string path)
    {
        switch (item.TypeData.Value)
        {
            case EditFieldItemDefData data:
                if (!IsEditFieldType(item.Type)) Error($"{path}.typeData", "EditField payload does not match the item type.");
                EditField(data.EditFieldPointer.Raw, item.EditField, $"{path}.editField");
                break;
            case ListBoxItemDefData data:
                if (item.Type != ItemDefType.ListBox) Error($"{path}.typeData", "ListBox payload requires ListBox item type.");
                ListBox(data.ListBoxPointer.Raw, item.ListBox, $"{path}.listBox");
                break;
            case MultiItemDefData data:
                if (item.Type != ItemDefType.Multi) Error($"{path}.typeData", "Multi payload requires Multi item type.");
                Multi(data.MultiPointer.Raw, item.Multi, $"{path}.multi");
                break;
            case DvarEnumItemDefData data:
                if (item.Type != ItemDefType.DvarEnum) Error($"{path}.typeData", "DvarEnum payload requires DvarEnum item type.");
                String(data.DvarEnumNamePointer.Raw, item.DvarEnumName, $"{path}.dvarEnum");
                break;
            case NewsTickerItemDefData data:
                if (item.Type != ItemDefType.NewsTicker) Error($"{path}.typeData", "NewsTicker payload requires NewsTicker item type.");
                NewsTicker(data.NewsTickerPointer.Raw, item.NewsTicker, $"{path}.newsTicker");
                break;
            case TextScrollItemDefData data:
                if (item.Type != ItemDefType.TextScroll) Error($"{path}.typeData", "TextScroll payload requires TextScroll item type.");
                TextScroll(data.TextScrollPointer.Raw, item.TextScroll, $"{path}.textScroll");
                break;
            case NoItemDefData:
                if (IsSpecialItemType(item.Type)) Error($"{path}.typeData", "Special item type requires its matching payload union arm.");
                break;
            default:
                Error($"{path}.typeData", "Unknown item-data union arm.");
                break;
        }
    }

    private void EventSet(int raw, MenuEventHandlerSet? value, string path)
    {
        if (!Require(raw, value, path) || !Visit(value!)) return;
        Count(value!.EventHandlers.Raw, value.EventHandlerCount, value.Handlers.Count, $"{path}.handlers");
        for (int index = 0; index < value.Handlers.Count; index++)
        {
            MenuEventHandlerReference row = value.Handlers[index];
            EventHandler(row.Pointer.Raw, row.Handler, $"{path}.handlers[{index}]");
        }
    }

    private void EventHandler(int raw, MenuEventHandler? value, string path)
    {
        if (!Require(raw, value, path) || !Visit(value!)) return;
        if (!Enum.IsDefined(value!.EventType)) { Error($"{path}.eventType", "Unknown event discriminator."); return; }
        switch (value.EventType)
        {
            case MenuEventHandlerType.UnconditionalScript:
                if (value.EventData.UnconditionalScript is not { } script) Error($"{path}.eventData", "Unconditional script arm is missing.");
                else String(script.Script.Raw, value.UnconditionalScript, $"{path}.script");
                break;
            case MenuEventHandlerType.ConditionalScript:
                if (value.EventData.ConditionalScript is not { } conditional) Error($"{path}.eventData", "Conditional script arm is missing.");
                else Conditional(conditional.ConditionalScriptPointer.Raw, value.ConditionalScript, $"{path}.conditional");
                break;
            case MenuEventHandlerType.ElseScript:
                if (value.EventData.ElseScript is not { } elseScript) Error($"{path}.eventData", "Else script arm is missing.");
                else EventSet(elseScript.EventHandlerSetPointer.Raw, value.ElseScriptSet, $"{path}.else");
                break;
            case MenuEventHandlerType.SetLocalVarBool or MenuEventHandlerType.SetLocalVarInt or MenuEventHandlerType.SetLocalVarFloat or MenuEventHandlerType.SetLocalVarString:
                if (value.EventData.SetLocalVarData is not { } local) Error($"{path}.eventData", "Set-local-variable arm is missing.");
                else LocalVar(local.SetLocalVarDataPointer.Raw, value.SetLocalVarData, $"{path}.setLocal");
                break;
        }
    }

    private void Conditional(int raw, ConditionalScript? value, string path)
    {
        if (!Require(raw, value, path) || !Visit(value!)) return;
        EventSet(value!.EventHandlerSet.Raw, value.EventHandlers, $"{path}.handlers");
        Statement(value.EventExpression.Raw, value.EventStatement, $"{path}.expression");
    }

    private void LocalVar(int raw, SetLocalVarData? value, string path)
    {
        if (!Require(raw, value, path) || !Visit(value!)) return;
        String(value!.LocalVarName.Raw, value.LocalVarNameString, $"{path}.name");
        Statement(value.Expression.Raw, value.ExpressionStatement, $"{path}.expression");
    }

    private void KeyHandler(int raw, ItemKeyHandler? value, string path)
    {
        if (!Require(raw, value, path) || !Visit(value!)) return;
        EventSet(value!.Action.Raw, value.ActionSet, $"{path}.action");
        KeyHandler(value.Next.Raw, value.NextHandler, $"{path}.next");
    }

    private void Statement(int raw, Statement? value, string path)
    {
        if (!Require(raw, value, path) || !Visit(value!)) return;
        Count(value!.Entries.Raw, value.NumEntries, value.LoadedEntries.Count, $"{path}.entries");
        for (int index = 0; index < value.LoadedEntries.Count; index++)
        {
            ExpressionEntry entry = value.LoadedEntries[index];
            if (!Enum.IsDefined(entry.Kind))
            {
                Error($"{path}.entries[{index}].kind", "Unknown expression-entry discriminator.");
                continue;
            }
            if (entry.Kind == ExpressionEntryKind.Operator)
            {
                if (!Enum.IsDefined((OperationEnum)entry.OperationCode))
                    Error($"{path}.entries[{index}].operation", "Unknown expression operator code.");
                continue;
            }
            if (!Enum.IsDefined(entry.Operand.DataType)) { Error($"{path}.entries[{index}].operand.dataType", "Unknown expression operand discriminator."); continue; }
            if (entry.Operand.DataType == ExpDataType.VAL_STRING) String(entry.Operand.EncodedValue, entry.StringValue, $"{path}.entries[{index}].string");
            if (entry.Operand.DataType == ExpDataType.VAL_FUNCTION) Statement(entry.Operand.EncodedValue, entry.FunctionStatement, $"{path}.entries[{index}].function");
        }
        Supporting(value.SupportingData.Raw, value.SupportingDataValue, $"{path}.supportingData");
    }

    private void Supporting(int raw, ExpressionSupportingData? value, string path)
    {
        if (!Require(raw, value, path) || !Visit(value!)) return;
        Count(value!.UiFunctions.Functions.Raw, value.UiFunctions.TotalFunctions, value.UiFunctions.LoadedFunctions.Count, $"{path}.functions");
        for (int index = 0; index < value.UiFunctions.LoadedFunctions.Count; index++) { StatementReference row = value.UiFunctions.LoadedFunctions[index]; Statement(row.Pointer.Raw, row.Statement, $"{path}.functions[{index}]"); }
        Count(value.StaticDvarList.StaticDvars.Raw, value.StaticDvarList.NumStaticDvars, value.StaticDvarList.LoadedStaticDvars.Count, $"{path}.staticDvars");
        for (int index = 0; index < value.StaticDvarList.LoadedStaticDvars.Count; index++) { StaticDvarReference row = value.StaticDvarList.LoadedStaticDvars[index]; StaticDvar(row.Pointer.Raw, row.StaticDvar, $"{path}.staticDvars[{index}]"); }
        Count(value.UiStrings.Strings.Raw, value.UiStrings.TotalStrings, value.UiStrings.LoadedStrings.Count, $"{path}.strings");
        for (int index = 0; index < value.UiStrings.LoadedStrings.Count; index++) { XStringReference row = value.UiStrings.LoadedStrings[index]; String(row.Pointer.Raw, row.Value, $"{path}.strings[{index}]"); }
    }

    private void StaticDvar(int raw, StaticDvar? value, string path)
    {
        if (!Require(raw, value, path) || !Visit(value!)) return;
        String(value!.DvarName.Raw, value.DvarNameString, $"{path}.name");
    }

    private void ListBox(int raw, ListBoxDef? value, string path)
    {
        if (!Require(raw, value, path) || !Visit(value!)) return;
        if (value!.StartPos.Count != 4 || value.EndPos.Count != 4) Error(path, "ListBox requires exactly four start/end cursor positions.");
        if (value.ColumnInfo.Count != 16) Error($"{path}.columnInfo", "ListBox requires exactly sixteen columns.");
        if (value.NumColumns is < 0 or > 16) Error($"{path}.numColumns", "ListBox column count must be in [0,16].");
        EventSet(value.DoubleClick.Raw, value.DoubleClickSet, $"{path}.doubleClick");
        String(value.SelectIcon.Raw, value.SelectIconMaterialName, $"{path}.selectIcon");
        Finite(value.ElementWidth, $"{path}.elementWidth"); Finite(value.ElementHeight, $"{path}.elementHeight"); Vec(value.SelectBorder, $"{path}.selectBorder");
    }

    private void Multi(int raw, MultiDef? value, string path)
    {
        if (!Require(raw, value, path) || !Visit(value!)) return;
        if (value!.DvarList.Count != MultiDef.EntryCapacity || value.DvarListStrings.Count != MultiDef.EntryCapacity || value.DvarStr.Count != MultiDef.EntryCapacity || value.DvarStrStrings.Count != MultiDef.EntryCapacity || value.DvarValue.Count != MultiDef.EntryCapacity)
            Error(path, "Multi payload requires all 32-entry fixed arrays.");
        if (value.Count is < 0 or > MultiDef.EntryCapacity) Error($"{path}.count", "Multi count must be in [0,32].");
        for (int index = 0; index < value.DvarList.Count && index < value.DvarListStrings.Count; index++) String(value.DvarList[index].Raw, value.DvarListStrings[index], $"{path}.dvarList[{index}]");
        for (int index = 0; index < value.DvarStr.Count && index < value.DvarStrStrings.Count; index++) String(value.DvarStr[index].Raw, value.DvarStrStrings[index], $"{path}.dvarStr[{index}]");
        foreach (float number in value.DvarValue) Finite(number, $"{path}.dvarValue");
    }

    private void EditField(int raw, EditFieldDef? value, string path)
    {
        if (!Require(raw, value, path) || !Visit(value!)) return;
        Finite(value!.MinVal, $"{path}.minVal"); Finite(value.MaxVal, $"{path}.maxVal"); Finite(value.DefVal, $"{path}.defVal"); Finite(value.Range, $"{path}.range");
    }

    private void NewsTicker(int raw, NewsTickerDef? value, string path)
    {
        if (!Require(raw, value, path) || !Visit(value!)) return;
        Finite(value!.X, $"{path}.x");
    }

    private void TextScroll(int raw, TextScrollDef? value, string path) => Require(raw, value, path);

    private void Window(WindowDef value, string path)
    {
        String(value.NamePointer.Raw, value.Name, $"{path}.name"); String(value.GroupPointer.Raw, value.Group, $"{path}.group");
        String(value.Background.Raw, value.BackgroundMaterialName, $"{path}.background");
        if (value.DynamicFlags.Count != 4) Error($"{path}.dynamicFlags", "Window requires exactly four dynamic flags.");
        Rect(value.Rect, $"{path}.rect"); Rect(value.RectClient, $"{path}.rectClient"); Vec(value.ForeColor, $"{path}.foreColor"); Vec(value.BackColor, $"{path}.backColor"); Vec(value.BorderColor, $"{path}.borderColor"); Vec(value.OutlineColor, $"{path}.outlineColor"); Vec(value.DisableColor, $"{path}.disableColor"); Finite(value.BorderSize, $"{path}.borderSize");
    }

    private void String(int raw, string? value, string path)
    {
        if (raw != 0 && value is null) Error(path, "A non-null serialized XString has no detached text.");
        if (value is not null && !AssetBodyEmitterHelpers.IsLatin1CString(value)) Error(path, "XString must be a Latin-1 C string.");
    }

    private bool Require<T>(int raw, T? value, string path) where T : class
    {
        if (raw != 0 && value is null) { Error(path, "A non-null serialized pointer has no detached graph node."); return false; }
        return value is not null;
    }

    private void Count(int raw, int expected, int actual, string path)
    {
        if (expected < 0 || expected != actual) Error(path, "Serialized count must equal the detached collection length.");
        if (raw != 0 && expected != 0 && actual == 0) Error(path, "A non-null serialized table pointer has no detached rows.");
    }

    private bool Visit(object value) => _visited.Add(value);
    private void Error(string path, string message) => _diagnostics.Add(new EmissionError(path, message, _rowIndex, XAssetType.Menu));
    private void Finite(float value, string path) { if (!float.IsFinite(value)) Error(path, "Value must be finite."); }
    private void Vec(Vec4 value, string path) { Finite(value.A, $"{path}.a"); Finite(value.R, $"{path}.r"); Finite(value.G, $"{path}.g"); Finite(value.B, $"{path}.b"); }
    private void Rect(RectangleDef value, string path) { Finite(value.X, $"{path}.x"); Finite(value.Y, $"{path}.y"); Finite(value.W, $"{path}.w"); Finite(value.H, $"{path}.h"); if (!Enum.IsDefined(value.HorzAlign) || !Enum.IsDefined(value.VertAlign)) Error(path, "Invalid rectangle alignment discriminator."); }
    private static bool IsSpecialItemType(ItemDefType value) => value is ItemDefType.ListBox or ItemDefType.Multi or ItemDefType.DvarEnum or ItemDefType.NewsTicker or ItemDefType.TextScroll || IsEditFieldType(value);
    private static bool IsEditFieldType(ItemDefType value) => value is ItemDefType.Text or ItemDefType.EditField or ItemDefType.NumericField or ItemDefType.Slider or ItemDefType.YesNo or ItemDefType.Bind or ItemDefType.Validation or ItemDefType.DecimalField or ItemDefType.UpDown or ItemDefType.EmailField or ItemDefType.PassWordField;
}
