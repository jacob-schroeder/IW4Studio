using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Preview;

/// <summary>
/// Projects authored static Menu state into renderer-neutral primitives.
/// Runtime expressions, owner-draw callbacks, models and cinematics are
/// represented by fidelity diagnostics/placeholders rather than simulated.
/// </summary>
public static class MenuPreviewProjector
{
    public static MenuPreviewScene Project(
        MenuEditorSnapshot menu,
        MenuPreviewSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        settings ??= MenuPreviewSettings.Default;
        ValidateSettings(settings);

        var primitives = new List<MenuPreviewPrimitive>();
        var hitRegions = new List<MenuPreviewHitRegion>();
        var issues = new List<MenuPreviewFidelityIssue>();

        MenuPreviewRect rootBounds = MenuRectTransform.Resolve(
            menu.Window.Value.Rect,
            settings);
        if (menu.Settings.Fullscreen != 0 &&
            !string.IsNullOrWhiteSpace(menu.Window.Value.BackgroundMaterialName))
        {
            primitives.Add(new MenuPreviewMaterial(
                menu.Window.Id,
                new MenuPreviewRect(0, 0, settings.CanvasWidth, settings.CanvasHeight),
                -10,
                menu.Window.Value.BackgroundMaterialName,
                new MenuColorValue(1, 1, 1, 1)));
        }
        ProjectWindow(
            menu.Window.Id,
            menu.Window.Value,
            rootBounds,
            0,
            "window",
            menu.Window.Value.Rect,
            primitives,
            hitRegions,
            issues);
        BehaviorIssues(menu, issues);

        for (int index = 0; index < menu.Items.Count; index++)
        {
            MenuItemSnapshot item = menu.Items[index];
            string path = $"items[{item.Id}]";
            if (!item.IsResolved)
            {
                issues.Add(new MenuPreviewFidelityIssue(
                    item.Id,
                    path,
                    "The item is unresolved and cannot be previewed.",
                    MenuPreviewFidelitySeverity.Warning));
                continue;
            }

            MenuPreviewRect bounds = MenuRectTransform.ResolveItem(
                menu.Window.Value,
                item.Value.Window,
                settings);
            int z = checked(index * 10 + 10);
            ProjectWindow(
                item.Id,
                item.Value.Window,
                bounds,
                z,
                $"{path}.window",
                item.Value.Window.RectClient,
                primitives,
                hitRegions,
                issues);
            ProjectItem(item, bounds, z + 4, path, primitives, issues);
        }

        issues.Insert(0, new MenuPreviewFidelityIssue(
            null,
            "preview",
            "Editor Preview renders authored static state and is not a runtime UI emulator.",
            MenuPreviewFidelitySeverity.Information));
        if (settings.CanvasWidth != 640 || settings.CanvasHeight != 480)
        {
            issues.Add(new MenuPreviewFidelityIssue(
                null,
                "preview.canvas",
                "PS3 alignment is defined in 640x480 virtual space; noncanonical dimensions are an editor visualization only.",
                MenuPreviewFidelitySeverity.Information));
        }
        return new MenuPreviewScene(settings, primitives, hitRegions, issues);
    }

    private static void ProjectWindow(
        MenuNodeId nodeId,
        MenuWindowValue window,
        MenuPreviewRect bounds,
        int z,
        string path,
        MenuRectangleValue authoredRect,
        List<MenuPreviewPrimitive> primitives,
        List<MenuPreviewHitRegion> hitRegions,
        List<MenuPreviewFidelityIssue> issues)
    {
        switch (window.Style)
        {
            case WindowStyle.WINDOW_STYLE_EMPTY:
                break;
            case WindowStyle.WINDOW_STYLE_FILLED:
                primitives.Add(new MenuPreviewFill(
                    nodeId,
                    bounds,
                    z,
                    window.BackColor));
                break;
            case WindowStyle.WINDOW_STYLE_SHADER:
                if (!string.IsNullOrWhiteSpace(window.BackgroundMaterialName))
                {
                    primitives.Add(new MenuPreviewMaterial(
                        nodeId,
                        bounds,
                        z,
                        window.BackgroundMaterialName,
                        ResolveShaderTint(window)));
                    if (UsesForeColorTint(window) &&
                        !IsWhiteRgb(window.ForeColor))
                    {
                        issues.Add(new MenuPreviewFidelityIssue(
                            nodeId,
                            $"{path}.foreColor",
                            "Material foreground tint is approximated by the editor texture renderer.",
                            MenuPreviewFidelitySeverity.Warning));
                    }
                }
                else
                {
                    primitives.Add(new MenuPreviewPlaceholder(
                        nodeId,
                        bounds,
                        z,
                        "Missing Material"));
                }
                break;
            case WindowStyle.WINDOW_STYLE_GRADIENT:
                primitives.Add(new MenuPreviewFill(nodeId, bounds, z, window.BackColor));
                issues.Add(new MenuPreviewFidelityIssue(
                    nodeId,
                    $"{path}.style",
                    "Gradient style is approximated by a solid fill.",
                    MenuPreviewFidelitySeverity.Warning));
                break;
            case WindowStyle.WINDOW_STYLE_TEAMCOLOR:
                primitives.Add(new MenuPreviewPlaceholder(nodeId, bounds, z, "Team Color"));
                issues.Add(new MenuPreviewFidelityIssue(
                    nodeId,
                    $"{path}.style",
                    "Team-color state is runtime controlled.",
                    MenuPreviewFidelitySeverity.Warning));
                break;
            case WindowStyle.WINDOW_STYLE_CINEMATIC:
                primitives.Add(new MenuPreviewPlaceholder(nodeId, bounds, z, "Cinematic"));
                issues.Add(new MenuPreviewFidelityIssue(
                    nodeId,
                    $"{path}.style",
                    "Cinematic playback is not available in Editor Preview.",
                    MenuPreviewFidelitySeverity.Warning));
                break;
            default:
                issues.Add(new MenuPreviewFidelityIssue(
                    nodeId,
                    $"{path}.style",
                    $"Unknown Window style value {(int)window.Style} is not rendered.",
                    MenuPreviewFidelitySeverity.Warning));
                break;
        }

        if (window.Border != WindowBorder.WINDOW_BORDER_NONE && window.BorderSize > 0)
        {
            primitives.Add(new MenuPreviewBorder(
                nodeId,
                bounds,
                z + 2,
                window.BorderColor,
                window.BorderSize,
                window.Border));
            if (window.Border == WindowBorder.WINDOW_BORDER_KCGRADIENT)
            {
                issues.Add(new MenuPreviewFidelityIssue(
                    nodeId,
                    $"{path}.border",
                    "KC gradient borders are approximated by horizontal solid edges.",
                    MenuPreviewFidelitySeverity.Warning));
            }
            else if (!Enum.IsDefined(window.Border))
            {
                issues.Add(new MenuPreviewFidelityIssue(
                    nodeId,
                    $"{path}.border",
                    $"Unknown border value {(int)window.Border} is approximated by a full border.",
                    MenuPreviewFidelitySeverity.Warning));
            }
        }
        else if (!float.IsFinite(window.BorderSize))
        {
            issues.Add(new MenuPreviewFidelityIssue(
                nodeId,
                $"{path}.borderSize",
                "A non-finite border size cannot be rendered.",
                MenuPreviewFidelitySeverity.Warning));
        }
        if ((int)window.OwnerDraw != 0)
        {
            primitives.Add(new MenuPreviewPlaceholder(nodeId, bounds, z + 3, "OwnerDraw"));
            issues.Add(new MenuPreviewFidelityIssue(
                nodeId,
                $"{path}.ownerDraw",
                "OwnerDraw content requires game runtime callbacks.",
                MenuPreviewFidelitySeverity.Warning));
        }
        AddAlignmentIssue(nodeId, authoredRect, path, issues);
        hitRegions.Add(new MenuPreviewHitRegion(nodeId, bounds, z + 9));
    }

    private static void ProjectItem(
        MenuItemSnapshot item,
        MenuPreviewRect bounds,
        int z,
        string path,
        List<MenuPreviewPrimitive> primitives,
        List<MenuPreviewFidelityIssue> issues)
    {
        MenuItemValue value = item.Value;
        if (!string.IsNullOrEmpty(value.Text))
        {
            float textOffsetX = float.IsFinite(value.TextAlignX)
                ? value.TextAlignX
                : 0;
            float textOffsetY = float.IsFinite(value.TextAlignY)
                ? value.TextAlignY
                : 0;
            var textBounds = bounds with
            {
                X = bounds.X + textOffsetX,
                Y = bounds.Y + textOffsetY
            };
            primitives.Add(new MenuPreviewText(
                item.Id,
                textBounds,
                z,
                value.Text,
                value.Window.ForeColor,
                value.TextScale,
                value.FontEnum,
                value.TextAlignMode,
                value.TextStyle));
            issues.Add(new MenuPreviewFidelityIssue(
                item.Id,
                $"{path}.text",
                "Text uses editor metrics; exact game font metrics may differ.",
                MenuPreviewFidelitySeverity.Information));
            if (!float.IsFinite(value.TextScale))
            {
                issues.Add(new MenuPreviewFidelityIssue(
                    item.Id,
                    $"{path}.textScale",
                    "A non-finite text scale is rendered with a safe editor fallback.",
                    MenuPreviewFidelitySeverity.Warning));
            }
            if (!float.IsFinite(value.TextAlignX) ||
                !float.IsFinite(value.TextAlignY))
            {
                issues.Add(new MenuPreviewFidelityIssue(
                    item.Id,
                    $"{path}.textAlignment",
                    "A non-finite text offset is rendered with a zero editor fallback.",
                    MenuPreviewFidelitySeverity.Warning));
            }
        }

        if (value.Type is ItemDefType.Model)
        {
            primitives.Add(new MenuPreviewPlaceholder(item.Id, bounds, z, "Model"));
            issues.Add(new MenuPreviewFidelityIssue(
                item.Id,
                $"{path}.type",
                "Model item rendering is not available in Editor Preview.",
                MenuPreviewFidelitySeverity.Warning));
        }
        if (value.Type is ItemDefType.OwnerDraw)
        {
            primitives.Add(new MenuPreviewPlaceholder(item.Id, bounds, z, "OwnerDraw Item"));
        }
        if (value.Behavior.HasVisibleExpression ||
            value.Behavior.HasDisabledExpression ||
            value.Behavior.HasTextExpression ||
            value.Behavior.HasMaterialExpression)
        {
            issues.Add(new MenuPreviewFidelityIssue(
                item.Id,
                $"{path}.expressions",
                "Expression-controlled item state is shown using its static authored values.",
                MenuPreviewFidelitySeverity.Warning));
        }
    }

    private static void BehaviorIssues(
        MenuEditorSnapshot menu,
        List<MenuPreviewFidelityIssue> issues)
    {
        if (menu.Behavior.HasVisibleExpression ||
            menu.Behavior.HasRectXExpression ||
            menu.Behavior.HasRectYExpression ||
            menu.Behavior.HasRectWidthExpression ||
            menu.Behavior.HasRectHeightExpression)
        {
            issues.Add(new MenuPreviewFidelityIssue(
                menu.Id,
                "menu.expressions",
                "Expression-controlled Menu geometry or visibility uses static authored values.",
                MenuPreviewFidelitySeverity.Warning));
        }
    }

    private static void AddAlignmentIssue(
        MenuNodeId nodeId,
        MenuRectangleValue rect,
        string path,
        List<MenuPreviewFidelityIssue> issues)
    {
        if ((byte)rect.HorizontalAlignment >= 8 || (byte)rect.VerticalAlignment >= 8)
        {
            issues.Add(new MenuPreviewFidelityIssue(
                nodeId,
                $"{path}.rect",
                "Raw PS3 alignment values are shown as direct virtual coordinates.",
                MenuPreviewFidelitySeverity.Warning));
        }
    }

    private static void ValidateSettings(MenuPreviewSettings settings)
    {
        if (!float.IsFinite(settings.CanvasWidth) || settings.CanvasWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "Canvas width must be finite and positive.");
        if (!float.IsFinite(settings.CanvasHeight) || settings.CanvasHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "Canvas height must be finite and positive.");
        if (settings.SafeArea.Left < 0 || settings.SafeArea.Top < 0 ||
            settings.SafeArea.Right < 0 || settings.SafeArea.Bottom < 0 ||
            settings.SafeArea.Left + settings.SafeArea.Right >= settings.CanvasWidth ||
            settings.SafeArea.Top + settings.SafeArea.Bottom >= settings.CanvasHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Safe-area insets must fit inside the preview canvas.");
        }
    }

    private static bool IsWhiteRgb(MenuColorValue color) =>
        float.IsFinite(color.R) &&
        float.IsFinite(color.G) &&
        float.IsFinite(color.B) &&
        Math.Abs(color.R - 1) < 0.0001f &&
        Math.Abs(color.G - 1) < 0.0001f &&
        Math.Abs(color.B - 1) < 0.0001f;

    private static MenuColorValue ResolveShaderTint(MenuWindowValue window) =>
        UsesForeColorTint(window)
            ? window.ForeColor
            : new MenuColorValue(1, 1, 1, 1);

    private static bool UsesForeColorTint(MenuWindowValue window) =>
        window.DynamicFlags.Count > 0 &&
        (window.DynamicFlags[0] &
         WindowDynamicFlags.WINDOW_DYNAMIC_HAS_FORECOLOR) != 0;
}
