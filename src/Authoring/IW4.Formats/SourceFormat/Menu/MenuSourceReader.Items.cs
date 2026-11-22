using IW4.Game.Assets.Menu;
using IW4.Game.Math;
using IW4.Game.Pointers;

namespace IW4.Formats.SourceFormat.Menu;

internal sealed partial class MenuSourceReader
{
    private sealed class SourceWindow
    {
        public string? Name;
        public string? Group;
        public RectangleDef Rect = new();
        public WindowStyle Style;
        public WindowBorder Border;
        public WindowOwnerDraw OwnerDraw;
        public int OwnerDrawFlags;
        public float BorderSize;
        public WindowStaticFlags StaticFlags;
        public WindowDynamicFlags DynamicFlags;
        public Vec4 ForeColor = OneColor();
        public Vec4 BackColor = new();
        public Vec4 BorderColor = new();
        public Vec4 OutlineColor = new();
        public Vec4 DisableColor = new();
        public string? Background;

        public WindowDef Build(bool isItem) => new()
        {
            NamePointer = Inline(Name),
            Name = Name,
            Rect = Rect,
            RectClient = isItem ? Rect : new RectangleDef(),
            GroupPointer = Inline(Group),
            Group = Group,
            Style = Style,
            Border = Border,
            OwnerDraw = OwnerDraw,
            OwnerDrawFlags = OwnerDrawFlags,
            BorderSize = BorderSize,
            StaticFlags = StaticFlags,
            DynamicFlags = [DynamicFlags, WindowDynamicFlags.None, WindowDynamicFlags.None, WindowDynamicFlags.None],
            ForeColor = ForeColor,
            BackColor = BackColor,
            BorderColor = BorderColor,
            OutlineColor = OutlineColor,
            DisableColor = DisableColor,
            Background = default,
            BackgroundMaterialName = Background
        };
    }

    private bool ReadWindowProperty(string property, SourceWindow window)
    {
        switch (property)
        {
            case "name": window.Name = Text(); return true;
            case "group": window.Group = Text(); return true;
            case "rect":
                window.Rect = new RectangleDef
                {
                    X = Number(), Y = Number(), W = Number(), H = Number(),
                    HorzAlign = ReadEnum<HorizontalAlign>("horizontal rectangle alignment"),
                    VertAlign = ReadEnum<VerticalAlign>("vertical rectangle alignment")
                };
                return true;
            case "style": window.Style = ReadEnum<WindowStyle>("window style"); return true;
            case "border": window.Border = ReadEnum<WindowBorder>("window border"); return true;
            case "bordersize": window.BorderSize = Number(); return true;
            case "backcolor": window.BackColor = ReadColor(); return true;
            case "forecolor":
                window.ForeColor = ReadColor();
                window.DynamicFlags |= WindowDynamicFlags.WINDOW_DYNAMIC_HAS_FORECOLOR;
                return true;
            case "bordercolor": window.BorderColor = ReadColor(); return true;
            case "outlinecolor": window.OutlineColor = ReadColor(); return true;
            case "disablecolor": window.DisableColor = ReadColor(); return true;
            case "background": window.Background = Text(); return true;
            case "ownerdraw": window.OwnerDraw = (WindowOwnerDraw)Integer(); return true;
            case "ownerdrawflag":
                int bit = Integer();
                if (bit is < 0 or > 31)
                    throw Error("ownerdrawFlag bit must be in [0, 31].");
                window.OwnerDrawFlags |= unchecked((int)(1u << bit));
                return true;
            case "screenspace": window.StaticFlags |= WindowStaticFlags.WINDOW_STATIC_SCREEN_SPACE; return true;
            case "decoration": window.StaticFlags |= WindowStaticFlags.WINDOW_STATIC_DECORATION; return true;
            case "autowrapped": window.StaticFlags |= WindowStaticFlags.WINDOW_STATIC_AUTOWRAPPED; return true;
            case "horizontalscroll": window.StaticFlags |= WindowStaticFlags.WINDOW_STATIC_HORIZONTAL; return true;
            case "outofboundsclick": window.StaticFlags |= WindowStaticFlags.WINDOW_STATIC_OUT_OF_BOUNDS_CLICK; return true;
            case "popup": window.StaticFlags |= WindowStaticFlags.WINDOW_STATIC_POPUP; return true;
            case "legacysplitscreenscale": window.StaticFlags |= WindowStaticFlags.WINDOW_STATIC_LEGACY_SPLITSCREEN_SCALE; return true;
            case "hiddenduringscope": window.StaticFlags |= WindowStaticFlags.WINDOW_STATIC_HIDDEN_DURING_SCOPE; return true;
            case "hiddenduringflashbang": window.StaticFlags |= WindowStaticFlags.WINDOW_STATIC_HIDDEN_DURING_FLASH; return true;
            case "hiddenduringui": window.StaticFlags |= WindowStaticFlags.WINDOW_STATIC_HIDDEN_DURING_UI; return true;
            case "textonlyfocus": window.StaticFlags |= WindowStaticFlags.WINDOW_STATIC_TEXT_ONLY_FOCUS; return true;
            default: return false;
        }
    }

    private ItemDefAsset ReadItem()
    {
        Expect("{");
        var window = new SourceWindow();
        var keys = new List<(int Key, MenuEventHandlerSet Handlers)>();
        var floatExpressions = new List<ItemFloatExpression>();
        var columns = new List<ColumnInfo>();
        var multiLabels = new List<string?>();
        var multiStrings = new List<string?>();
        var multiValues = new List<float>();
        ItemDefType type = ItemDefType.Text;
        ItemFlags itemFlags = 0;
        ItemHorizontalAlignment align = 0;
        ItemFont font = 0;
        ItemTextStyle textStyle = 0;
        ItemDvarFlags dvarFlags = 0;
        int textAlign = 0, gameIndex = 0, gameMode = 0;
        int letterTime = 0, decayStart = 0, decayDuration = 0;
        int maxChars = 0, maxPaintChars = 0, maxCharsGotoNext = 0;
        int elementStyle = 0, notSelectable = 0, noScrollbars = 0, usePaging = 0;
        int spacing = 0, speed = 0, feed = 0;
        float textAlignX = 0, textAlignY = 0, textScale = 0, feeder = 0;
        float elementWidth = 0, elementHeight = 0;
        float defVal = -1, minVal = -1, maxVal = -1;
        string? itemText = null, dvar = null, dvarTest = null, enableDvar = null;
        string? focusSound = null, selectIcon = null, dvarEnumName = null;
        Vec4 glowColor = new(), selectBorder = new();
        Statement? visible = null, disabled = null, textExpression = null, materialExpression = null;
        MenuEventHandlerSet? onFocus = null, leaveFocus = null, mouseEnter = null, mouseExit = null;
        MenuEventHandlerSet? mouseEnterText = null, mouseExitText = null, action = null, accept = null, doubleClick = null;
        bool decodeEffect = false;
        bool hasListProperties = false, hasEditProperties = false, hasMultiProperties = false;
        bool hasTickerProperties = false, hasEnumProperties = false;
        while (!Take("}"))
        {
            if (Take(";")) continue;
            string property = Word().ToLowerInvariant();
            if (ReadWindowProperty(property, window))
                continue;
            switch (property)
            {
                case "text": itemText = Text(); break;
                case "textsavegame": itemFlags |= ItemFlags.SaveGameText; break;
                case "textcinematicsubtitle": itemFlags |= ItemFlags.CinematicSubtitle; break;
                case "type": type = ReadEnum<ItemDefType>("item type"); break;
                case "visible":
                    if (AtNumber)
                    {
                        if (Number() != 0) window.DynamicFlags |= WindowDynamicFlags.WINDOW_DYNAMIC_VISIBLE;
                    }
                    else visible = ReadStatementProperty(when: true);
                    break;
                case "disabled": disabled = ReadStatementProperty(when: true); break;
                case "align": align = ReadEnum<ItemHorizontalAlignment>("item alignment"); break;
                case "textalign": textAlign = Integer(); break;
                case "textalignx": textAlignX = Number(); break;
                case "textaligny": textAlignY = Number(); break;
                case "textscale": textScale = Number(); break;
                case "textstyle": textStyle = ReadEnum<ItemTextStyle>("text style"); break;
                case "textfont": font = ReadEnum<ItemFont>("item font"); break;
                case "glowcolor": glowColor = ReadColor(); break;
                case "onfocus": onFocus = ReadHandlers(); break;
                case "leavefocus": leaveFocus = ReadHandlers(); break;
                case "mouseenter": mouseEnter = ReadHandlers(); break;
                case "mouseexit": mouseExit = ReadHandlers(); break;
                case "mouseentertext": mouseEnterText = ReadHandlers(); break;
                case "mouseexittext": mouseExitText = ReadHandlers(); break;
                case "action": action = ReadHandlers(); break;
                case "accept": accept = ReadHandlers(); break;
                case "focussound": focusSound = Text(); break;
                case "dvartest": dvarTest = Text(); break;
                case "enabledvar": dvarFlags = ItemDvarFlags.Enable; enableDvar = ReadDvarCondition(); break;
                case "disabledvar": dvarFlags = ItemDvarFlags.Disable; enableDvar = ReadDvarCondition(); break;
                case "showdvar": dvarFlags = ItemDvarFlags.Show; enableDvar = ReadDvarCondition(); break;
                case "hidedvar": dvarFlags = ItemDvarFlags.Hide; enableDvar = ReadDvarCondition(); break;
                case "focusdvar": dvarFlags = ItemDvarFlags.Focus; enableDvar = ReadDvarCondition(); break;
                case "execkey": case "execkeyint": ReadKey(property, keys); break;
                case "exp":
                    string field = Word().ToLowerInvariant();
                    if (field == "text") textExpression = ReadStatementProperty();
                    else if (field == "material") materialExpression = ReadStatementProperty();
                    else
                    {
                        string component = Word().ToLowerInvariant();
                        floatExpressions.Add(new ItemFloatExpression
                        {
                            Target = FloatTarget(field, component),
                            Expression = new XPointer<Statement>(-1),
                            Statement = ReadStatementProperty()
                        });
                    }
                    break;
                case "gamemsgwindowindex": gameIndex = Integer(); break;
                case "gamemsgwindowmode": gameMode = Integer(); break;
                case "decodeeffect": decodeEffect = true; letterTime = Integer(); decayStart = Integer(); decayDuration = Integer(); break;
                case "notselectable": hasListProperties = true; notSelectable = 1; break;
                case "noscrollbars": hasListProperties = true; noScrollbars = 1; break;
                case "usepaging": hasListProperties = true; usePaging = 1; break;
                case "elementwidth": hasListProperties = true; elementWidth = Number(); break;
                case "elementheight": hasListProperties = true; elementHeight = Number(); break;
                case "feeder": hasListProperties = true; feeder = Number(); break;
                case "elementtype": hasListProperties = true; elementStyle = Integer(); break;
                case "columns":
                    hasListProperties = true;
                    int count = Integer();
                    if (count is < 0 or > 16) throw Error("List columns count must be in [0, 16].");
                    for (int i = 0; i < count; i++)
                        columns.Add(new ColumnInfo { Pos = Integer(), Width = Integer(), MaxChars = Integer(), Alignment = Integer() });
                    break;
                case "doubleclick": hasListProperties = true; doubleClick = ReadHandlers(); break;
                case "selectborder": hasListProperties = true; selectBorder = ReadColor(); break;
                case "selecticon": hasListProperties = true; selectIcon = Text(); break;
                case "dvar": dvar = Text(); break;
                case "dvarfloat":
                    hasEditProperties = true; dvar = Text(); defVal = Number(); minVal = Number(); maxVal = Number(); break;
                case "maxchars": hasEditProperties = true; maxChars = Integer(); break;
                case "maxcharsgotonext": hasEditProperties = true; maxCharsGotoNext = 1; break;
                case "maxpaintchars": hasEditProperties = true; maxPaintChars = Integer(); break;
                case "dvarstrlist": case "dvarfloatlist":
                    hasMultiProperties = true;
                    ReadMultiList(property == "dvarstrlist", multiLabels, multiStrings, multiValues);
                    break;
                case "dvarenumlist": hasEnumProperties = true; dvarEnumName = Text(); break;
                case "spacing": hasTickerProperties = true; spacing = Integer(); break;
                case "speed": hasTickerProperties = true; speed = Integer(); break;
                case "newsfeed": hasTickerProperties = true; feed = Integer(); break;
                default: throw Error($"Unknown itemDef property '{property}'.");
            }
        }

        bool editType = type is ItemDefType.Text or ItemDefType.EditField or ItemDefType.NumericField or
            ItemDefType.Slider or ItemDefType.YesNo or ItemDefType.Bind or ItemDefType.Validation or
            ItemDefType.DecimalField or ItemDefType.UpDown or ItemDefType.EmailField or ItemDefType.PassWordField;
        if (hasListProperties && type != ItemDefType.ListBox || hasEditProperties && !editType ||
            hasMultiProperties && type != ItemDefType.Multi || hasEnumProperties && type != ItemDefType.DvarEnum ||
            hasTickerProperties && type != ItemDefType.NewsTicker)
            throw Error($"Item '{window.Name}' has properties incompatible with type {type}.");

        EditFieldDef? edit = editType ? new EditFieldDef
        {
            DefVal = defVal, MinVal = minVal, MaxVal = maxVal,
            MaxChars = maxChars, MaxCharsGotoNext = maxCharsGotoNext, MaxPaintChars = maxPaintChars
        } : null;
        ListBoxDef? list = type == ItemDefType.ListBox ? new ListBoxDef
        {
            StartPos = new int[4], EndPos = new int[4],
            ElementWidth = elementWidth, ElementHeight = elementHeight, ElementStyle = elementStyle,
            NumColumns = columns.Count,
            ColumnInfo = columns.Concat(Enumerable.Range(0, 16 - columns.Count).Select(_ => new ColumnInfo())).ToArray(),
            NotSelectable = notSelectable, NoScrollbars = noScrollbars, UsePaging = usePaging,
            DoubleClick = Inline(doubleClick), DoubleClickSet = doubleClick,
            SelectBorder = selectBorder, SelectIcon = default,
            SelectIconMaterialName = selectIcon
        } : null;
        MultiDef? multi = type == ItemDefType.Multi ? new MultiDef
        {
            Count = multiLabels.Count, StrDef = multiStrings.Count > 0 ? 1 : 0,
            DvarList = multiLabels.Select(value => Inline(value)).Concat(Enumerable.Repeat(default(XPointer<string>), MultiDef.EntryCapacity - multiLabels.Count)).ToArray(),
            DvarListStrings = multiLabels.Concat(Enumerable.Repeat<string?>(null, MultiDef.EntryCapacity - multiLabels.Count)).ToArray(),
            DvarStr = multiStrings.Select(value => Inline(value)).Concat(Enumerable.Repeat(default(XPointer<string>), MultiDef.EntryCapacity - multiStrings.Count)).ToArray(),
            DvarStrStrings = multiStrings.Concat(Enumerable.Repeat<string?>(null, MultiDef.EntryCapacity - multiStrings.Count)).ToArray(),
            DvarValue = multiValues.Concat(Enumerable.Repeat(0f, MultiDef.EntryCapacity - multiValues.Count)).ToArray()
        } : null;
        NewsTickerDef? ticker = type == ItemDefType.NewsTicker ? new NewsTickerDef
        {
            Spacing = spacing, Speed = speed, FeedId = feed
        } : null;
        ItemDefDataValue data = type switch
        {
            _ when editType => new EditFieldItemDefData { EditFieldPointer = new XPointer<EditFieldDef>(-1) },
            ItemDefType.ListBox => new ListBoxItemDefData { ListBoxPointer = new XPointer<ListBoxDef>(-1) },
            ItemDefType.Multi => new MultiItemDefData { MultiPointer = new XPointer<MultiDef>(-1) },
            ItemDefType.DvarEnum => new DvarEnumItemDefData { DvarEnumNamePointer = Inline(dvarEnumName) },
            ItemDefType.NewsTicker => new NewsTickerItemDefData { NewsTickerPointer = new XPointer<NewsTickerDef>(-1) },
            ItemDefType.TextScroll => new TextScrollItemDefData { TextScrollPointer = new XPointer<TextScrollDef>(-1) },
            _ => new NoItemDefData()
        };
        ItemKeyHandler? keyHandlers = BuildKeys(keys);
        return new ItemDefAsset
        {
            Window = window.Build(isItem: true), TextRect = [new RectangleDef(), new RectangleDef(), new RectangleDef(), new RectangleDef()],
            Type = type, DataType = (int)type, Align = align, FontEnum = font,
            TextAlignMode = textAlign, TextAlignX = textAlignX, TextAlignY = textAlignY,
            TextScale = textScale, TextStyle = textStyle, GameMsgWindowIndex = gameIndex,
            GameMsgWindowMode = gameMode, Text = Inline(itemText), TextString = itemText,
            ItemFlags = itemFlags, Dvar = Inline(dvar), DvarString = dvar,
            DvarTest = Inline(dvarTest), DvarTestString = dvarTest,
            EnableDvar = Inline(enableDvar), EnableDvarString = enableDvar, DvarFlags = dvarFlags,
            FocusSound = default, FocusSoundName = focusSound,
            MouseEnterText = Inline(mouseEnterText), MouseEnterTextSet = mouseEnterText,
            MouseExitText = Inline(mouseExitText), MouseExitTextSet = mouseExitText,
            MouseEnter = Inline(mouseEnter), MouseEnterSet = mouseEnter,
            MouseExit = Inline(mouseExit), MouseExitSet = mouseExit,
            Action = Inline(action), ActionSet = action, Accept = Inline(accept), AcceptSet = accept,
            OnFocus = Inline(onFocus), OnFocusSet = onFocus,
            LeaveFocus = Inline(leaveFocus), LeaveFocusSet = leaveFocus,
            OnKey = Inline(keyHandlers), OnKeyHandler = keyHandlers,
            Special = feeder, CursorPos = new int[4], ImageTrack = (int)ImageTrackType.UI,
            TypeData = new ItemDefData { Value = data },
            EditField = edit, ListBox = list, Multi = multi, DvarEnumName = dvarEnumName,
            NewsTicker = ticker, TextScroll = type == ItemDefType.TextScroll ? new TextScrollDef() : null,
            FloatExpressionCount = floatExpressions.Count,
            FloatExpressions = floatExpressions.Count > 0 ? new XPointer<ItemFloatExpression[]>(-1) : default,
            LoadedFloatExpressions = floatExpressions,
            VisibleExpression = Inline(visible), VisibleStatement = visible,
            DisabledExpression = Inline(disabled), DisabledStatement = disabled,
            TextExpression = Inline(textExpression), TextStatement = textExpression,
            MaterialExpression = Inline(materialExpression), MaterialStatement = materialExpression,
            GlowColor = glowColor, DecayActive = decodeEffect ? (byte)1 : (byte)0,
            FxLetterTime = letterTime, FxDecayStartTime = decayStart, FxDecayDuration = decayDuration
        };
    }

    private T ReadEnum<T>(string description) where T : struct, Enum
    {
        int value = Integer();
        T result = (T)Enum.ToObject(typeof(T), value);
        if (!Enum.IsDefined(result)) throw Error($"Unsupported {description} value {value}.");
        return result;
    }

    private Vec4 ReadColor() => new() { A = Number(), R = Number(), G = Number(), B = Number() };
    private static Vec4 OneColor() => new() { A = 1, R = 1, G = 1, B = 1 };

    private string ReadDvarCondition()
    {
        Expect("{");
        var words = new List<string>();
        while (!Take("}"))
        {
            if (Take(";")) continue;
            words.Add(QuoteScriptToken(Text()));
        }
        return string.Join(' ', words);
    }

    private void ReadMultiList(bool strings, List<string?> labels, List<string?> values, List<float> numbers)
    {
        Expect("{");
        while (!Take("}"))
        {
            if (labels.Count >= MultiDef.EntryCapacity)
                throw Error($"Multi list exceeds {MultiDef.EntryCapacity} entries.");
            labels.Add(Text());
            if (strings) values.Add(Text());
            else numbers.Add(Number());
        }
    }

    private ItemFloatExpressionTarget FloatTarget(string field, string component) =>
        (field, component) switch
        {
            ("rect", "x") => ItemFloatExpressionTarget.RectX,
            ("rect", "y") => ItemFloatExpressionTarget.RectY,
            ("rect", "w") => ItemFloatExpressionTarget.RectW,
            ("rect", "h") => ItemFloatExpressionTarget.RectH,
            ("forecolor", "r") => ItemFloatExpressionTarget.ForeColorR,
            ("forecolor", "g") => ItemFloatExpressionTarget.ForeColorG,
            ("forecolor", "b") => ItemFloatExpressionTarget.ForeColorB,
            ("forecolor", "rgb") => ItemFloatExpressionTarget.ForeColorRgb,
            ("forecolor", "a") => ItemFloatExpressionTarget.ForeColorA,
            ("glowcolor", "r") => ItemFloatExpressionTarget.GlowColorR,
            ("glowcolor", "g") => ItemFloatExpressionTarget.GlowColorG,
            ("glowcolor", "b") => ItemFloatExpressionTarget.GlowColorB,
            ("glowcolor", "rgb") => ItemFloatExpressionTarget.GlowColorRgb,
            ("glowcolor", "a") => ItemFloatExpressionTarget.GlowColorA,
            ("backcolor", "r") => ItemFloatExpressionTarget.BackColorR,
            ("backcolor", "g") => ItemFloatExpressionTarget.BackColorG,
            ("backcolor", "b") => ItemFloatExpressionTarget.BackColorB,
            ("backcolor", "rgb") => ItemFloatExpressionTarget.BackColorRgb,
            ("backcolor", "a") => ItemFloatExpressionTarget.BackColorA,
            _ => throw Error($"Unsupported item float expression target '{field} {component}'.")
        };
}
