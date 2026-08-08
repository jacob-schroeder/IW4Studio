namespace IW4.Assets.Assets.Menu;

public enum WindowBorder : int
{
    /// <summary>
    /// Draw no border.
    /// </summary>
    WINDOW_BORDER_NONE = 0,

    /// <summary>
    /// Draw all four border edges.
    /// </summary>
    WINDOW_BORDER_FULL = 1,

    /// <summary>
    /// Draw only the top and bottom border edges.
    /// </summary>
    WINDOW_BORDER_HORZ = 2,

    /// <summary>
    /// Draw only the left and right border edges.
    /// </summary>
    WINDOW_BORDER_VERT = 3,

    /// <summary>
    /// Draw horizontal gradient-bar borders.
    /// </summary>
    WINDOW_BORDER_KCGRADIENT = 4,

    /// <summary>
    /// Observed in serialized menu data; its engine behavior is unknown.
    /// </summary>
    WINDOW_BORDER_UNKNOWN_5 = 5
}
