using IW4.Assets.Assets.Menu;
using IW4.Render.UI.ScreenPlacement;

namespace IW4.Studio.Documents.MenuEditing.Preview;

/// <summary>
/// Composes native Menu item rectangles, then applies or removes the verified
/// PS3 ScreenPlacement transform.
/// </summary>
public static class MenuRectTransform
{
    public static MenuPreviewPlacement Place(
        MenuRectangleValue value,
        MenuPreviewSettings settings) =>
        new(value, Resolve(value, settings));

    public static MenuPreviewRect Resolve(
        MenuRectangleValue value,
        MenuPreviewSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        UiScreenAxisPlacement horizontal = settings.ScreenPlacement.Resolve(
            value.HorizontalAlignment);
        UiScreenAxisPlacement vertical = settings.ScreenPlacement.Resolve(
            value.VerticalAlignment);
        return new MenuPreviewRect(
            horizontal.ApplyPosition(value.X),
            vertical.ApplyPosition(value.Y),
            horizontal.ApplyLength(MathF.Abs(value.Width)),
            vertical.ApplyLength(MathF.Abs(value.Height)));
    }

    public static MenuPreviewRect Unresolve(
        MenuPreviewRect value,
        HorizontalAlign horizontalAlignment,
        VerticalAlign verticalAlignment,
        MenuPreviewSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        UiScreenAxisPlacement horizontal = settings.ScreenPlacement.Resolve(
            horizontalAlignment);
        UiScreenAxisPlacement vertical = settings.ScreenPlacement.Resolve(
            verticalAlignment);
        return new MenuPreviewRect(
            horizontal.RemovePosition(value.X),
            vertical.RemovePosition(value.Y),
            horizontal.RemoveLength(value.Width),
            vertical.RemoveLength(value.Height));
    }

    /// <summary>
    /// Recreates Item_SetScreenCoords, then applies ScreenPlacement. Geometry
    /// comes from RectClient, includes Menu and item borders, and inherits the
    /// root alignment only when both client alignment fields are default.
    /// </summary>
    public static MenuPreviewPlacement PlaceItem(
        MenuRectangleValue root,
        float rootBorderInset,
        float itemBorderInset,
        MenuRectangleValue client,
        MenuPreviewSettings settings)
    {
        MenuRectangleValue rectangle = ComposeItem(
            root,
            rootBorderInset,
            itemBorderInset,
            client);
        return Place(rectangle, settings);
    }

    public static MenuRectangleValue ComposeItem(
        MenuRectangleValue root,
        float rootBorderInset,
        float itemBorderInset,
        MenuRectangleValue client)
    {
        bool inheritsRootAlignment =
            client.HorizontalAlignment ==
                HorizontalAlign.HORIZONTAL_ALIGN_SUBLEFT &&
            client.VerticalAlignment ==
                VerticalAlign.VERTICAL_ALIGN_SUBTOP;
        return client with
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
    }
}
