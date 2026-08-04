using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing.Preview;

/// <summary>
/// Pure preview transform for the documented Menu alignment discriminators.
/// Raw/unknown PS3 values deliberately fall back to direct coordinates and
/// are accompanied by a fidelity warning from the projector.
/// </summary>
public static class MenuRectTransform
{
    public static MenuPreviewRect Resolve(
        MenuRectangleValue value,
        MenuPreviewSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        float width = value.Width;
        float height = value.Height;
        float x = value.HorizontalAlignment switch
        {
            HorizontalAlign.HORIZONTAL_ALIGN_LEFT => settings.SafeArea.Left + value.X,
            HorizontalAlign.HORIZONTAL_ALIGN_CENTER =>
                settings.CanvasWidth * 0.5f + value.X,
            HorizontalAlign.HORIZONTAL_ALIGN_RIGHT =>
                settings.CanvasWidth - settings.SafeArea.Right + value.X,
            HorizontalAlign.HORIZONTAL_ALIGN_CENTER_SAFEAREA =>
                settings.SafeArea.Left +
                SafeWidth(settings) * 0.5f +
                value.X,
            _ => value.X
        };

        float y = value.VerticalAlignment switch
        {
            VerticalAlign.VERTICAL_ALIGN_TOP => settings.SafeArea.Top + value.Y,
            VerticalAlign.VERTICAL_ALIGN_CENTER =>
                settings.CanvasHeight * 0.5f + value.Y,
            VerticalAlign.VERTICAL_ALIGN_BOTTOM =>
                settings.CanvasHeight - settings.SafeArea.Bottom + value.Y,
            VerticalAlign.VERTICAL_ALIGN_CENTER_SAFEAREA =>
                settings.SafeArea.Top +
                SafeHeight(settings) * 0.5f +
                value.Y,
            _ => value.Y
        };

        return new MenuPreviewRect(x, y, width, height);
    }

    /// <summary>
    /// Recreates the authored portion of Item_SetScreenCoords: Item geometry
    /// comes from RectClient, is offset by the root Menu, and inherits the
    /// root alignment only when both client alignment fields are default.
    /// </summary>
    public static MenuPreviewRect ResolveItem(
        MenuWindowValue root,
        MenuWindowValue item,
        MenuPreviewSettings settings)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(item);
        float rootInset = root.Border == WindowBorder.WINDOW_BORDER_NONE
            ? 0
            : root.BorderSize;
        float itemInset = item.Border == WindowBorder.WINDOW_BORDER_NONE
            ? 0
            : item.BorderSize;
        return ResolveItem(
            root.Rect,
            rootInset,
            itemInset,
            item.RectClient,
            settings);
    }

    /// <summary>
    /// Resolves evaluated root and item rectangles without reconstructing
    /// mutable Window definitions. The caller supplies the authored root and
    /// item border insets because expressions can replace geometry, not
    /// border configuration.
    /// </summary>
    public static MenuPreviewRect ResolveItem(
        MenuRectangleValue root,
        float rootBorderInset,
        float itemBorderInset,
        MenuRectangleValue client,
        MenuPreviewSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        bool inheritsRootAlignment =
            client.HorizontalAlignment ==
                HorizontalAlign.HORIZONTAL_ALIGN_SUBLEFT &&
            client.VerticalAlignment ==
                VerticalAlign.VERTICAL_ALIGN_SUBTOP;
        var screen = client with
        {
            X = client.X + root.X + rootBorderInset + itemBorderInset,
            Y = client.Y + root.Y + rootBorderInset + itemBorderInset,
            HorizontalAlignment = inheritsRootAlignment
                ? root.HorizontalAlignment
                : client.HorizontalAlignment,
            VerticalAlignment = inheritsRootAlignment
                ? root.VerticalAlignment
                : client.VerticalAlignment
        };
        return Resolve(screen, settings);
    }

    private static float SafeWidth(MenuPreviewSettings settings) =>
        settings.CanvasWidth - settings.SafeArea.Left - settings.SafeArea.Right;

    private static float SafeHeight(MenuPreviewSettings settings) =>
        settings.CanvasHeight - settings.SafeArea.Top - settings.SafeArea.Bottom;
}
