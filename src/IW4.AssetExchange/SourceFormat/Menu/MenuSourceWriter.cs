using System.Globalization;
using IW4.Assets.Assets.Menu;
using IW4.Assets.Math;

namespace IW4.AssetExchange.SourceFormat.Menu;

internal sealed partial class MenuSourceWriter
{
    private const int KeySpacing = 28;
    private const float FloatComparisonEpsilon = 1.1920929E-07f;

    private static readonly uint KnownWindowStaticFlagMask =
        Enum.GetValues<WindowStaticFlags>()
            .Aggregate(
                0u,
                (mask, value) => mask | unchecked((uint)(int)value));

    private readonly TextWriter _writer;
    private int _indent;

    public MenuSourceWriter(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    public void Start()
    {
        Indent();
        _writer.WriteLine('{');
        _indent++;
    }

    public void End()
    {
        if (_indent != 1)
        {
            throw new InvalidOperationException(
                $"Menu source writer ended with {_indent} open scopes.");
        }

        EndScope();
    }

    public void IncludeMenu(string menuPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(menuPath);
        Indent();
        _writer.Write("loadMenu { ");
        WriteEscapedString(menuPath);
        _writer.WriteLine(" }");
    }

    public void WriteSharedFunctionDefinitions(
        IEnumerable<MenuDefAsset> menus)
    {
        ArgumentNullException.ThrowIfNull(menus);
        ExpressionSupportingData? shared = null;
        foreach (MenuDefAsset menu in menus)
        {
            ExpressionSupportingData? candidate = menu.ExpressionDataValue;
            if (candidate is null)
                continue;

            if (shared is null)
            {
                shared = candidate;
                continue;
            }

            if (!SupportingDataEquivalent(shared, candidate))
            {
                throw new InvalidDataException(
                    "A MenuFile references multiple non-equivalent expression-supporting-data roots.");
            }
        }

        if (shared is null)
            return;

        UIFunctionList functions = shared.UiFunctions;
        if (functions.TotalFunctions < 0)
        {
            throw new InvalidDataException(
                $"Menu UIFunctionList has invalid TotalFunctions {functions.TotalFunctions}.");
        }

        IReadOnlyDictionary<int, StatementReference> rows = FunctionRows(functions);
        for (int index = 0; index < functions.TotalFunctions; index++)
        {
            if (!rows.TryGetValue(index, out StatementReference? reference) ||
                reference.Statement is null)
            {
                continue;
            }

            StartScope("functionDef");
            WriteStringProperty("name", $"FUNC_{index}");
            WriteStatementProperty("value", reference.Statement, isBoolean: false);
            EndScope();
        }
    }

    public void WriteMenu(MenuDefAsset menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        StartScope("menuDef");
        WriteMenuData(menu);
        EndScope();
    }

    private void WriteMenuData(MenuDefAsset menu)
    {
        WindowDef window = menu.Window;
        ValidateWindowStaticFlags(window, $"Menu '{window.Name}'");
        WriteStringProperty("name", window.Name);
        WriteBoolProperty("fullscreen", menu.Fullscreen != 0, defaultValue: false);
        WriteKeywordProperty(
            "screenSpace",
            HasFlag(window.StaticFlags, WindowStaticFlags.WINDOW_STATIC_SCREEN_SPACE));
        WriteKeywordProperty(
            "decoration",
            HasFlag(window.StaticFlags, WindowStaticFlags.WINDOW_STATIC_DECORATION));
        WriteRectProperty("rect", window.Rect);
        WriteIntProperty("style", (int)window.Style, 0);
        WriteIntProperty("border", (int)window.Border, 0);
        WriteFloatProperty("borderSize", window.BorderSize, 0.0f);
        WriteColorProperty("backcolor", window.BackColor, ColorDefault.Zero);
        WriteColorProperty("forecolor", window.ForeColor, ColorDefault.One);
        WriteColorProperty("bordercolor", window.BorderColor, ColorDefault.Zero);
        WriteColorProperty("focuscolor", menu.FocusColor, ColorDefault.Zero);
        WriteColorProperty("outlinecolor", window.OutlineColor, ColorDefault.Zero);
        WriteMaterialProperty(
            "background",
            window.BackgroundMaterialName ?? window.BackgroundMaterial?.Info.Name);
        WriteIntProperty("ownerdraw", (int)window.OwnerDraw, 0);
        WriteFlagsProperty("ownerdrawFlag", window.OwnerDrawFlags);
        WriteKeywordProperty(
            "outOfBoundsClick",
            HasFlag(
                window.StaticFlags,
                WindowStaticFlags.WINDOW_STATIC_OUT_OF_BOUNDS_CLICK));
        WriteStringProperty("soundLoop", menu.SoundNameString);
        WriteKeywordProperty(
            "popup",
            HasFlag(window.StaticFlags, WindowStaticFlags.WINDOW_STATIC_POPUP));
        WriteFloatProperty("fadeClamp", menu.FadeClamp, 0.0f);
        WriteIntProperty("fadeCycle", menu.FadeCycle, 0);
        WriteFloatProperty("fadeAmount", menu.FadeAmount, 0.0f);
        WriteFloatProperty("fadeInAmount", menu.FadeInAmount, 0.0f);
        WriteFloatProperty("blurWorld", menu.BlurRadius, 0.0f);
        WriteKeywordProperty(
            "legacySplitScreenScale",
            HasFlag(
                window.StaticFlags,
                WindowStaticFlags.WINDOW_STATIC_LEGACY_SPLITSCREEN_SCALE));
        WriteKeywordProperty(
            "hiddenDuringScope",
            HasFlag(
                window.StaticFlags,
                WindowStaticFlags.WINDOW_STATIC_HIDDEN_DURING_SCOPE));
        WriteKeywordProperty(
            "hiddenDuringFlashbang",
            HasFlag(
                window.StaticFlags,
                WindowStaticFlags.WINDOW_STATIC_HIDDEN_DURING_FLASH));
        WriteKeywordProperty(
            "hiddenDuringUI",
            HasFlag(
                window.StaticFlags,
                WindowStaticFlags.WINDOW_STATIC_HIDDEN_DURING_UI));
        WriteStringProperty("allowedBinding", menu.AllowedBindingString);
        WriteKeywordProperty(
            "textOnlyFocus",
            HasFlag(
                window.StaticFlags,
                WindowStaticFlags.WINDOW_STATIC_TEXT_ONLY_FOCUS));

        if (menu.VisibleStatement is not null)
        {
            WriteStatementProperty("visible", menu.VisibleStatement, isBoolean: true);
        }
        else if (IsVisible(window))
        {
            WriteIntProperty("visible", 1, 0);
        }

        WriteStatementProperty("exp rect X", menu.RectXStatement, isBoolean: false);
        WriteStatementProperty("exp rect Y", menu.RectYStatement, isBoolean: false);
        WriteStatementProperty("exp rect W", menu.RectWStatement, isBoolean: false);
        WriteStatementProperty("exp rect H", menu.RectHStatement, isBoolean: false);
        WriteEventHandlerSetProperty("onOpen", menu.OnOpenSet);
        WriteEventHandlerSetProperty("onClose", menu.OnCloseSet);
        WriteEventHandlerSetProperty("onRequestClose", menu.OnCloseRequestSet);
        WriteEventHandlerSetProperty("onESC", menu.OnEscSet);
        WriteItemKeyHandlers(menu.ExecKeyHandler);
        WriteItems(menu);
    }

    private void WriteItems(MenuDefAsset menu)
    {
        if (menu.ItemCount < 0)
        {
            throw new InvalidDataException(
                $"Menu '{menu.Window.Name}' has invalid ItemCount {menu.ItemCount}.");
        }

        if (menu.ItemCount != menu.Items.Count)
        {
            throw new InvalidDataException(
                $"Menu '{menu.Window.Name}' declares {menu.ItemCount} items " +
                $"but exposes {menu.Items.Count} resolved registrations.");
        }

        foreach (ItemDefReference reference in menu.Items)
        {
            ItemDefAsset? item = reference.Item;
            if (item is null)
                continue;

            StartScope("itemDef");
            WriteItemData(item);
            EndScope();
        }
    }

    private void WriteItemData(ItemDefAsset item)
    {
        WindowDef window = item.Window;
        ValidateWindowStaticFlags(window, $"Item '{window.Name}'");
        WriteStringProperty("name", window.Name);
        WriteStringProperty("text", item.TextString);
        WriteKeywordProperty(
            "textsavegame",
            item.ItemFlags.HasFlag(ItemFlags.SaveGameText));
        WriteKeywordProperty(
            "textcinematicsubtitle",
            item.ItemFlags.HasFlag(ItemFlags.CinematicSubtitle));
        WriteStringProperty("group", window.Group);
        WriteRectProperty("rect", window.RectClient);
        WriteIntProperty("style", (int)window.Style, 0);
        WriteKeywordProperty(
            "decoration",
            HasFlag(window.StaticFlags, WindowStaticFlags.WINDOW_STATIC_DECORATION));
        WriteKeywordProperty(
            "autowrapped",
            HasFlag(window.StaticFlags, WindowStaticFlags.WINDOW_STATIC_AUTOWRAPPED));
        WriteKeywordProperty(
            "horizontalscroll",
            HasFlag(window.StaticFlags, WindowStaticFlags.WINDOW_STATIC_HORIZONTAL));
        WriteIntProperty("type", (int)item.Type, (int)ItemDefType.Text);
        WriteIntProperty("border", (int)window.Border, 0);
        WriteFloatProperty("borderSize", window.BorderSize, 0.0f);

        if (item.VisibleStatement is not null)
        {
            WriteStatementProperty("visible", item.VisibleStatement, isBoolean: true);
        }
        else if (IsVisible(window))
        {
            WriteIntProperty("visible", 1, 0);
        }

        WriteStatementProperty("disabled", item.DisabledStatement, isBoolean: true);
        WriteIntProperty("ownerdraw", (int)window.OwnerDraw, 0);
        WriteFlagsProperty("ownerdrawFlag", window.OwnerDrawFlags);
        WriteIntProperty("align", (int)item.Align, 0);
        WriteIntProperty("textalign", item.TextAlignMode, 0);
        WriteFloatProperty("textalignx", item.TextAlignX, 0.0f);
        WriteFloatProperty("textaligny", item.TextAlignY, 0.0f);
        WriteFloatProperty("textscale", item.TextScale, 0.0f);
        WriteIntProperty("textstyle", (int)item.TextStyle, 0);
        WriteIntProperty("textfont", (int)item.FontEnum, 0);
        WriteColorProperty("backcolor", window.BackColor, ColorDefault.Zero);
        WriteColorProperty("forecolor", window.ForeColor, ColorDefault.One);
        WriteColorProperty("bordercolor", window.BorderColor, ColorDefault.Zero);
        WriteColorProperty("outlinecolor", window.OutlineColor, ColorDefault.Zero);
        WriteColorProperty("disablecolor", window.DisableColor, ColorDefault.Zero);
        WriteColorProperty("glowcolor", item.GlowColor, ColorDefault.Zero);
        WriteMaterialProperty(
            "background",
            window.BackgroundMaterialName ?? window.BackgroundMaterial?.Info.Name);
        WriteEventHandlerSetProperty("onFocus", item.OnFocusSet);
        WriteEventHandlerSetProperty("leaveFocus", item.LeaveFocusSet);
        WriteEventHandlerSetProperty("mouseEnter", item.MouseEnterSet);
        WriteEventHandlerSetProperty("mouseExit", item.MouseExitSet);
        WriteEventHandlerSetProperty("mouseEnterText", item.MouseEnterTextSet);
        WriteEventHandlerSetProperty("mouseExitText", item.MouseExitTextSet);
        WriteEventHandlerSetProperty("action", item.ActionSet);
        WriteEventHandlerSetProperty("accept", item.AcceptSet);
        WriteStringProperty(
            "focusSound",
            item.FocusSoundName ?? item.FocusSoundAsset?.AliasName);
        WriteStringProperty("dvarTest", item.DvarTestString);
        WriteDvarCondition(item);
        WriteItemKeyHandlers(item.OnKeyHandler);
        WriteStatementProperty("exp text", item.TextStatement, isBoolean: false);
        WriteStatementProperty("exp material", item.MaterialStatement, isBoolean: false);
        WriteFloatExpressions(item);
        WriteIntProperty("gamemsgwindowindex", item.GameMsgWindowIndex, 0);
        WriteIntProperty("gamemsgwindowmode", item.GameMsgWindowMode, 0);
        WriteDecodeEffect(item);
        WriteListBoxProperties(item);
        WriteEditFieldProperties(item);
        WriteMultiProperties(item);
        WriteDvarEnumProperties(item);
        WriteNewsTickerProperties(item);
    }

    private void WriteDvarCondition(ItemDefAsset item)
    {
        string? property = item.DvarFlags switch
        {
            var flags when flags.HasFlag(ItemDvarFlags.Enable) => "enableDvar",
            var flags when flags.HasFlag(ItemDvarFlags.Disable) => "disableDvar",
            var flags when flags.HasFlag(ItemDvarFlags.Show) => "showDvar",
            var flags when flags.HasFlag(ItemDvarFlags.Hide) => "hideDvar",
            var flags when flags.HasFlag(ItemDvarFlags.Focus) => "focusDvar",
            _ => null
        };

        if (property is not null)
            WriteMultiTokenStringProperty(property, item.EnableDvarString);
    }

    private void WriteFloatExpressions(ItemDefAsset item)
    {
        if (item.FloatExpressionCount < 0)
        {
            throw new InvalidDataException(
                $"Item '{item.Window.Name}' has invalid FloatExpressionCount " +
                $"{item.FloatExpressionCount}.");
        }

        if (item.FloatExpressionCount != item.LoadedFloatExpressions.Count)
        {
            throw new InvalidDataException(
                $"Item '{item.Window.Name}' declares {item.FloatExpressionCount} float expressions " +
                $"but exposes {item.LoadedFloatExpressions.Count} loaded expressions.");
        }

        foreach (ItemFloatExpression expression in item.LoadedFloatExpressions)
        {
            if (!TryFloatExpressionBinding(
                    expression.Target,
                    out string? field,
                    out string? component))
            {
                throw new InvalidDataException(
                    $"Item '{item.Window.Name}' has unsupported float-expression target " +
                    $"0x{(int)expression.Target:X}.");
            }

            WriteStatementProperty(
                $"exp {field} {component}",
                expression.Statement,
                isBoolean: false);
        }
    }

    private static bool TryFloatExpressionBinding(
        ItemFloatExpressionTarget target,
        out string? field,
        out string? component)
    {
        (field, component) = target switch
        {
            ItemFloatExpressionTarget.RectX => ("rect", "x"),
            ItemFloatExpressionTarget.RectY => ("rect", "y"),
            ItemFloatExpressionTarget.RectW => ("rect", "w"),
            ItemFloatExpressionTarget.RectH => ("rect", "h"),
            ItemFloatExpressionTarget.ForeColorR => ("forecolor", "r"),
            ItemFloatExpressionTarget.ForeColorG => ("forecolor", "g"),
            ItemFloatExpressionTarget.ForeColorB => ("forecolor", "b"),
            ItemFloatExpressionTarget.ForeColorRgb => ("forecolor", "rgb"),
            ItemFloatExpressionTarget.ForeColorA => ("forecolor", "a"),
            ItemFloatExpressionTarget.GlowColorR => ("glowcolor", "r"),
            ItemFloatExpressionTarget.GlowColorG => ("glowcolor", "g"),
            ItemFloatExpressionTarget.GlowColorB => ("glowcolor", "b"),
            ItemFloatExpressionTarget.GlowColorRgb => ("glowcolor", "rgb"),
            ItemFloatExpressionTarget.GlowColorA => ("glowcolor", "a"),
            ItemFloatExpressionTarget.BackColorR => ("backcolor", "r"),
            ItemFloatExpressionTarget.BackColorG => ("backcolor", "g"),
            ItemFloatExpressionTarget.BackColorB => ("backcolor", "b"),
            ItemFloatExpressionTarget.BackColorRgb => ("backcolor", "rgb"),
            ItemFloatExpressionTarget.BackColorA => ("backcolor", "a"),
            _ => (null, null)
        };
        return field is not null;
    }

    private void WriteDecodeEffect(ItemDefAsset item)
    {
        if (item.DecayActive == 0)
            return;

        Indent();
        WriteKey("decodeEffect");
        WriteInt(item.FxLetterTime);
        _writer.Write(' ');
        WriteInt(item.FxDecayStartTime);
        _writer.Write(' ');
        WriteInt(item.FxDecayDuration);
        _writer.WriteLine();
    }

    private void WriteListBoxProperties(ItemDefAsset item)
    {
        if (item.Type != ItemDefType.ListBox || item.ListBox is null)
            return;

        ListBoxDef listBox = item.ListBox;
        WriteKeywordProperty("notselectable", listBox.NotSelectable != 0);
        WriteKeywordProperty("noscrollbars", listBox.NoScrollbars != 0);
        WriteKeywordProperty("usepaging", listBox.UsePaging != 0);
        WriteFloatProperty("elementwidth", listBox.ElementWidth, 0.0f);
        WriteFloatProperty("elementheight", listBox.ElementHeight, 0.0f);
        WriteFloatProperty("feeder", item.Special, 0.0f);
        WriteIntProperty("elementtype", listBox.ElementStyle, 0);
        WriteColumns(listBox);
        WriteEventHandlerSetProperty("doubleclick", listBox.DoubleClickSet);
        WriteColorProperty("selectBorder", listBox.SelectBorder, ColorDefault.Zero);
        WriteMaterialProperty(
            "selectIcon",
            listBox.SelectIconMaterialName ?? listBox.SelectIconMaterial?.Info.Name);
    }

    private void WriteColumns(ListBoxDef listBox)
    {
        if (listBox.NumColumns < 0)
        {
            throw new InvalidDataException(
                $"ListBox has invalid NumColumns {listBox.NumColumns}.");
        }

        if (listBox.NumColumns == 0)
            return;

        if (listBox.NumColumns > listBox.ColumnInfo.Count)
        {
            throw new InvalidDataException(
                $"ListBox declares {listBox.NumColumns} columns but exposes " +
                $"{listBox.ColumnInfo.Count} column rows.");
        }

        Indent();
        WriteKey("columns");
        WriteInt(listBox.NumColumns);
        _writer.WriteLine();

        for (int index = 0; index < listBox.NumColumns; index++)
        {
            ColumnInfo column = listBox.ColumnInfo[index];
            Indent();
            _writer.Write(new string(' ', KeySpacing));
            WriteInt(column.Pos);
            _writer.Write(' ');
            WriteInt(column.Width);
            _writer.Write(' ');
            WriteInt(column.MaxChars);
            _writer.Write(' ');
            WriteInt(column.Alignment);
            _writer.WriteLine();
        }
    }

    private void WriteEditFieldProperties(ItemDefAsset item)
    {
        if (item.Type is not (ItemDefType.Text or
            ItemDefType.EditField or
            ItemDefType.NumericField or
            ItemDefType.Slider or
            ItemDefType.YesNo or
            ItemDefType.Bind or
            ItemDefType.Validation or
            ItemDefType.DecimalField or
            ItemDefType.UpDown or
            ItemDefType.EmailField or
            ItemDefType.PassWordField) ||
            item.EditField is null)
        {
            return;
        }

        EditFieldDef editField = item.EditField;
        if (!ApproximatelyEqual(editField.DefVal, -1.0f) ||
            !ApproximatelyEqual(editField.MinVal, -1.0f) ||
            !ApproximatelyEqual(editField.MaxVal, -1.0f))
        {
            WriteDvarFloatProperty(item.DvarString, editField);
        }
        else
        {
            WriteStringProperty("dvar", item.DvarString);
        }

        WriteIntProperty("maxChars", editField.MaxChars, 0);
        WriteKeywordProperty("maxCharsGotoNext", editField.MaxCharsGotoNext != 0);
        WriteIntProperty("maxPaintChars", editField.MaxPaintChars, 0);
    }

    private void WriteDvarFloatProperty(string? dvar, EditFieldDef editField)
    {
        if (string.IsNullOrEmpty(dvar))
            return;

        Indent();
        WriteKey("dvarFloat");
        WriteEscapedString(dvar);
        _writer.Write(' ');
        WriteFloat(editField.DefVal);
        _writer.Write(' ');
        WriteFloat(editField.MinVal);
        _writer.Write(' ');
        WriteFloat(editField.MaxVal);
        _writer.WriteLine();
    }

    private void WriteMultiProperties(ItemDefAsset item)
    {
        if (item.Type != ItemDefType.Multi || item.Multi is null)
            return;

        MultiDef multi = item.Multi;
        if (multi.Count < 0 || multi.Count > MultiDef.EntryCapacity)
        {
            throw new InvalidDataException(
                $"Multi item has invalid Count {multi.Count}.");
        }

        if (multi.Count == 0)
            return;

        WriteStringProperty("dvar", item.DvarString);
        Indent();
        WriteKey(multi.StrDef != 0 ? "dvarStrList" : "dvarFloatList");
        _writer.Write('{');
        for (int index = 0; index < multi.Count; index++)
        {
            string? label = ValueAt(multi.DvarListStrings, index);
            string? stringValue = multi.StrDef != 0
                ? ValueAt(multi.DvarStrStrings, index)
                : null;
            if (label is null || multi.StrDef != 0 && stringValue is null)
                continue;

            _writer.Write(' ');
            WriteEscapedString(label);
            _writer.Write(' ');
            if (multi.StrDef != 0)
            {
                WriteEscapedString(stringValue!);
            }
            else
            {
                if (index >= multi.DvarValue.Count)
                {
                    throw new InvalidDataException(
                        $"Multi item is missing numeric value row {index}.");
                }

                WriteFloat(multi.DvarValue[index]);
            }
        }

        _writer.WriteLine(" }");
    }

    private void WriteDvarEnumProperties(ItemDefAsset item)
    {
        if (item.Type != ItemDefType.DvarEnum)
            return;

        WriteStringProperty("dvar", item.DvarString);
        WriteStringProperty("dvarEnumList", item.DvarEnumName);
    }

    private void WriteNewsTickerProperties(ItemDefAsset item)
    {
        if (item.Type != ItemDefType.NewsTicker || item.NewsTicker is null)
            return;

        WriteIntProperty("spacing", item.NewsTicker.Spacing, 0);
        WriteIntProperty("speed", item.NewsTicker.Speed, 0);
        WriteIntProperty("newsfeed", item.NewsTicker.FeedId, 0);
    }

    private void StartScope(string name)
    {
        Indent();
        _writer.WriteLine(name);
        Indent();
        _writer.WriteLine('{');
        _indent++;
    }

    private void EndScope()
    {
        if (_indent <= 0)
            throw new InvalidOperationException("Menu source writer has no open scope.");

        _indent--;
        Indent();
        _writer.WriteLine('}');
    }

    private void Indent() =>
        _writer.Write(new string(' ', _indent * 4));

    private void WriteKey(string key)
    {
        _writer.Write(key);
        if (key.Length < KeySpacing)
            _writer.Write(new string(' ', KeySpacing - key.Length));
    }

    private void WriteStringProperty(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        Indent();
        WriteKey(key);
        WriteEscapedString(value);
        _writer.WriteLine();
    }

    private void WriteBoolProperty(string key, bool value, bool defaultValue)
    {
        if (value == defaultValue)
            return;

        Indent();
        WriteKey(key);
        _writer.WriteLine(value ? "1" : "0");
    }

    private void WriteIntProperty(string key, int value, int defaultValue)
    {
        if (value == defaultValue)
            return;

        Indent();
        WriteKey(key);
        WriteInt(value);
        _writer.WriteLine();
    }

    private void WriteFloatProperty(string key, float value, float defaultValue)
    {
        if (ApproximatelyEqual(value, defaultValue))
            return;

        Indent();
        WriteKey(key);
        WriteFloat(value);
        _writer.WriteLine();
    }

    private void WriteColorProperty(
        string key,
        Vec4 value,
        ColorDefault defaultValue)
    {
        float expected = defaultValue == ColorDefault.Zero ? 0.0f : 1.0f;
        if (ApproximatelyEqual(value.A, expected) &&
            ApproximatelyEqual(value.R, expected) &&
            ApproximatelyEqual(value.G, expected) &&
            ApproximatelyEqual(value.B, expected))
        {
            return;
        }

        Indent();
        WriteKey(key);
        WriteFloat(value.A);
        _writer.Write(' ');
        WriteFloat(value.R);
        _writer.Write(' ');
        WriteFloat(value.G);
        _writer.Write(' ');
        WriteFloat(value.B);
        _writer.WriteLine();
    }

    private void WriteKeywordProperty(string key, bool shouldWrite)
    {
        if (!shouldWrite)
            return;

        Indent();
        _writer.WriteLine(key);
    }

    private void WriteFlagsProperty(string key, int flags)
    {
        uint remaining = unchecked((uint)flags);
        for (int bit = 0; bit < 32; bit++)
        {
            if ((remaining & (1u << bit)) == 0)
                continue;

            Indent();
            WriteKey(key);
            WriteInt(bit);
            _writer.WriteLine();
        }
    }

    private void WriteRectProperty(string key, RectangleDef rect)
    {
        Indent();
        WriteKey(key);
        WriteFloat(rect.X);
        _writer.Write(' ');
        WriteFloat(rect.Y);
        _writer.Write(' ');
        WriteFloat(rect.W);
        _writer.Write(' ');
        WriteFloat(rect.H);
        _writer.Write(' ');
        WriteInt((int)rect.HorzAlign);
        _writer.Write(' ');
        WriteInt((int)rect.VertAlign);
        _writer.WriteLine();
    }

    private void WriteMaterialProperty(string key, string? materialName)
    {
        if (string.IsNullOrEmpty(materialName))
            return;

        WriteStringProperty(
            key,
            materialName[0] == ',' ? materialName[1..] : materialName);
    }

    private void WriteEscapedString(string value)
    {
        _writer.Write('"');
        foreach (char character in value)
        {
            switch (character)
            {
                case '\r':
                    _writer.Write("\\r");
                    break;
                case '\n':
                    _writer.Write("\\n");
                    break;
                case '\t':
                    _writer.Write("\\t");
                    break;
                case '\f':
                    _writer.Write("\\f");
                    break;
                case '"':
                    _writer.Write("\\\"");
                    break;
                default:
                    _writer.Write(character);
                    break;
            }
        }

        _writer.Write('"');
    }

    private void WriteInt(int value) =>
        _writer.Write(value.ToString(CultureInfo.InvariantCulture));

    private void WriteFloat(float value) =>
        _writer.Write(value.ToString("G9", CultureInfo.InvariantCulture));

    private static bool ApproximatelyEqual(float left, float right) =>
        MathF.Abs(left - right) < FloatComparisonEpsilon;

    private static bool HasFlag(
        WindowStaticFlags value,
        WindowStaticFlags flag) =>
        (value & flag) != 0;

    private static void ValidateWindowStaticFlags(
        WindowDef window,
        string owner)
    {
        uint unmapped = unchecked((uint)(int)window.StaticFlags) &
            ~KnownWindowStaticFlagMask;
        if (unmapped != 0)
        {
            throw new InvalidDataException(
                $"{owner} has unmapped PS3 Window.StaticFlags bits 0x{unmapped:X8}.");
        }
    }

    private static bool IsVisible(WindowDef window) =>
        window.DynamicFlags.Count > 0 &&
        (window.DynamicFlags[0] & WindowDynamicFlags.WINDOW_DYNAMIC_VISIBLE) != 0;

    private static T? ValueAt<T>(IReadOnlyList<T> values, int index) =>
        (uint)index < (uint)values.Count ? values[index] : default;

    private enum ColorDefault
    {
        Zero,
        One
    }
}
