using System.Numerics;
using IW4.Formats.SourceFormat.XModel;
using IW4.Formats.XModel;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;

namespace IW4.Render.EditorPreview;

public static class FxModelPreviewGeometry
{
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
        return document.Triangles.GroupBy(triangle => document.Materials[triangle.MaterialIndex].Name,
                StringComparer.Ordinal)
            .Select(group => (group.Key, group.SelectMany(triangle =>
                // Keep the winding used by Radiant's XMODEL_EXPORT preview.
                new[] { Vertex(triangle.First), Vertex(triangle.Third), Vertex(triangle.Second) }).ToArray())).ToArray();

        FxPreviewVertex Vertex(XModelExportCorner corner) => new(
            document.Vertices[corner.VertexIndex].Position, corner.Normal, corner.Uv0, corner.Color);
    }
}
