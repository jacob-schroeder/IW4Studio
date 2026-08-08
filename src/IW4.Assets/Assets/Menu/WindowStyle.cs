namespace IW4.Assets.Assets.Menu;

public enum WindowStyle : int
{
    /// <summary>
    /// Draw no background for the window.
    /// </summary>
    WINDOW_STYLE_EMPTY = 0,

    /// <summary>
    /// Fill the window rectangle with backColor.
    /// </summary>
    WINDOW_STYLE_FILLED = 1,

    /// <summary>
    /// Draw a gradient fill based on backColor.
    /// </summary>
    WINDOW_STYLE_GRADIENT = 2,

    /// <summary>
    /// Draw the background material in the window rectangle.
    /// </summary>
    WINDOW_STYLE_SHADER = 3,

    /// <summary>
    /// Draw using the current team color.
    /// </summary>
    WINDOW_STYLE_TEAMCOLOR = 4,

    /// <summary>
    /// Draw a cinematic/movie-backed window.
    /// </summary>
    WINDOW_STYLE_CINEMATIC = 5,

    /// <summary>
    /// Observed in serialized menu data; its engine behavior is unknown.
    /// </summary>
    WINDOW_STYLE_UNKNOWN_6 = 6
}
