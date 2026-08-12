using IW4.Assets.Assets.Menu;
using IW4.Studio.Documents;
using IW4.Studio.Documents.MenuEditing.Behavior;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>
/// Editor-facing validation with stable Menu-node paths. The linker remains
/// the final wire-contract validator; these diagnostics keep invalid staged
/// values addressable by the Properties UI before Save As.
/// </summary>
internal static class MenuEditorValidation
{
    public static IReadOnlyList<AssetValidationIssue> Validate(
        MenuEditorSnapshot menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        var issues = new List<AssetValidationIssue>();
        string menuPath = $"menu[{menu.Id}]";
        if (!menu.IsComplete)
            Error(menuPath, "The Menu definition is unresolved.", issues);

        ValidateSettings(menu.Settings, $"{menuPath}.settings", issues);
        ValidateWindow(menu.Window.Value, $"{menuPath}.window", issues);
        var behaviorValidator = new MenuItemBehaviorValidator(
            new MenuBehaviorExpressionCodec(menu.ExpressionSupport.Source));
        for (int index = 0; index < menu.Items.Count; index++)
        {
            MenuItemSnapshot item = menu.Items[index];
            string path = $"{menuPath}.items[{item.Id}]";
            if (!item.IsResolved)
            {
                Error(path, "The serialized item pointer has no detached definition.", issues);
                continue;
            }

            ValidateItem(item.Value, path, issues);
            foreach (MenuBehaviorValidationIssue issue in
                     behaviorValidator.Validate(
                         item.Behavior,
                         MenuBehaviorValidationMode.Imported))
            {
                string behaviorPath = issue.Path.StartsWith(
                    "item",
                    StringComparison.Ordinal)
                        ? path + issue.Path[4..]
                        : $"{path}.behavior.{issue.Path}";
                issues.Add(new AssetValidationIssue(
                    behaviorPath,
                    issue.Message,
                    issue.Severity == MenuBehaviorValidationSeverity.Error
                        ? AssetValidationSeverity.Error
                        : AssetValidationSeverity.Warning));
            }
        }

        return Array.AsReadOnly(issues.ToArray());
    }

    public static IReadOnlyList<AssetValidationIssue> Validate(
        MenuFileEditorSnapshot menuFile)
    {
        ArgumentNullException.ThrowIfNull(menuFile);
        var issues = new List<AssetValidationIssue>();
        String(menuFile.Name, "menuFile.name", issues);
        foreach (MenuFileRegistrationSnapshot registration in menuFile.Registrations)
        {
            string path = $"menuFile.registrations[{registration.Id}]";
            String(registration.Name, $"{path}.name", issues);
            if (registration.IsEditableDefinition && registration.Menu is null)
                Error(path, "The owned Menu registration has no detached definition.", issues);
            if (registration.Menu is { } menu)
                issues.AddRange(Validate(menu));
        }

        return Array.AsReadOnly(issues.ToArray());
    }

    private static void ValidateSettings(
        MenuSettingsValue value,
        string path,
        List<AssetValidationIssue> issues)
    {
        String(value.Font, $"{path}.font", issues);
        Binary(value.Fullscreen, $"{path}.fullscreen", issues);
        String(value.AllowedBinding, $"{path}.allowedBinding", issues);
        String(value.SoundName, $"{path}.soundName", issues);
        Fixed(value.CursorItems, 4, $"{path}.cursorItems", issues);
        Finite(value.FadeClamp, $"{path}.fadeClamp", issues);
        Finite(value.FadeAmount, $"{path}.fadeAmount", issues);
        Finite(value.FadeInAmount, $"{path}.fadeInAmount", issues);
        Finite(value.BlurRadius, $"{path}.blurRadius", issues);
        if (value.BlurRadius < 0)
            Error($"{path}.blurRadius", "Blur radius cannot be negative.", issues);
        Color(value.FocusColor, $"{path}.focusColor", issues);
        Transitions(value.ScaleTransitions, $"{path}.scaleTransitions", issues);
        Transitions(value.AlphaTransitions, $"{path}.alphaTransitions", issues);
        Transitions(value.XTransitions, $"{path}.xTransitions", issues);
        Transitions(value.YTransitions, $"{path}.yTransitions", issues);
    }

    private static void ValidateWindow(
        MenuWindowValue value,
        string path,
        List<AssetValidationIssue> issues)
    {
        String(value.Name, $"{path}.name", issues);
        String(value.Group, $"{path}.group", issues);
        String(value.BackgroundMaterialName, $"{path}.background", issues);
        Rect(value.Rect, $"{path}.rect", issues);
        Rect(value.RectClient, $"{path}.rectClient", issues);
        if (!Enum.IsDefined(value.Style))
            Error($"{path}.style", "Unknown Window style discriminator.", issues);
        if (!Enum.IsDefined(value.Border))
            Error($"{path}.border", "Unknown Window border discriminator.", issues);
        Fixed(value.DynamicFlags, 4, $"{path}.dynamicFlags", issues);
        Finite(value.BorderSize, $"{path}.borderSize", issues);
        Color(value.ForeColor, $"{path}.foreColor", issues);
        Color(value.BackColor, $"{path}.backColor", issues);
        Color(value.BorderColor, $"{path}.borderColor", issues);
        Color(value.OutlineColor, $"{path}.outlineColor", issues);
        Color(value.DisableColor, $"{path}.disableColor", issues);
    }

    private static void ValidateItem(
        MenuItemValue value,
        string path,
        List<AssetValidationIssue> issues)
    {
        ValidateWindow(value.Window, $"{path}.window", issues);
        Fixed(value.TextRectangles, 4, $"{path}.textRectangles", issues);
        foreach ((MenuRectangleValue rectangle, int index) in
                 value.TextRectangles.Select((rectangle, index) => (rectangle, index)))
        {
            Rect(rectangle, $"{path}.textRectangles[{index}]", issues);
        }
        Fixed(value.CursorPositions, 4, $"{path}.cursorPositions", issues);
        if (!Enum.IsDefined(value.Type))
            Error($"{path}.type", "Unknown item type discriminator.", issues);
        if (unchecked((uint)value.TextAlignMode) >= 16u ||
            (value.TextAlignMode & 3) == 3)
        {
            Error(
                $"{path}.textAlignMode",
                "Text alignment must use a valid horizontal and vertical mode.",
                issues);
        }
        if (value.Type == ItemDefType.GameMessageWindow)
        {
            Range(
                value.GameMessageWindowIndex,
                0,
                3,
                $"{path}.gameMessageWindowIndex",
                "Game-message window index",
                issues);
            Range(
                value.GameMessageWindowMode,
                0,
                3,
                $"{path}.gameMessageWindowMode",
                "Game-message window mode",
                issues);
        }
        String(value.Text, $"{path}.text", issues);
        String(value.Dvar, $"{path}.dvar", issues);
        String(value.DvarTest, $"{path}.dvarTest", issues);
        String(value.EnableDvar, $"{path}.enableDvar", issues);
        String(value.FocusSoundName, $"{path}.focusSound", issues);
        Finite(value.TextAlignX, $"{path}.textAlignX", issues);
        Finite(value.TextAlignY, $"{path}.textAlignY", issues);
        Finite(value.TextScale, $"{path}.textScale", issues);
        Finite(value.Special, $"{path}.special", issues);
        Color(value.GlowColor, $"{path}.glowColor", issues);
        ValidatePayload(value.Type, value.Payload, $"{path}.payload", issues);
    }

    private static void ValidatePayload(
        ItemDefType type,
        MenuItemPayloadValue payload,
        string path,
        List<AssetValidationIssue> issues)
    {
        bool editType = type is
            ItemDefType.Text or
            ItemDefType.EditField or
            ItemDefType.NumericField or
            ItemDefType.Slider or
            ItemDefType.YesNo or
            ItemDefType.Bind or
            ItemDefType.Validation or
            ItemDefType.DecimalField or
            ItemDefType.UpDown or
            ItemDefType.EmailField or
            ItemDefType.PassWordField;
        bool matches = payload switch
        {
            MenuEditFieldPayloadValue => editType,
            MenuListBoxPayloadValue => type == ItemDefType.ListBox,
            MenuMultiPayloadValue => type == ItemDefType.Multi,
            MenuDvarEnumPayloadValue => type == ItemDefType.DvarEnum,
            MenuNewsTickerPayloadValue => type == ItemDefType.NewsTicker,
            MenuTextScrollPayloadValue => type == ItemDefType.TextScroll,
            MenuNoItemPayloadValue => Enum.IsDefined(type),
            _ => false
        };
        if (!matches)
            Error(path, "Item payload does not match the selected item type.", issues);

        switch (payload)
        {
            case MenuEditFieldPayloadValue edit:
                Finite(edit.MinValue, $"{path}.minValue", issues);
                Finite(edit.MaxValue, $"{path}.maxValue", issues);
                Finite(edit.DefaultValue, $"{path}.defaultValue", issues);
                Finite(edit.Range, $"{path}.range", issues);
                Binary(edit.MaxCharsGotoNext, $"{path}.maxCharsGotoNext", issues);
                break;
            case MenuListBoxPayloadValue list:
                Finite(list.ElementWidth, $"{path}.elementWidth", issues);
                Finite(list.ElementHeight, $"{path}.elementHeight", issues);
                Fixed(list.Columns, 16, $"{path}.columns", issues);
                if (list.NumColumns is < 0 or > 16)
                    Error($"{path}.numColumns", "ListBox column count must be in [0,16].", issues);
                Color(list.SelectBorder, $"{path}.selectBorder", issues);
                String(list.SelectIconMaterialName, $"{path}.selectIcon", issues);
                Binary(list.UsePaging, $"{path}.usePaging", issues);
                break;
            case MenuMultiPayloadValue multi:
                Fixed(multi.Entries, MultiDef.EntryCapacity, $"{path}.entries", issues);
                if (multi.Count is < 0 or > MultiDef.EntryCapacity)
                    Error($"{path}.count", "Multi entry count must be in [0,32].", issues);
                Binary(multi.StringDefinition, $"{path}.stringDefinition", issues);
                foreach ((MenuMultiEntryValue entry, int index) in
                         multi.Entries.Select((entry, index) => (entry, index)))
                {
                    String(entry.DvarListValue, $"{path}.entries[{index}].dvarList", issues);
                    String(entry.DvarStringValue, $"{path}.entries[{index}].dvarString", issues);
                    Finite(entry.NumericValue, $"{path}.entries[{index}].value", issues);
                }
                break;
            case MenuDvarEnumPayloadValue dvar:
                String(dvar.DvarName, $"{path}.dvarName", issues);
                break;
            case MenuNewsTickerPayloadValue ticker:
                Finite(ticker.X, $"{path}.x", issues);
                break;
        }
    }

    private static void Transitions(
        IReadOnlyList<MenuTransitionValue> values,
        string path,
        List<AssetValidationIssue> issues)
    {
        Fixed(values, 4, path, issues);
        foreach ((MenuTransitionValue value, int index) in
                 values.Select((value, index) => (value, index)))
        {
            if (!Enum.IsDefined(value.TransitionType))
                Error($"{path}[{index}].type", "Unknown transition type.", issues);
            if (!Enum.IsDefined(value.EndTriggerType))
                Error($"{path}[{index}].endTrigger", "Unknown transition end trigger.", issues);
            Finite(value.StartValue, $"{path}[{index}].startValue", issues);
            Finite(value.EndValue, $"{path}[{index}].endValue", issues);
            Finite(value.Time, $"{path}[{index}].time", issues);
        }
    }

    private static void Rect(
        MenuRectangleValue value,
        string path,
        List<AssetValidationIssue> issues)
    {
        Finite(value.X, $"{path}.x", issues);
        Finite(value.Y, $"{path}.y", issues);
        Finite(value.Width, $"{path}.width", issues);
        Finite(value.Height, $"{path}.height", issues);
        if (!Enum.IsDefined(value.HorizontalAlignment))
            Error($"{path}.horizontalAlignment", "Unknown horizontal alignment.", issues);
        if (!Enum.IsDefined(value.VerticalAlignment))
            Error($"{path}.verticalAlignment", "Unknown vertical alignment.", issues);
    }

    private static void Color(
        MenuColorValue value,
        string path,
        List<AssetValidationIssue> issues)
    {
        Finite(value.A, $"{path}.a", issues);
        Finite(value.R, $"{path}.r", issues);
        Finite(value.G, $"{path}.g", issues);
        Finite(value.B, $"{path}.b", issues);
    }

    private static void Fixed<T>(
        IReadOnlyList<T> values,
        int count,
        string path,
        List<AssetValidationIssue> issues)
    {
        if (values.Count != count)
            Error(path, $"Exactly {count} values are required.", issues);
    }

    private static void Finite(
        float value,
        string path,
        List<AssetValidationIssue> issues)
    {
        if (!float.IsFinite(value))
            Error(path, "Value must be finite.", issues);
    }

    private static void Binary(
        int value,
        string path,
        List<AssetValidationIssue> issues)
    {
        if (value is not 0 and not 1)
            Error(path, "Value must be 0 or 1.", issues);
    }

    private static void Range(
        int value,
        int minimum,
        int maximum,
        string path,
        string label,
        List<AssetValidationIssue> issues)
    {
        if (value < minimum || value > maximum)
            Error(path, $"{label} must be in [{minimum},{maximum}].", issues);
    }

    private static void String(
        string? value,
        string path,
        List<AssetValidationIssue> issues)
    {
        if (value is null)
            return;
        if (value.Contains('\0'))
            Error(path, "XString values cannot contain embedded null characters.", issues);
        if (value.Any(character => character > byte.MaxValue))
            Error(path, "XString values must be representable in Latin-1.", issues);
    }

    private static void Error(
        string path,
        string message,
        List<AssetValidationIssue> issues) =>
        issues.Add(new AssetValidationIssue(
            path,
            message,
            AssetValidationSeverity.Error));
}
