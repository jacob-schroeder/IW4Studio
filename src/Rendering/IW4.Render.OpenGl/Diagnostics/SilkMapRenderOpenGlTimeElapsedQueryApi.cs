using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Diagnostics;

/// <summary>
/// Silk.NET implementation of the narrow timer-query API. All methods must be
/// called on the thread that owns the active OpenGL context.
/// </summary>
public sealed class SilkMapRenderOpenGlTimeElapsedQueryApi :
    IMapRenderOpenGlTimeElapsedQueryApi
{
    private readonly GL _gl;

    public SilkMapRenderOpenGlTimeElapsedQueryApi(GL gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _gl = gl;
    }

    public uint CreateQuery() => _gl.GenQuery();

    public void DeleteQuery(uint query) => _gl.DeleteQuery(query);

    public void BeginTimeElapsedQuery(uint query) =>
        _gl.BeginQuery(QueryTarget.TimeElapsed, query);

    public void EndTimeElapsedQuery() =>
        _gl.EndQuery(QueryTarget.TimeElapsed);

    public unsafe bool IsQueryResultAvailable(uint query)
    {
        int available = 0;
        _gl.GetQueryObject(
            query,
            QueryObjectParameterName.ResultAvailable,
            &available);
        return available != 0;
    }

    public unsafe ulong GetQueryResultNanoseconds(uint query)
    {
        ulong nanoseconds = 0;
        _gl.GetQueryObject(
            query,
            QueryObjectParameterName.Result,
            &nanoseconds);
        return nanoseconds;
    }
}
