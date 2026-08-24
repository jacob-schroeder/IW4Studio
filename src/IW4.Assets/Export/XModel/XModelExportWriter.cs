using System.Globalization;
using System.Numerics;

namespace IW4.Assets.Export.XModel;

/// <summary>Invariant-culture writer for OpenAssetTools XMODEL_EXPORT version 6.</summary>
public static class XModelExportWriter
{
    // BaseMat quaternions are recovered single-precision data, not values to normalize.
    private const float UnitQuaternionLengthSquaredTolerance = 0.001f;

    public static void Write(TextWriter writer, XModelExportDocument document)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);

        writer.WriteLine("MODEL");
        writer.WriteLine("VERSION 6");
        writer.WriteLine();

        writer.WriteLine($"NUMBONES {document.Bones.Count}");
        for (int index = 0; index < document.Bones.Count; index++)
        {
            XModelExportBone bone = document.Bones[index];
            writer.WriteLine($"BONE {index} {bone.ParentIndex} {Quote(bone.Name)}");
        }
        writer.WriteLine();
        for (int index = 0; index < document.Bones.Count; index++)
        {
            XModelExportBone bone = document.Bones[index];
            writer.WriteLine($"BONE {index}");
            writer.WriteLine($"OFFSET {Vector(bone.GlobalOffset)}");
            writer.WriteLine("SCALE 1.000000, 1.000000, 1.000000");
            WriteRotation(writer, bone.GlobalRotation);
            writer.WriteLine();
        }

        writer.WriteLine($"NUMVERTS {document.Vertices.Count}");
        for (int index = 0; index < document.Vertices.Count; index++)
        {
            XModelExportVertex vertex = document.Vertices[index];
            writer.WriteLine($"VERT {index}");
            writer.WriteLine($"OFFSET {Vector(vertex.Position)}");
            writer.WriteLine($"BONES {vertex.Weights.Count}");
            foreach (XModelExportBoneWeight weight in vertex.Weights)
                writer.WriteLine($"BONE {weight.BoneIndex} {Float(weight.Weight)}");
            writer.WriteLine();
        }

        writer.WriteLine($"NUMFACES {document.Triangles.Count}");
        foreach (XModelExportTriangle triangle in document.Triangles)
        {
            writer.WriteLine($"TRI {triangle.ObjectIndex} {triangle.MaterialIndex} 0 0");
            WriteCorner(writer, triangle.First);
            WriteCorner(writer, triangle.Second);
            WriteCorner(writer, triangle.Third);
            writer.WriteLine();
        }

        writer.WriteLine($"NUMOBJECTS {document.Objects.Count}");
        for (int index = 0; index < document.Objects.Count; index++)
            writer.WriteLine($"OBJECT {index} {Quote(document.Objects[index].SurfaceIdentity)}");
        writer.WriteLine();

        writer.WriteLine($"NUMMATERIALS {document.Materials.Count}");
        for (int index = 0; index < document.Materials.Count; index++)
            WriteMaterial(writer, index, document.Materials[index]);
    }

    private static void WriteRotation(TextWriter writer, Quaternion q)
    {
        float xx = q.X * q.X;
        float yy = q.Y * q.Y;
        float zz = q.Z * q.Z;
        float xy = q.X * q.Y;
        float xz = q.X * q.Z;
        float yz = q.Y * q.Z;
        float wx = q.W * q.X;
        float wy = q.W * q.Y;
        float wz = q.W * q.Z;

        if (!float.IsFinite(xx) || !float.IsFinite(yy) ||
            !float.IsFinite(zz) || !float.IsFinite(xy) ||
            !float.IsFinite(xz) || !float.IsFinite(yz) ||
            !float.IsFinite(wx) || !float.IsFinite(wy) ||
            !float.IsFinite(wz))
        {
            throw new InvalidDataException(
                "XMODEL_EXPORT bone rotation cannot be represented as a finite matrix.");
        }

        writer.WriteLine($"X {Vector(1f - (2f * (yy + zz)), 2f * (xy + wz), 2f * (xz - wy))}");
        writer.WriteLine($"Y {Vector(2f * (xy - wz), 1f - (2f * (xx + zz)), 2f * (yz + wx))}");
        writer.WriteLine($"Z {Vector(2f * (xz + wy), 2f * (yz - wx), 1f - (2f * (xx + yy)))}");
    }

    private static void WriteCorner(TextWriter writer, XModelExportCorner corner)
    {
        writer.WriteLine($"VERT {corner.VertexIndex}");
        writer.WriteLine($"NORMAL {Vector(corner.Normal, separator: ' ')}");
        writer.WriteLine($"COLOR {Vector(corner.Color)}");
        writer.WriteLine($"UV 1 {Float(corner.Uv0.X)} {Float(corner.Uv0.Y)}");
    }

    private static void WriteMaterial(
        TextWriter writer,
        int index,
        XModelExportMaterial material)
    {
        writer.WriteLine($"MATERIAL {index} {Quote(material.Name)} \"Phong\" {Quote(material.ColorMapPath)}");
        writer.WriteLine("COLOR 0.000000 0.000000 0.000000 1.000000");
        writer.WriteLine("TRANSPARENCY 0.000000 0.000000 0.000000 1.000000");
        writer.WriteLine("AMBIENTCOLOR 0.000000 0.000000 0.000000 1.000000");
        writer.WriteLine("INCANDESCENCE 0.000000 0.000000 0.000000 1.000000");
        writer.WriteLine("COEFFS 0.800000 0.000000");
        writer.WriteLine("GLOW 0.000000 0");
        writer.WriteLine("REFRACTIVE 6 1.000000");
        writer.WriteLine("SPECULARCOLOR -1.000000 -1.000000 -1.000000 1.000000");
        writer.WriteLine("REFLECTIVECOLOR -1.000000 -1.000000 -1.000000 1.000000");
        writer.WriteLine("REFLECTIVE -1 -1.000000");
        writer.WriteLine("BLINN -1.000000 -1.000000");
        writer.WriteLine("PHONG -1.000000");
        writer.WriteLine();
    }

    private static string Vector(Vector3 value, char separator = ',') =>
        separator == ','
            ? $"{Float(value.X)}, {Float(value.Y)}, {Float(value.Z)}"
            : $"{Float(value.X)} {Float(value.Y)} {Float(value.Z)}";

    private static string Vector(float x, float y, float z) =>
        $"{Float(x)}, {Float(y)}, {Float(z)}";

    private static string Vector(Vector4 value) =>
        $"{Float(value.X)} {Float(value.Y)} {Float(value.Z)} {Float(value.W)}";

    private static string Float(float value) =>
        value.ToString("F6", CultureInfo.InvariantCulture);

    private static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal).Replace("\f", "\\f", StringComparison.Ordinal)}\"";

    private static void Validate(XModelExportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document.Bones);
        ArgumentNullException.ThrowIfNull(document.Vertices);
        ArgumentNullException.ThrowIfNull(document.Triangles);
        ArgumentNullException.ThrowIfNull(document.Objects);
        ArgumentNullException.ThrowIfNull(document.Materials);
        for (int index = 0; index < document.Bones.Count; index++)
        {
            XModelExportBone bone = document.Bones[index] ?? throw new InvalidDataException($"XMODEL_EXPORT bone {index} is null.");
            ValidateString(bone.Name, $"bone {index} name");
            if (bone.ParentIndex < -1 || bone.ParentIndex >= index)
                throw new InvalidDataException($"XMODEL_EXPORT bone {index} has an invalid parent.");
            RequireFinite(bone.GlobalOffset, $"bone {index} offset");
            RequireFinite(bone.GlobalRotation, $"bone {index} rotation");
            float lengthSquared = bone.GlobalRotation.LengthSquared();
            if (!float.IsFinite(lengthSquared) ||
                MathF.Abs(lengthSquared - 1f) >
                UnitQuaternionLengthSquaredTolerance)
            {
                throw new InvalidDataException(
                    $"XMODEL_EXPORT bone {index} rotation is not a unit quaternion.");
            }
        }
        for (int index = 0; index < document.Vertices.Count; index++)
        {
            XModelExportVertex vertex = document.Vertices[index] ?? throw new InvalidDataException($"XMODEL_EXPORT vertex {index} is null.");
            RequireFinite(vertex.Position, $"vertex {index} position");
            if (vertex.Weights is null || vertex.Weights.Count == 0)
                throw new InvalidDataException($"XMODEL_EXPORT vertex {index} has no weights.");
            float total = 0f;
            foreach (XModelExportBoneWeight weight in vertex.Weights)
            {
                if (weight is null || weight.BoneIndex < 0 || weight.BoneIndex >= document.Bones.Count || !float.IsFinite(weight.Weight) || weight.Weight < 0f)
                    throw new InvalidDataException($"XMODEL_EXPORT vertex {index} has an invalid bone weight.");
                total += weight.Weight;
            }
            if (!float.IsFinite(total) || MathF.Abs(total - 1f) > 0.00001f)
                throw new InvalidDataException($"XMODEL_EXPORT vertex {index} weights are not normalized.");
        }
        foreach (XModelExportTriangle triangle in document.Triangles)
        {
            if (triangle is null || triangle.ObjectIndex < 0 || triangle.ObjectIndex >= document.Objects.Count || triangle.MaterialIndex < 0 || triangle.MaterialIndex >= document.Materials.Count)
                throw new InvalidDataException("XMODEL_EXPORT triangle has an invalid object or material.");
            ValidateCorner(triangle.First, document.Vertices.Count);
            ValidateCorner(triangle.Second, document.Vertices.Count);
            ValidateCorner(triangle.Third, document.Vertices.Count);
        }
        foreach (XModelExportObject value in document.Objects)
            ValidateString(value?.SurfaceIdentity, "object surface identity");
        foreach (XModelExportMaterial value in document.Materials)
        {
            ValidateString(value?.Name, "material name");
            ValidateString(value?.ColorMapPath, "material color-map path", allowEmpty: true);
        }
    }

    private static void ValidateCorner(XModelExportCorner? value, int vertexCount)
    {
        if (value is null || value.VertexIndex < 0 || value.VertexIndex >= vertexCount)
            throw new InvalidDataException("XMODEL_EXPORT triangle has an invalid corner vertex.");
        RequireFinite(value.Normal, "triangle normal");
        RequireFinite(value.Color, "triangle color");
        RequireFinite(value.Uv0, "triangle UV");
    }

    private static void ValidateString(string? value, string field, bool allowEmpty = false)
    {
        if (value is null || (!allowEmpty && value.Length == 0) || value.Any(char.IsControl))
            throw new InvalidDataException($"XMODEL_EXPORT {field} contains an unsupported control character or is empty.");
    }

    private static void RequireFinite(Vector2 value, string field)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            throw new InvalidDataException($"XMODEL_EXPORT {field} is non-finite.");
    }

    private static void RequireFinite(Vector3 value, string field)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new InvalidDataException($"XMODEL_EXPORT {field} is non-finite.");
    }

    private static void RequireFinite(Vector4 value, string field)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
            throw new InvalidDataException($"XMODEL_EXPORT {field} is non-finite.");
    }

    private static void RequireFinite(Quaternion value, string field)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
            throw new InvalidDataException($"XMODEL_EXPORT {field} is non-finite.");
    }
}
