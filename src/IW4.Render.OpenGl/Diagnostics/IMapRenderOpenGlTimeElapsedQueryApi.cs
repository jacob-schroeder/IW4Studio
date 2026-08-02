namespace IW4.Render.OpenGl.Diagnostics;

/// <summary>
/// Narrow OpenGL query API used by the timer-query ring. Keeping the driver API
/// behind this boundary makes the no-stall scheduling behavior testable
/// without an OpenGL context.
/// </summary>
public interface IMapRenderOpenGlTimeElapsedQueryApi
{
    uint CreateQuery();

    void DeleteQuery(uint query);

    void BeginTimeElapsedQuery(uint query);

    void EndTimeElapsedQuery();

    bool IsQueryResultAvailable(uint query);

    ulong GetQueryResultNanoseconds(uint query);
}
