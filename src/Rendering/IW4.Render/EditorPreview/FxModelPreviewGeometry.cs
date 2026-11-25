using System.Numerics;
using IW4.Game.Codecs.XModel;
using IW4.Game.Assets.TechniqueSet;
using IW4.Formats.SourceFormat.XModel;
using IW4.Formats.XModel;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Render.Geometry;
using IW4.Render.Shaders;

namespace IW4.Render.EditorPreview;

public static class FxModelPreviewGeometry
{
    private static readonly ShaderVertexInputBinding[] PositionInput =
    [
        new(0, MaterialStreamSource.Position,
            MaterialStreamDestination.Position, 0,
            XSurfaceVertexCodec.StreamStride, 0, 4,
            RsxVertexElementType.Float32)
    ];

    internal static IReadOnlyList<(string Material, FxPreviewVertex[] Vertices)> Load(string sourceDirectory, string modelName)
    {
        // Resolve geometry with the same native source reader used by the linker.
        // FX owns motion; material textures are resolved by the viewport catalog.
        var imported = new XModelNativeExchange().Link(sourceDirectory, modelName,
            name => new MaterialAsset { Info = new MaterialInfo { Name = name } },
            name => new PhysPresetAsset { Name = name },
            name => new PhysCollmapAsset { Name = name });
        return FromModel(imported.Model);
    }

    public static IReadOnlyList<(string Material, FxPreviewVertex[] Vertices)> FromModel(XModelAsset model)
    {
        if (!XModelExportProjector.TryProjectMaterializedLod(model, 0, out var document, out var blockers) ||
            document is null)
            throw new InvalidDataException($"FX model '{model.Name}': {string.Join("; ", blockers.Take(2))}");
        XModelSurfsAsset? modelSurfs = model.Lods[0].ModelSurfs;
        if (modelSurfs is null)
            throw new InvalidDataException($"FX model '{model.Name}' has no materialized LOD 0 surfaces.");
        var tangents = new Vector3?[document.Vertices.Count];
        var positionW = new float[document.Vertices.Count];
        Span<Vector4> positionInput = stackalloc Vector4[XSurfaceVertexDecoder.RsxVertexInputCount];
        int vertexBase = 0;
        for (int surfaceIndex = 0; surfaceIndex < model.Lods[0].NumSurfs; surfaceIndex++)
        {
            XSurface surface = modelSurfs.Surfaces[surfaceIndex];
            if (surface.VertCount > tangents.Length - vertexBase)
                throw new InvalidDataException($"FX model '{model.Name}' has inconsistent LOD 0 vertex indexing.");
            for (int vertexIndex = 0; vertexIndex < surface.VertCount; vertexIndex++)
            {
                if (!XSurfaceVertexDecoder.TryReadRsxVertexInputs(
                        surface, vertexIndex, PositionInput, positionInput, out string blocker) ||
                    !float.IsFinite(positionInput[0].W))
                    throw new InvalidDataException(
                        $"FX model '{model.Name}' vertex {vertexBase + vertexIndex}: " +
                        $"position W is unavailable ({blocker}).");
                positionW[vertexBase + vertexIndex] = positionInput[0].W;
                if (XSurfaceVertexCodec.TryReadTangent(surface.Verts1, vertexIndex,
                        out Vector3 tangent))
                    tangents[vertexBase + vertexIndex] = tangent;
            }
            vertexBase += surface.VertCount;
        }
        if (vertexBase != tangents.Length)
            throw new InvalidDataException($"FX model '{model.Name}' has inconsistent LOD 0 vertex indexing.");
        return document.Triangles.GroupBy(triangle => document.Materials[triangle.MaterialIndex].Name,
                StringComparer.Ordinal)
            .Select(group => (group.Key, group.SelectMany(triangle =>
                // Keep the winding used by Radiant's XMODEL_EXPORT preview.
                new[] { Vertex(triangle.First), Vertex(triangle.Third), Vertex(triangle.Second) }).ToArray())).ToArray();

        FxPreviewVertex Vertex(XModelExportCorner corner) => new(
            document.Vertices[corner.VertexIndex].Position, corner.Normal, corner.Uv0, corner.Color,
            tangents[corner.VertexIndex] ?? default,
            tangents[corner.VertexIndex].HasValue,
            positionW[corner.VertexIndex]);
    }
}
