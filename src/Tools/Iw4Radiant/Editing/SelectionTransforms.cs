using System.Numerics;
using Iw4Radiant.MapSource;

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
                    foreach (MapBrush brush in entity.Brushes) brush.Transform(transform, session.TextureLock);
                    foreach (MapTerrain terrain in entity.Terrains) terrain.Transform(transform);
                    Vector3 position = Vector3.Transform(EditorSession.EntityOrigin(entity), transform);
                    if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
                        throw new ArgumentException("The transformed entity position must be finite.");
                    entity.Properties["origin"] = FormattableString.Invariant($"{position.X:G9} {position.Y:G9} {position.Z:G9}");
                    EntityOrientation.Transform(entity, transform);
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
}
