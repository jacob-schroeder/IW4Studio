using IW4.Assets.Assets.Menu;
using IW4.Assets.Math;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>
/// Emits the fixed, self-contained MenuFile and Menu roots.  The deliberately
/// strict child checks are important: the current loader has not yet captured
/// symbolic identities for every material/sound child or all recursive UI
/// payloads, so an old runtime pointer must never leak into an output zone.
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
        IReadOnlyList<IMenuBuildData> menus = data.Menus;
        for (int index = 0; index < menus.Count; index++)
        {
            IMenuBuildData? menu = menus[index];
            if (menu is null)
            {
                diagnostics.Add(Error($"menus[{index}]", "MenuFile cannot contain a null menu entry.", rowIndex));
                continue;
            }
            if (menu.AssetType != XAssetType.Menu)
                diagnostics.Add(Error($"menus[{index}].type", "Nested MenuFile entries must declare the Menu asset type.", rowIndex));
            if (!menu.IsComplete)
                diagnostics.Add(Error($"menus[{index}]", "Nested Menu registration was unresolved at capture time and cannot be emitted.", rowIndex));
            diagnostics.AddRange(MenuBodyEmitter.ValidateDefinition(menu, $"menus[{index}]", rowIndex));
        }
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IMenuFileBuildData data = (IMenuFileBuildData)buildData;
        IReadOnlyList<IMenuBuildData> menuBuildData = data.Menus;
        var all = new List<EmissionBlockSegment>();

        plan.Push(XFileBlockType.TEMP);
        EmissionAddress rootAddress = plan.Allocate(MenuFileAsset.SerializedSize, 4);
        plan.Push(XFileBlockType.LARGE);
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, all, plan.StringAliases);
        EmissionAddress? menuTableAddress = menuBuildData.Count == 0 ? null : plan.Allocate(checked(menuBuildData.Count * sizeof(int)), 4);
        // One identity planner spans the complete MenuFile graph.  A child
        // shared by two nested menus is emitted once at its first source
        // occurrence and referenced by its packed block address thereafter.
        var menuPlanner = new MenuGraphPlanner(plan, all);
        var menus = new MenuPlan[menuBuildData.Count];
        for (int index = 0; index < menus.Length; index++)
        {
            EmissionAddress pointerCell = new(
                menuTableAddress!.Value.Block,
                checked(menuTableAddress.Value.Offset + index * sizeof(int)));
            string? logicalName = menuBuildData[index].Definition.Window.Name;
            string? aliasKey = logicalName is null
                ? null
                : $"{(int)XAssetType.Menu}\u0000{logicalName.TrimStart(',')}";
            if (aliasKey is not null &&
                plan.PersistentXAssetAliasCells.TryGetValue(aliasKey, out EmissionAddress existingCell))
            {
                menus[index] = new MenuPlan(null, [], existingCell.ToPackedPointer());
                continue;
            }

            menus[index] = menuPlanner.PlanMenu(
                menuBuildData[index].Definition,
                menuBuildData[index].References);
            if (aliasKey is not null)
                plan.PersistentXAssetAliasCells.TryAdd(aliasKey, pointerCell);
        }
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        if (menuTableAddress is { } tableAddress)
        {
            var tableWriter = new XSourceWriter();
            foreach (MenuPlan menu in menus)
                tableWriter.WriteInt32(menu.PointerRaw);
            all.Add(new EmissionBlockSegment(tableAddress, tableWriter.ToArray()));
        }

        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        rootWriter.WriteInt32(menuBuildData.Count);
        rootWriter.WriteInt32(menuTableAddress is null ? 0 : -1);
        Exact(rootWriter, MenuFileAsset.SerializedSize, "MenuFile");
        EmissionBlockSegment root = new(rootAddress, rootWriter.ToArray());
        all.Add(root);

        var source = new List<EmissionBlockSegment> { root };
        AddNewSegments(source, all, name);
        if (menuTableAddress is { } address)
            source.Add(all.Single(segment => segment.Address == address));
        foreach (MenuPlan menu in menus)
            source.AddRange(menu.Source);
        return new AssetBodyEmission(AssetType, rootAddress, all, source);
    }

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
        MenuPlan menu = PlanDefinition(data.Definition, data.References, plan, all);
        EmissionBlockSegment root = menu.Root
            ?? throw new InvalidDataException("A top-level Menu cannot be emitted as a nested alias cell.");
        return new AssetBodyEmission(AssetType, root.Address, all, menu.Source);
    }

    internal static IReadOnlyList<EmissionError> ValidateDefinition(IMenuBuildData? data, string path, int? rowIndex) =>
        data is null ? [Error(path, "Menu definition is null.", rowIndex)] : ValidateDefinition(data.Definition, data.References, path, rowIndex);

    internal static IReadOnlyList<EmissionError> ValidateDefinition(MenuDefAsset? definition, MenuReferenceBuildData references, string path, int? rowIndex)
    {
        var diagnostics = new List<EmissionError>();
        if (definition is null)
        {
            diagnostics.Add(Error(path, "Menu definition is null.", rowIndex));
            return diagnostics;
        }
        ValidateWindow(definition.Window, references, $"{path}.window", diagnostics, rowIndex);
        ValidateString(definition.Font, $"{path}.font", diagnostics, rowIndex);
        ValidateString(definition.AllowedBindingString, $"{path}.allowedBinding", diagnostics, rowIndex);
        ValidateString(definition.SoundNameString, $"{path}.soundName", diagnostics, rowIndex);
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

    internal static MenuPlan PlanDefinition(MenuDefAsset definition, MenuReferenceBuildData references, EmissionPlan plan, List<EmissionBlockSegment> all)
        => new MenuGraphPlanner(plan, all).PlanMenu(definition, references);

    private static void WriteWindow(XSourceWriter writer, WindowDef window, PlannedString? name, PlannedString? group, ExternalPlan? background)
    {
        writer.WriteInt32(Pointer(name));
        WriteRectangle(writer, window.Rect); WriteRectangle(writer, window.RectClient);
        writer.WriteInt32(Pointer(group)); writer.WriteInt32((int)window.Style); writer.WriteInt32((int)window.Border); writer.WriteInt32((int)window.OwnerDraw); writer.WriteInt32(window.OwnerDrawFlags); writer.WriteSingle(window.BorderSize); writer.WriteInt32((int)window.StaticFlags);
        WriteInts(writer, window.DynamicFlags.Select(value => (int)value).ToArray(), 4, "Window.dynamicFlags");
        writer.WriteInt32(window.NextTime);
        WriteVec4(writer, window.ForeColor); WriteVec4(writer, window.BackColor); WriteVec4(writer, window.BorderColor); WriteVec4(writer, window.OutlineColor); WriteVec4(writer, window.DisableColor);
        writer.WriteInt32(Pointer(background));
    }

    private static void WriteRectangle(XSourceWriter writer, RectangleDef value)
    {
        writer.WriteSingle(value.X); writer.WriteSingle(value.Y); writer.WriteSingle(value.W); writer.WriteSingle(value.H); writer.WriteByte((byte)value.HorzAlign); writer.WriteByte((byte)value.VertAlign); writer.WriteUInt16(value.Pad12);
    }

    private static void WriteVec4(XSourceWriter writer, Vec4 value)
    {
        writer.WriteSingle(value.A); writer.WriteSingle(value.R); writer.WriteSingle(value.G); writer.WriteSingle(value.B);
    }

    private static void WriteInts(XSourceWriter writer, IReadOnlyList<int> values, int expected, string path)
    {
        if (values.Count != expected) throw new InvalidDataException($"{path} requires exactly {expected} values.");
        foreach (int value in values) writer.WriteInt32(value);
    }

    private static void WriteTransitions(XSourceWriter writer, IReadOnlyList<MenuTransition> values)
    {
        if (values.Count != 4) throw new InvalidDataException("Menu transition groups require exactly four values.");
        foreach (MenuTransition value in values)
        {
            writer.WriteInt32((int)value.TransitionType); writer.WriteInt32(value.TargetField); writer.WriteInt32(value.StartTime); writer.WriteSingle(value.StartValue); writer.WriteSingle(value.EndValue); writer.WriteSingle(value.Time); writer.WriteInt32((int)value.EndTriggerType);
        }
    }

    private static PlannedString? PlanString(string? value, EmissionPlan plan, List<EmissionBlockSegment> all, List<EmissionBlockSegment> source)
    {
        int before = all.Count;
        PlannedString? result = AssetBodyEmitterHelpers.PlanString(value, plan, all, plan.StringAliases);
        source.AddRange(all.Skip(before));
        return result;
    }

    private static void ValidateWindow(WindowDef value, MenuReferenceBuildData references, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        ValidateString(value.Name, $"{path}.name", diagnostics, rowIndex); ValidateString(value.Group, $"{path}.group", diagnostics, rowIndex);
        ValidateReference(references.WindowBackgroundMaterial, XAssetType.Material, $"{path}.background", diagnostics, rowIndex);
        if (value.Background.Raw != 0 && references.WindowBackgroundMaterial is null)
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

    private static int Pointer(PlannedString? value) => AssetBodyEmitterHelpers.SourcePointer(value);
    private static int Pointer(ExternalPlan? value) => value?.PointerRaw ?? 0;

    private static ExternalPlan? PlanExternal(SymbolicXAssetReference? reference, XAssetType type, int rootSize, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (reference is null) return null;
        if (reference.AssetType != type || !reference.IsExternalReference || !AssetBodyEmitterHelpers.IsLatin1CString(reference.OriginalSerializedName))
            throw new InvalidDataException($"Menu external {type} reference must retain a comma-prefixed Latin-1 serialized name.");
        plan.Push(XFileBlockType.TEMP);
        EmissionAddress rootAddress = plan.Allocate(rootSize, 4);
        plan.Push(XFileBlockType.LARGE);
        int before = all.Count;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(reference.OriginalSerializedName, plan, all, plan.StringAliases);
        EmissionBlockSegment[] names = all.Skip(before).ToArray();
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);
        var writer = new XSourceWriter(); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); writer.Reserve(rootSize - sizeof(int));
        EmissionBlockSegment root = new(rootAddress, writer.ToArray()); all.Add(root);
        return new ExternalPlan(root, [root, .. names], -1);
    }

    private static void Add(List<EmissionBlockSegment> destination, ExternalPlan? value)
    {
        if (value is not null) destination.AddRange(value.Source);
    }

    private static void ValidateReference(SymbolicXAssetReference? value, XAssetType expected, string path, List<EmissionError> diagnostics, int? rowIndex)
    {
        if (value is null) return;
        if (value.AssetType != expected || !value.IsExternalReference || !AssetBodyEmitterHelpers.IsLatin1CString(value.OriginalSerializedName))
            diagnostics.Add(Error(path, $"Reference must be a comma-prefixed symbolic {expected} identity.", rowIndex));
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
