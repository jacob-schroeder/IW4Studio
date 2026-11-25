using System.Numerics;
using IW4.Game.Assets.TechniqueSet;
using IW4.Render.Shaders;

namespace IW4.Render.EditorPreview;

/// <summary>Projects sampled native, Z-up FX vertices into authored RSX input registers.</summary>
public static class FxRenderVertexPayload
{
    private const int RegisterCount = 16;
    private const int ComponentsPerRegister = 4;
    private const int FloatsPerVertex = RegisterCount * ComponentsPerRegister;

    public static bool Project(
        FxPreviewVertex[] vertices,
        IReadOnlyList<ShaderVertexInputBinding> bindings,
        out float[] rsxVertexInputs,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(bindings);
        var values = new float[checked(vertices.Length * FloatsPerVertex)];
        if (!Project(vertices, bindings, values, out blocker))
        {
            rsxVertexInputs = [];
            return false;
        }
        rsxVertexInputs = values;
        return true;
    }

    public static bool Project(
        FxPreviewVertex[] vertices,
        IReadOnlyList<ShaderVertexInputBinding> bindings,
        Span<float> destination,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(bindings);
        blocker = string.Empty;
        if (vertices.Length % 3 != 0)
        {
            blocker = "FX vertex count is not a complete triangle list";
            return false;
        }
        if (destination.Length != checked(vertices.Length * FloatsPerVertex))
        {
            blocker = "FX RSX vertex destination length does not match the sampled vertices";
            return false;
        }
        if (bindings.Count == 0)
        {
            blocker = "vertexDeclarationRoutes=missing";
            return false;
        }

        uint destinationMask = 0;
        bool requiresTangent = false;
        foreach (ShaderVertexInputBinding binding in bindings)
        {
            requiresTangent |= binding.Source == MaterialStreamSource.Tangent;
            int destinationIndex = (byte)binding.Destination;
            uint bit = destinationIndex < RegisterCount ? 1u << destinationIndex : 0u;
            if (bit == 0 || (destinationMask & bit) != 0)
            {
                blocker = $"FX vertex destination {destinationIndex} is invalid or duplicated";
                return false;
            }
            destinationMask |= bit;
            bool supported = binding.Source switch
            {
                MaterialStreamSource.Position => binding.ComponentCount == 4 &&
                    binding.RsxType == RsxVertexElementType.Float32,
                MaterialStreamSource.Color => binding.ComponentCount == 4 &&
                    binding.RsxType == RsxVertexElementType.Unsigned8Normalized,
                MaterialStreamSource.TexCoord0 => binding.ComponentCount == 2 &&
                    binding.RsxType is RsxVertexElementType.Float32 or RsxVertexElementType.Float16,
                MaterialStreamSource.Normal => binding.ComponentCount == 1 &&
                    binding.RsxType == RsxVertexElementType.Signed11_11_10Normalized,
                MaterialStreamSource.Tangent => binding.ComponentCount == 1 &&
                    binding.RsxType == RsxVertexElementType.Signed11_11_10Normalized,
                _ => false
            };
            if (binding.IsDisabledDefaultAttribute || !supported)
            {
                blocker = $"FX vertex stream {binding.Source} is unavailable";
                return false;
            }
        }

        for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
        {
            FxPreviewVertex vertex = vertices[vertexIndex];
            if (requiresTangent && !vertex.HasTangent)
            {
                blocker = $"FX vertex {vertexIndex} has no authored tangent";
                return false;
            }
            int baseIndex = vertexIndex * FloatsPerVertex;
            for (int register = 0; register < RegisterCount; register++)
            {
                int offset = baseIndex + register * ComponentsPerRegister;
                destination[offset] = 0f;
                destination[offset + 1] = 0f;
                destination[offset + 2] = 0f;
                destination[offset + 3] = 1f;
            }
            foreach (ShaderVertexInputBinding binding in bindings)
            {
                Vector4 value = binding.Source switch
                {
                    MaterialStreamSource.Position => new Vector4(vertex.Position, vertex.PositionW),
                    MaterialStreamSource.Color => vertex.Color,
                    MaterialStreamSource.TexCoord0 => new Vector4(vertex.Uv, 0f, 1f),
                    MaterialStreamSource.Normal => new Vector4(vertex.Normal, 1f),
                    MaterialStreamSource.Tangent => new Vector4(vertex.Tangent, 1f),
                    _ => default
                };
                int offset = baseIndex + (byte)binding.Destination * ComponentsPerRegister;
                destination[offset] = value.X;
                destination[offset + 1] = value.Y;
                destination[offset + 2] = value.Z;
                destination[offset + 3] = value.W;
            }
        }
        return true;
    }
}
