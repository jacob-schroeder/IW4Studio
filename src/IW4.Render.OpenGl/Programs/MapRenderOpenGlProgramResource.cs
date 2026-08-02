using System.Collections.ObjectModel;
using IW4.Render.OpenGl.Shaders;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Immutable metadata owned by a context-local program cache for one linked GL
/// handle. Uniform locations are program properties; occurrence values never
/// enter this resource.
/// </summary>
public sealed class MapRenderOpenGlProgramResource
{
    private readonly ReadOnlyDictionary<int, int> _samplerUniformLocations;
    private readonly ReadOnlyDictionary<int, int> _vertexConstantUniformLocations;
    private readonly ReadOnlyDictionary<int, int>
        _codePixelConstantUniformLocations;
    public MapRenderOpenGlProgramResource(
        MapRenderOpenGlProgramKey key,
        uint handle,
        string vertexGlslSha256,
        string pixelGlslSha256,
        IReadOnlyDictionary<int, int> samplerUniformLocations,
        IReadOnlyDictionary<int, int> vertexConstantUniformLocations,
        IReadOnlyDictionary<int, int> codePixelConstantUniformLocations)
    {
        if (!key.IsValid)
            throw new ArgumentException("A valid OpenGL program key is required.", nameof(key));
        if (handle == 0)
            throw new ArgumentOutOfRangeException(nameof(handle), "A linked OpenGL program handle is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexGlslSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(pixelGlslSha256);
        ArgumentNullException.ThrowIfNull(samplerUniformLocations);
        ArgumentNullException.ThrowIfNull(vertexConstantUniformLocations);
        ArgumentNullException.ThrowIfNull(codePixelConstantUniformLocations);
        Key = key;
        Handle = handle;
        VertexGlslSha256 = vertexGlslSha256;
        PixelGlslSha256 = pixelGlslSha256;
        _samplerUniformLocations = SnapshotLocations(
            samplerUniformLocations,
            15,
            nameof(samplerUniformLocations));
        _vertexConstantUniformLocations = SnapshotLocations(
            vertexConstantUniformLocations,
            MapRenderRsxVertexConstantLayout.Count - 1,
            nameof(vertexConstantUniformLocations));
        _codePixelConstantUniformLocations = SnapshotLocations(
            codePixelConstantUniformLocations,
            MapRenderOpenGlCodePixelConstantUniformLayout.Count - 1,
            nameof(codePixelConstantUniformLocations));
    }

    public MapRenderOpenGlProgramKey Key { get; }

    public uint Handle { get; }

    public string VertexGlslSha256 { get; }

    public string PixelGlslSha256 { get; }

    public IReadOnlyDictionary<int, int> SamplerUniformLocations =>
        _samplerUniformLocations;

    public IReadOnlyDictionary<int, int> VertexConstantUniformLocations =>
        _vertexConstantUniformLocations;

    public IReadOnlyDictionary<int, int> CodePixelConstantUniformLocations =>
        _codePixelConstantUniformLocations;

    public bool TryGetSamplerUniformLocation(int destination, out int location) =>
        _samplerUniformLocations.TryGetValue(destination, out location);

    public bool TryGetVertexConstantUniformLocation(int destination, out int location) =>
        _vertexConstantUniformLocations.TryGetValue(destination, out location);

    public bool TryGetCodePixelConstantUniformLocation(
        int codeIndex,
        out int location) =>
        _codePixelConstantUniformLocations.TryGetValue(codeIndex, out location);

    private static ReadOnlyDictionary<int, int> SnapshotLocations(
        IReadOnlyDictionary<int, int> source,
        int maximumDestination,
        string parameterName)
    {
        var result = new SortedDictionary<int, int>();
        foreach ((int destination, int location) in source)
        {
            if ((uint)destination > (uint)maximumDestination)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    $"Uniform destination {destination} is outside 0..{maximumDestination}.");
            }
            if (location < 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    $"Cached uniform location {location} is inactive or invalid.");
            }

            result.Add(destination, location);
        }

        return new ReadOnlyDictionary<int, int>(result);
    }
}
