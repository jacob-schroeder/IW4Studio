using System.Numerics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Editing;

internal static class SelectionTransforms
{
    internal static void Translate(EditorSession session, Vector3 delta) => Apply(session, Matrix4x4.CreateTranslation(delta));

    // The caller owns the edit transaction, so drag updates produce a single undo step.
    internal static void Apply(EditorSession session, Matrix4x4 transform)
    {
        if (!session.CanTransformSelection)
            throw new ArgumentException("Select whole objects or vertices to transform.");
        object[] items = session.Selection.Items.ToArray();
        foreach (object item in items)
        {
            switch (item)
            {
                case MapBrush brush: brush.Transform(transform, session.TextureLock); break;
                case MapTerrain terrain: terrain.Transform(transform); break;
                case MapEntity entity:
                    ApplyEntity(entity, transform, session.TextureLock);
                    break;
            }
        }
        foreach (var group in items.OfType<BrushVertexSelection>().GroupBy(vertex => vertex.Brush))
        {
            var positions = group.ToDictionary(vertex => vertex.Position, vertex => Vector3.Transform(vertex.Position, transform));
            BrushVertexEditing.MoveVertices(group.Key, positions, session.TextureLock);
            foreach (BrushVertexSelection vertex in group) vertex.Position = positions[vertex.Position];
        }
        foreach (var group in items.OfType<TerrainVertexSelection>().GroupBy(vertex => vertex.Terrain))
            group.Key.Transform(transform, group.Select(vertex => vertex.Index).ToArray());
    }

    internal static void ApplyEntity(MapEntity entity, Matrix4x4 transform, bool textureLock)
    {
        if (entity.PreservedPrimitives.Count > 0)
            throw new ArgumentException("This entity contains unsupported source primitives and cannot be transformed without losing their placement.");
        if (XModelGeometry.IsModel(entity) || entity.ClassName == "misc_prefab")
        {
            if (!Matrix4x4.Decompose(transform, out Vector3 scale, out _, out _) || scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0 ||
                MathF.Abs(scale.X - scale.Y) > 0.00001f || MathF.Abs(scale.X - scale.Z) > 0.00001f)
                throw new ArgumentException("Model and prefab instances require a positive uniform scale. Set the same scale on all three axes.");
            if (MathF.Abs(scale.X - 1) > 0.000001f)
            {
                Vector3 result = XModelGeometry.Scale(entity) * scale.X;
                if (!BrushGeometry.IsFinite(result) || result.X <= 0 || result.Y <= 0 || result.Z <= 0)
                    throw new ArgumentException("The transformed instance scale must remain positive and finite.");
                if (entity.Properties.ContainsKey("modelscale_vec"))
                    entity.Properties["modelscale_vec"] = FormattableString.Invariant($"{result.X:G9} {result.Y:G9} {result.Z:G9}");
                else entity.Properties["modelscale"] = result.X.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        foreach (MapBrush brush in entity.Brushes) brush.Transform(transform, textureLock);
        foreach (MapTerrain terrain in entity.Terrains) terrain.Transform(transform);
        if (entity.ClassName == "worldspawn") return;
        Vector3 position = Vector3.Transform(EditorSession.EntityOrigin(entity), transform);
        if (!BrushGeometry.IsFinite(position)) throw new ArgumentException("The transformed entity position must be finite.");
        entity.Properties["origin"] = FormattableString.Invariant($"{position.X:G9} {position.Y:G9} {position.Z:G9}");
        EntityOrientation.Transform(entity, transform);
    }
}
