using System.Globalization;
using System.Numerics;
using IW4.AssetExchange.XModel;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Rendering;

internal static class XModelGeometry
{
    internal static bool IsModel(MapEntity entity) => entity.ClassName is not ("worldspawn" or "misc_prefab") &&
        entity.Properties.TryGetValue("model", out string? name) && name.Length > 0 && name[0] is not ('*' or '?');

    internal static Matrix4x4 Transform(MapEntity entity) => Matrix4x4.CreateScale(Scale(entity)) *
        EntityOrientation.Rotation(entity) * Matrix4x4.CreateTranslation(EditorSession.EntityOrigin(entity));

    internal static Vector3 Scale(MapEntity entity)
    {
        if (entity.Properties.TryGetValue("modelscale_vec", out string? text))
        {
            string[] values = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length != 3) throw new ArgumentException("Model scale requires three positive numbers.");
            return new Vector3(Number(values[0]), Number(values[1]), Number(values[2]));
        }
        return new Vector3(entity.Properties.TryGetValue("modelscale", out text) ? Number(text) : 1);
    }

    internal static (Vector3 Min, Vector3 Max) Bounds(MapEntity entity, XModelSource source)
    {
        Matrix4x4 transform = Transform(entity);
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        foreach (var vertex in source.Document.Vertices)
        {
            Vector3 point = Vector3.Transform(vertex.Position, transform);
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        return (min, max);
    }

    internal static IEnumerable<(string Material, SceneVertex A, SceneVertex B, SceneVertex C)> GetTriangles(
        MapEntity entity, XModelSource source)
    {
        Matrix4x4 transform = Transform(entity);
        if (!Matrix4x4.Invert(transform, out var inverse)) throw new ArgumentException("The model transform must be invertible.");
        Matrix4x4 normalTransform = Matrix4x4.Transpose(inverse);
        foreach (var triangle in GetLocalTriangles(source))
            yield return (triangle.Material, Vertex(triangle.A), Vertex(triangle.B), Vertex(triangle.C));

        SceneVertex Vertex(SceneVertex vertex)
        {
            Vector3 normal = Vector3.TransformNormal(vertex.Normal, normalTransform);
            if (normal.LengthSquared() > 0.000001f) normal = Vector3.Normalize(normal);
            return new SceneVertex(Vector3.Transform(vertex.Position, transform), normal, vertex.Uv, vertex.Color);
        }
    }

    internal static IEnumerable<(string Material, SceneVertex A, SceneVertex B, SceneVertex C)> GetLocalTriangles(
        XModelSource source)
    {
        XModelExportDocument document = source.Document;
        foreach (var triangle in document.Triangles)
            yield return (document.Materials[triangle.MaterialIndex].Name,
                // XMODEL_EXPORT uses clockwise-front winding; Radiant expects counter-clockwise.
                Vertex(triangle.First), Vertex(triangle.Third), Vertex(triangle.Second));

        SceneVertex Vertex(XModelExportCorner corner) => new(
            document.Vertices[corner.VertexIndex].Position, corner.Normal, corner.Uv0, corner.Color);
    }

    private static float Number(string text) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
        float.IsFinite(value) && value > 0 ? value : throw new ArgumentException("Model scale must contain positive finite numbers.");
}
