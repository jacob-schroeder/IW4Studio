using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Iw4Radiant.Viewports;

internal static class ViewportCursors
{
    private static readonly Lazy<Cursor> OpenHandCursor = new(() => CreateHand(closed: false));
    private static readonly Lazy<Cursor> ClosedHandCursor = new(() => CreateHand(closed: true));
    internal static Cursor OpenHand => OpenHandCursor.Value;
    internal static Cursor ClosedHand => ClosedHandCursor.Value;

    private static Cursor CreateHand(bool closed)
    {
        // Avalonia's standard Hand is a pointing finger; use matching grips for gizmos.
        using var bitmap = new RenderTargetBitmap(new PixelSize(24, 24), new Vector(96, 96));
        using (DrawingContext drawing = bitmap.CreateDrawingContext())
        {
            var outline = new Pen(Brushes.Black, 1.3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            drawing.DrawGeometry(Brushes.White, outline, Geometry.Parse(closed ?
                "M 6,12 L 6,8 C 6,5 10,5 10,8 L 10,6 C 10,3 14,3 14,6 " +
                "L 14,7 C 14,4 18,4 18,7 L 18,9 C 18,6 22,6 22,9 " +
                "L 22,14 C 22,17 20,18 19,20 L 19,22 L 9,22 L 9,20 " +
                "C 6,19 5,16 3,14 C 1,11 4,9 6,12 Z" :
                "M 7,12 L 5,6 C 4,3 7,2 8,5 L 9,9 L 9,3 C 9,0 12,0 12,3 " +
                "L 12,9 L 13,3 C 13,0 16,1 16,4 L 15,10 L 18,6 C 19,3 22,5 20,8 " +
                "L 18,14 C 18,17 17,19 17,21 L 8,21 C 8,19 6,18 5,16 " +
                "L 2,12 C 0,10 3,8 5,10 Z"));
            if (closed) drawing.DrawGeometry(null, outline, Geometry.Parse(
                "M 10,8 L 10,11 M 14,7 L 14,11 M 18,9 L 18,12 " +
                "M 6,12 L 9,15 C 11,17 14,15 12,13 L 10,11"));
        }
        return new Cursor(bitmap, new PixelPoint(10, 12));
    }
}
