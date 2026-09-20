using System.Globalization;
using System.Text;
using IW4.AssetExchange.SourceFormat.Material;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.RawFile;

namespace D3dbspLinker.Conversion;

internal static class WaterVolumeScript
{
    internal static RawFileAsset? Create(string mapAssetName, ClipMapAsset collision,
        IReadOnlyDictionary<string, WaterMaterialDefinition> waterMaterials)
    {
        var volumes = new StringBuilder();
        int count = 0;
        for (int index = 0; index < collision.Brushes.Count; index++)
        {
            var brush = collision.Brushes[index];
            string? materialName = TopMaterialName(collision, brush);
            if (materialName is null || !waterMaterials.TryGetValue(materialName, out WaterMaterialDefinition? material)) continue;
            var bounds = collision.BrushBounds[index];
            var center = bounds.MidPoint;
            var extent = bounds.HalfSize;
            volumes.AppendLine("    volume = spawnStruct();");
            volumes.AppendLine($"    volume.minimum = {Vector(center.X - extent.X, center.Y - extent.Y, center.Z - extent.Z)};");
            volumes.AppendLine($"    volume.maximum = {Vector(center.X + extent.X, center.Y + extent.Y, center.Z + extent.Z)};");
            volumes.AppendLine("    volume.normals = []; volume.distances = [];");
            var tint = WaterMaterialAuthoring.CreateUnderwaterTint(material.Red, material.Green, material.Blue);
            volumes.AppendLine($"    volume.tint = {Vector(tint.X, tint.Y, tint.Z)};");
            foreach (var side in brush.Sides)
            {
                var plane = side.Plane ?? throw new InvalidDataException("A water volume has an unresolved collision plane.");
                volumes.AppendLine($"    volume.normals[volume.normals.size] = {Vector(plane.Normal.X, plane.Normal.Y, plane.Normal.Z)};");
                volumes.AppendLine($"    volume.distances[volume.distances.size] = {Number(plane.Dist)};");
            }
            volumes.AppendLine($"    volume.index = {count};");
            volumes.AppendLine($"    level.iw4r_water_volumes[{count++}] = volume;");
        }
        if (count == 0) return null;
        using Stream stream = typeof(WaterVolumeScript).Assembly.GetManifestResourceStream("IW4.WaterVolumes.gsc") ??
            throw new InvalidDataException("Missing packaged water-volume script.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string script = reader.ReadToEnd()
            .Replace("__VOLUMES__", volumes.ToString(), StringComparison.Ordinal)
            .Replace("__OPACITY__", Number(WaterMaterialAuthoring.UnderwaterOpacity), StringComparison.Ordinal)
            .Replace("__FADE__", Number(WaterMaterialAuthoring.UnderwaterFadeSeconds), StringComparison.Ordinal)
            .Replace("__INSET__", Number(WaterMaterialAuthoring.UnderwaterBoundaryInset), StringComparison.Ordinal);
        byte[] bytes = Encoding.ASCII.GetBytes(script);
        return new RawFileAsset { Name = mapAssetName[..^".d3dbsp".Length] + "_water.gsc",
            Len = bytes.Length, CompressedLen = 0, Buffer = [.. bytes, 0] };
    }

    internal static string Startup(RawFileAsset script)
    {
        string name = script.Name ?? throw new InvalidDataException("Missing water script name.");
        return name[..^4].Replace('/', '\\') + "::main();";
    }

    private static string? TopMaterialName(ClipMapAsset collision, CBrush brush)
    {
        float topNormalZ = float.NegativeInfinity;
        string? topMaterial = null;
        void Consider(int materialIndex, float normalZ)
        {
            string material = collision.Materials[materialIndex].Name ?? "";
            if (normalZ > topNormalZ || normalZ == topNormalZ &&
                (topMaterial is null || string.CompareOrdinal(material, topMaterial) < 0))
            {
                topNormalZ = normalZ;
                topMaterial = material;
            }
        }

        if (brush.EdgeCount.Count > 5 && brush.AxialMaterialNum.Count > 5 && brush.EdgeCount[5] != 0)
            Consider(brush.AxialMaterialNum[5], 1);
        foreach (var side in brush.Sides)
        {
            var plane = side.Plane ?? throw new InvalidDataException("A water volume has an unresolved collision plane.");
            Consider(side.MaterialNum, plane.Normal.Z);
        }
        return topMaterial;
    }

    private static string Vector(float x, float y, float z) => $"({Number(x)}, {Number(y)}, {Number(z)})";
    private static string Number(float value) => float.IsFinite(value)
        ? ((double)value).ToString("0.#########", CultureInfo.InvariantCulture)
        : throw new InvalidDataException("A water volume contains a non-finite value.");
}
