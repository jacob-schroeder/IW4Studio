using IW4.Formats.SourceFormat.XModel;
using IW4.Formats.XModel;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;

namespace Iw4Radiant.Rendering;

internal static class FxModelPreviewGeometry
{
    internal static IReadOnlyList<(string Material, SceneVertex[] Vertices)> Load(string sourceDirectory, string modelName)
    {
        // Resolve geometry with the same native source reader used by the linker.
        // FX owns motion; material textures are resolved by the viewport catalog.
        var imported = new XModelNativeExchange().Link(sourceDirectory, modelName,
            name => new MaterialAsset { Info = new MaterialInfo { Name = name } },
            name => new PhysPresetAsset { Name = name },
            name => new PhysCollmapAsset { Name = name });
        if (!XModelExportProjector.TryProjectMaterializedLod(imported.Model, 0, out var document, out var blockers) ||
            document is null)
            throw new InvalidDataException($"FX model '{modelName}': {string.Join("; ", blockers.Take(2))}");
        return XModelGeometry.GetLocalTriangles(document)
            .GroupBy(triangle => triangle.Material, StringComparer.Ordinal)
            .Select(group => (group.Key, group.SelectMany(triangle =>
                new[] { triangle.A, triangle.B, triangle.C }).ToArray())).ToArray();
    }
}
