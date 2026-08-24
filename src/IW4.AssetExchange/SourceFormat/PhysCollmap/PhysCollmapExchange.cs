using System.Globalization;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;

namespace IW4.AssetExchange.SourceFormat.PhysCollmap;

/// <summary>
/// Writes the box and cylinder geometry that OpenAssetTools can represent in
/// an IW4 physics-collision map.
/// </summary>
public sealed class PhysCollmapExchange
{
    private const int PhysGeomBox = 1;
    private const int PhysGeomCylinder = 5;

    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        PhysCollmapAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string name = ValidateAsset(asset);
        var output = new SourceOutput(sourceDirectory);
        return output.WriteTextBatch([
            ($"phys_collmaps/{name}.map", writer => WriteMap(writer, asset))
        ]);
    }

    private static string ValidateAsset(PhysCollmapAsset asset)
    {
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "PhysCollmap");
        if (asset.Count < 0 || asset.Geoms.Count != asset.Count)
        {
            throw new InvalidDataException(
                $"PhysCollmap '{assetName}' declares {asset.Count} geometries but materialized {asset.Geoms.Count}.");
        }

        foreach (PhysGeomInfo geom in asset.Geoms)
        {
            int requiredOrientationCount = geom.Type switch
            {
                PhysGeomBox => 3,
                PhysGeomCylinder => 1,
                _ => throw new NotSupportedException(
                    $"PhysCollmap '{assetName}' geometry type {geom.Type} has no complete OpenAssetTools map representation.")
            };
            if (geom.Orientation.Count < requiredOrientationCount)
            {
                throw new InvalidDataException(
                    $"PhysCollmap '{assetName}' geometry type {geom.Type} has only {geom.Orientation.Count} orientation rows.");
            }

            EnsureFinite(assetName, geom.Bounds.MidPoint, "midpoint");
            EnsureFinite(assetName, geom.Bounds.HalfSize, "half-size");
            for (int index = 0; index < requiredOrientationCount; index++)
                EnsureFinite(assetName, geom.Orientation[index], $"orientation {index}");
        }

        return assetName;
    }

    private static void WriteMap(TextWriter writer, PhysCollmapAsset asset)
    {
        writer.WriteLine("iwmap 4");
        writer.WriteLine("\"000_Global\" flags active");
        writer.WriteLine("\"The Map\" flags");
        if (asset.Count == 0)
            return;

        writer.WriteLine("// entity 0");
        writer.WriteLine("{");
        writer.WriteLine("  \"classname\" \"worldspawn\"");
        for (int index = 0; index < asset.Geoms.Count; index++)
        {
            PhysGeomInfo geom = asset.Geoms[index];
            writer.WriteLine($"  // brush {index}");
            writer.WriteLine("  {");
            switch (geom.Type)
            {
                case PhysGeomBox:
                    WriteBox(writer, geom);
                    break;
                case PhysGeomCylinder:
                    WriteCylinder(writer, geom);
                    break;
            }
            writer.WriteLine("  }");
        }
        writer.WriteLine("}");
    }

    private static void WriteBox(TextWriter writer, PhysGeomInfo geom)
    {
        writer.WriteLine("    physics_box");
        writer.WriteLine("    {");
        writer.Write("      ");
        WriteVec3(writer, geom.Orientation[0]);
        writer.Write(' ');
        WriteVec3(writer, geom.Orientation[1]);
        writer.Write(' ');
        WriteVec3(writer, geom.Orientation[2]);
        writer.Write(' ');
        WriteVec3(writer, geom.Bounds.MidPoint);
        writer.Write(' ');
        WriteVec3(writer, geom.Bounds.HalfSize);
        writer.WriteLine();
        writer.WriteLine("    }");
    }

    private static void WriteCylinder(TextWriter writer, PhysGeomInfo geom)
    {
        writer.WriteLine("    physics_cylinder");
        writer.WriteLine("    {");
        writer.Write("      ");
        WriteVec3(writer, geom.Orientation[0]);
        writer.Write(' ');
        WriteVec3(writer, geom.Bounds.MidPoint);
        writer.Write(' ');
        WriteFloat(writer, geom.Bounds.HalfSize.Z * 2f);
        writer.Write(' ');
        WriteFloat(writer, geom.Bounds.HalfSize.X);
        writer.WriteLine();
        writer.WriteLine("    }");
    }

    private static void WriteVec3(TextWriter writer, Vec3 value)
    {
        WriteFloat(writer, value.X);
        writer.Write(' ');
        WriteFloat(writer, value.Y);
        writer.Write(' ');
        WriteFloat(writer, value.Z);
    }

    private static void WriteFloat(TextWriter writer, float value) =>
        writer.Write(value.ToString("F6", CultureInfo.InvariantCulture));

    private static void EnsureFinite(string assetName, Vec3 value, string field)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z))
        {
            throw new InvalidDataException(
                $"PhysCollmap '{assetName}' has a non-finite {field}.");
        }
    }
}
