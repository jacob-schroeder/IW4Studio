using IW4.Assets.Assets.Menu;
using IW4.Assets.Math;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>
/// Emits a MenuFile root and its ordered nested Menu links. Inline and insert
/// links own a detached incoming Menu body; packed links resolve through a
/// previously published logical Menu owner or materialize an external Menu
/// registration through the shared nested-XAsset path.
/// </summary>
public sealed class MenuFileBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.MenuFile;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IMenuFileBuildData data)
        {
            diagnostics.Add(Error("body", "MenuFile build data does not implement IMenuFileBuildData.", rowIndex));
            return diagnostics;
        }
        ValidateString(data.Name, "name", diagnostics, rowIndex);
        if (string.IsNullOrEmpty(data.Name))
        {
            diagnostics.Add(Error(
                "name",
                "MenuFile requires a non-empty asset name.",
                rowIndex));
        }
        IReadOnlyList<NestedXAssetBuildLink> links = data.MenuLinks;
        for (int index = 0; index < links.Count; index++)
        {
            NestedXAssetBuildLink? link = links[index];
            string path = $"menuLinks[{index}]";
            if (link is null)
            {
                diagnostics.Add(Error(
                    path,
                    "MenuFile cannot contain a null Menu link.",
                    rowIndex));
                continue;
            }

            diagnostics.AddRange(NestedXAssetEmission.Validate(
                link,
                XAssetType.Menu,
                path,
                rowIndex,
                AssetType));

            string serializedName = link.Reference.OriginalSerializedName;
            if (serializedName.Length == 0)
            {
                diagnostics.Add(Error(
                    $"{path}.reference",
                    "Nested Menu identity cannot be empty.",
                    rowIndex));
            }

            if (link.IncomingDefinition is null)
                continue;

            if (link.IncomingDefinition is not IMenuBuildData menu)
            {
                diagnostics.Add(Error(
                    $"{path}.incomingDefinition",
                    "Nested Menu definition does not implement IMenuBuildData.",
                    rowIndex));
                continue;
            }

            if (!menu.IsComplete)
            {
                diagnostics.Add(Error(
                    $"{path}.incomingDefinition",
                    "Nested Menu definition is incomplete.",
                    rowIndex));
            }

            string? definitionName = menu.Definition?.Window.Name;
            if (link.Reference.IsExternalReference ||
                definitionName?.StartsWith(",", StringComparison.Ordinal) == true)
            {
                diagnostics.Add(Error(
                    $"{path}.reference",
                    "An owned inline/insert Menu definition cannot use a comma-prefixed external-reference identity.",
                    rowIndex));
            }
            if (definitionName is not null &&
                !SameLogicalName(serializedName, definitionName))
            {
                diagnostics.Add(Error(
                    $"{path}.reference",
                    "Nested Menu reference does not match its incoming definition name.",
                    rowIndex));
            }

            diagnostics.AddRange(MenuBodyEmitter.ValidateDefinition(
                menu,
                $"{path}.incomingDefinition",
                rowIndex));
        }
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IMenuFileBuildData data = (IMenuFileBuildData)buildData;
        IReadOnlyList<NestedXAssetBuildLink> menuLinks = data.MenuLinks;
        var all = new List<EmissionBlockSegment>();

        plan.Push(XFileBlockType.TEMP);
        EmissionAddress rootAddress = plan.Allocate(MenuFileAsset.SerializedSize, 4);
        plan.Push(XFileBlockType.LARGE);
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, all, plan.StringAliases);
        EmissionAddress? menuTableAddress = menuLinks.Count == 0
            ? null
            : plan.Allocate(checked(menuLinks.Count * sizeof(int)), 4);
        var menus = new NestedXAssetPlan[menuLinks.Count];
        for (int index = 0; index < menuLinks.Count; index++)
        {
            EmissionAddress pointerCell = new(
                menuTableAddress!.Value.Block,
                checked(menuTableAddress.Value.Offset + index * sizeof(int)));
            menus[index] = NestedXAssetEmission.Plan(
                menuLinks[index],
                plan,
                all,
                pointerCell,
                "MenuFile");
        }
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        if (menuTableAddress is { } tableAddress)
        {
            var tableWriter = new XSourceWriter();
            foreach (NestedXAssetPlan menu in menus)
                tableWriter.WriteInt32(menu.PointerRaw);
            all.Add(new EmissionBlockSegment(tableAddress, tableWriter.ToArray()));
        }

        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        rootWriter.WriteInt32(menuLinks.Count);
        rootWriter.WriteInt32(menuTableAddress is null ? 0 : -1);
        Exact(rootWriter, MenuFileAsset.SerializedSize, "MenuFile");
        EmissionBlockSegment root = new(rootAddress, rootWriter.ToArray());
        all.Add(root);

        var source = new List<EmissionBlockSegment> { root };
        AddNewSegments(source, all, name);
        if (menuTableAddress is { } address)
            source.Add(all.Single(segment => segment.Address == address));
        foreach (NestedXAssetPlan menu in menus)
            source.AddRange(menu.Source);
        return new AssetBodyEmission(AssetType, rootAddress, all, source);
    }

    private static bool SameLogicalName(string left, string right) =>
        string.Equals(
            NormalizeLogicalName(left),
            NormalizeLogicalName(right),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLogicalName(string value) =>
        value.TrimStart(',').Replace('\\', '/');

    private static void ValidateString(string? value, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        if (value is not null && !AssetBodyEmitterHelpers.IsLatin1CString(value))
            diagnostics.Add(Error(path, "XString must be a Latin-1 C string.", rowIndex));
    }

    private static void AddNewSegments(List<EmissionBlockSegment> source, List<EmissionBlockSegment> all, PlannedString? value)
    {
        if (value is null || value.Value.IsExistingMaterialization)
            return;
        source.Add(all.Single(segment => segment.Address == value.Value.Address));
    }

    private static void Exact(XSourceWriter writer, int size, string name)
    {
        if (writer.Position != size)
            throw new InvalidDataException($"{name} root emission produced 0x{writer.Position:X} bytes instead of 0x{size:X}.");
    }

    private static EmissionError Error(string path, string message, int? rowIndex) =>
        new(path, message, rowIndex, XAssetType.MenuFile);
}

/// <summary>Top-level Menu uses the same body contract as a nested MenuFile
/// child, but its root itself is the XAsset row payload.</summary>
public sealed class MenuBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.Menu;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IMenuBuildData data)
        {
            diagnostics.Add(new EmissionError("body", "Menu build data does not implement IMenuBuildData.", rowIndex, AssetType));
            return diagnostics;
        }
        if (!data.IsComplete)
            diagnostics.Add(new EmissionError("definition", "Menu definition was unresolved at capture time and cannot be emitted.", rowIndex, AssetType));
        diagnostics.AddRange(ValidateDefinition(data, "definition", rowIndex));
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IMenuBuildData data = (IMenuBuildData)buildData;
        var all = new List<EmissionBlockSegment>();
        MenuPlan menu = PlanDefinition(data.Definition, plan, all);
        EmissionBlockSegment root = menu.Root
            ?? throw new InvalidDataException("A top-level Menu cannot be emitted as a nested alias cell.");
        return new AssetBodyEmission(AssetType, root.Address, all, menu.Source);
    }

    internal static IReadOnlyList<EmissionError> ValidateDefinition(IMenuBuildData? data, string path, int? rowIndex) =>
        data is null
            ? [Error(path, "Menu definition is null.", rowIndex)]
            : ValidateDefinition(data.Definition, path, rowIndex);

    internal static IReadOnlyList<EmissionError> ValidateDefinition(
        MenuDefAsset? definition,
        string path,
        int? rowIndex)
    {
        var diagnostics = new List<EmissionError>();
        if (definition is null)
        {
            diagnostics.Add(Error(path, "Menu definition is null.", rowIndex));
            return diagnostics;
        }
        ValidateWindow(
            definition.Window,
            $"{path}.window",
            diagnostics,
            rowIndex);
        if (string.IsNullOrEmpty(definition.Window.Name))
        {
            diagnostics.Add(Error(
                $"{path}.window.name",
                "Menu requires a non-empty asset name.",
                rowIndex));
        }
        ValidateString(definition.Font, $"{path}.font", diagnostics, rowIndex);
        ValidateString(definition.AllowedBindingString, $"{path}.allowedBinding", diagnostics, rowIndex);
        ValidateString(definition.SoundNameString, $"{path}.soundName", diagnostics, rowIndex);
        if (definition.Fullscreen is not 0 and not 1)
            diagnostics.Add(Error($"{path}.fullscreen", "Value must be 0 or 1.", rowIndex));
        if (definition.CursorItems.Count != 4)
            diagnostics.Add(Error($"{path}.cursorItems", "Menu requires exactly four cursor items.", rowIndex));
        foreach ((string member, float number) in new[]
                 {
                     ("fadeClamp", definition.FadeClamp),
                     ("fadeAmount", definition.FadeAmount),
                     ("fadeInAmount", definition.FadeInAmount),
                     ("blurRadius", definition.BlurRadius)
                 })
        {
            if (!float.IsFinite(number))
                diagnostics.Add(Error($"{path}.{member}", "Value must be finite.", rowIndex));
        }
        if (definition.BlurRadius < 0)
            diagnostics.Add(Error($"{path}.blurRadius", "Blur radius cannot be negative.", rowIndex));
        ValidateVec4(definition.FocusColor, $"{path}.focusColor", diagnostics, rowIndex);
        ValidateTransitions(definition.ScaleTransitions, $"{path}.scaleTransitions", diagnostics, rowIndex);
        ValidateTransitions(definition.AlphaTransitions, $"{path}.alphaTransitions", diagnostics, rowIndex);
        ValidateTransitions(definition.XTransitions, $"{path}.xTransitions", diagnostics, rowIndex);
        ValidateTransitions(definition.YTransitions, $"{path}.yTransitions", diagnostics, rowIndex);
        if (definition.ItemCount < 0 || definition.ItemCount != definition.Items.Count)
            diagnostics.Add(Error($"{path}.items", "Item count must equal the detached item-reference table.", rowIndex));
        RequireDetached(definition.OnOpen.Raw, definition.OnOpenSet, $"{path}.onOpen", diagnostics, rowIndex);
        RequireDetached(definition.OnCloseRequest.Raw, definition.OnCloseRequestSet, $"{path}.onCloseRequest", diagnostics, rowIndex);
        RequireDetached(definition.OnClose.Raw, definition.OnCloseSet, $"{path}.onClose", diagnostics, rowIndex);
        RequireDetached(definition.OnEsc.Raw, definition.OnEscSet, $"{path}.onEsc", diagnostics, rowIndex);
        RequireDetached(definition.ExecKeys.Raw, definition.ExecKeyHandler, $"{path}.execKeys", diagnostics, rowIndex);
        RequireDetached(definition.VisibleExpression.Raw, definition.VisibleStatement, $"{path}.visibleExpression", diagnostics, rowIndex);
        RequireDetached(definition.RectXExpression.Raw, definition.RectXStatement, $"{path}.rectXExpression", diagnostics, rowIndex);
        RequireDetached(definition.RectYExpression.Raw, definition.RectYStatement, $"{path}.rectYExpression", diagnostics, rowIndex);
        RequireDetached(definition.RectWExpression.Raw, definition.RectWStatement, $"{path}.rectWExpression", diagnostics, rowIndex);
        RequireDetached(definition.RectHExpression.Raw, definition.RectHStatement, $"{path}.rectHExpression", diagnostics, rowIndex);
        RequireDetached(definition.ExpressionData.Raw, definition.ExpressionDataValue, $"{path}.expressionData", diagnostics, rowIndex);
        if (definition.ItemsPointer.Raw != 0 && definition.Items.Any(item => item.Item is null))
            diagnostics.Add(Error($"{path}.items", "A non-null item pointer has no detached item graph.", rowIndex));
        diagnostics.AddRange(MenuGraphValidator.Validate(definition, path, rowIndex));
        return diagnostics;
    }

    internal static MenuPlan PlanDefinition(
        MenuDefAsset definition,
        EmissionPlan plan,
        List<EmissionBlockSegment> all) =>
        new MenuGraphPlanner(plan, all).PlanMenu(definition);

    private static void ValidateWindow(
        WindowDef value,
        string path,
        List<EmissionError> diagnostics,
        int? rowIndex)
    {
        ValidateString(value.Name, $"{path}.name", diagnostics, rowIndex); ValidateString(value.Group, $"{path}.group", diagnostics, rowIndex);
        ValidateString(
            value.BackgroundMaterialName,
            $"{path}.background",
            diagnostics,
            rowIndex);
        if (value.BackgroundMaterialName is { Length: 0 })
            diagnostics.Add(Error($"{path}.background", "Material identity cannot be empty.", rowIndex));
        if (value.Background.Raw != 0 && value.BackgroundMaterialName is null)
            diagnostics.Add(Error($"{path}.background", "Window background has no captured symbolic Material identity.", rowIndex));
        if (value.DynamicFlags.Count != 4)
            diagnostics.Add(Error($"{path}.dynamicFlags", "Window requires exactly four local-client dynamic flag values.", rowIndex));
        ValidateRectangle(value.Rect, $"{path}.rect", diagnostics, rowIndex); ValidateRectangle(value.RectClient, $"{path}.rectClient", diagnostics, rowIndex);
        ValidateVec4(value.ForeColor, $"{path}.foreColor", diagnostics, rowIndex); ValidateVec4(value.BackColor, $"{path}.backColor", diagnostics, rowIndex); ValidateVec4(value.BorderColor, $"{path}.borderColor", diagnostics, rowIndex); ValidateVec4(value.OutlineColor, $"{path}.outlineColor", diagnostics, rowIndex); ValidateVec4(value.DisableColor, $"{path}.disableColor", diagnostics, rowIndex);
        if (!float.IsFinite(value.BorderSize)) diagnostics.Add(Error($"{path}.borderSize", "Value must be finite.", rowIndex));
    }

    private static void ValidateRectangle(RectangleDef value, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        if (!Enum.IsDefined(value.HorzAlign)) diagnostics.Add(Error($"{path}.horzAlign", "Invalid horizontal alignment discriminator.", rowIndex));
        if (!Enum.IsDefined(value.VertAlign)) diagnostics.Add(Error($"{path}.vertAlign", "Invalid vertical alignment discriminator.", rowIndex));
        foreach ((string member, float number) in new[] { ("x", value.X), ("y", value.Y), ("w", value.W), ("h", value.H) })
            if (!float.IsFinite(number)) diagnostics.Add(Error($"{path}.{member}", "Value must be finite.", rowIndex));
    }

    private static void ValidateVec4(Vec4 value, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        foreach ((string member, float number) in new[] { ("a", value.A), ("r", value.R), ("g", value.G), ("b", value.B) })
            if (!float.IsFinite(number)) diagnostics.Add(Error($"{path}.{member}", "Value must be finite.", rowIndex));
    }

    private static void ValidateTransitions(IReadOnlyList<MenuTransition> values, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        if (values.Count != 4) { diagnostics.Add(Error(path, "Menu transition groups require exactly four values.", rowIndex)); return; }
        for (int index = 0; index < values.Count; index++)
        {
            MenuTransition value = values[index];
            if (!Enum.IsDefined(value.TransitionType)) diagnostics.Add(Error($"{path}[{index}].transitionType", "Invalid transition discriminator.", rowIndex));
            if (!Enum.IsDefined(value.EndTriggerType)) diagnostics.Add(Error($"{path}[{index}].endTriggerType", "Invalid transition end-trigger discriminator.", rowIndex));
            foreach ((string member, float number) in new[] { ("startValue", value.StartValue), ("endValue", value.EndValue), ("time", value.Time) })
                if (!float.IsFinite(number)) diagnostics.Add(Error($"{path}[{index}].{member}", "Value must be finite.", rowIndex));
        }
    }

    private static void RequireDetached(int raw, object? child, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        if (raw != 0 && child is null)
            diagnostics.Add(Error(path, "A non-null serialized pointer has no detached child graph.", rowIndex));
    }

    private static void ValidateString(string? value, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        if (value is not null && !AssetBodyEmitterHelpers.IsLatin1CString(value))
            diagnostics.Add(Error(path, "XString must be a Latin-1 C string.", rowIndex));
    }

    private static EmissionError Error(string path, string message, int? rowIndex) => new(path, message, rowIndex, XAssetType.Menu);
}

internal sealed record MenuPlan(
    EmissionBlockSegment? Root,
    IReadOnlyList<EmissionBlockSegment> Source,
    int PointerRaw = -1);
internal sealed record ExternalPlan(
    EmissionBlockSegment? Root,
    IReadOnlyList<EmissionBlockSegment> Source,
    int PointerRaw = -1);
