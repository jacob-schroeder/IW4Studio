using System.Numerics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Editing;

internal static class XModelEditing
{
    internal static MapEntity Place(EditorSession session, XModelSource source, Vector3 position, Vector3? normal = null)
    {
        _ = source.Document;
        var entity = new MapEntity();
        entity.Properties["classname"] = "misc_model";
        entity.Properties["model"] = source.Name;
        entity.Properties["origin"] = "0 0 0";
        entity.Properties["angles"] = "0 0 0";
        Vector3 supportNormal = normal is { } surfaceNormal ? Vector3.Normalize(surfaceNormal) : Vector3.UnitZ;
        if (normal is not null)
        {
            Vector3 axis = Vector3.Cross(Vector3.UnitZ, supportNormal);
            float dot = Math.Clamp(Vector3.Dot(Vector3.UnitZ, supportNormal), -1, 1);
            if (axis.LengthSquared() > 0.000001f)
                EntityOrientation.Transform(entity, Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(dot)));
            else if (dot < 0)
                EntityOrientation.Transform(entity, Matrix4x4.CreateRotationX(MathF.PI));
        }
        Matrix4x4 rotation = XModelGeometry.Transform(entity);
        float offset = source.Document.Vertices.Min(vertex => Vector3.Dot(Vector3.Transform(vertex.Position, rotation), supportNormal));
        SetOrigin(entity, position - supportNormal * offset);
        session.Edit(() =>
        {
            session.Document.Entities.Add(entity);
            session.Selection.Set(entity);
        });
        return entity;
    }

    internal static int FindInstances(EditorSession session, string name)
    {
        MapEntity[] instances = session.Document.Entities.Where(entity => XModelGeometry.IsModel(entity) &&
            entity.Properties["model"].Equals(name, StringComparison.Ordinal)).ToArray();
        session.SelectRange(instances);
        return session.Selection.Count;
    }

    internal static int ReplaceInstances(EditorSession session, string fromName, XModelSource replacement, bool selectedOnly)
    {
        _ = replacement.Document;
        IEnumerable<MapEntity> candidates = selectedOnly ? session.Selection.Items.OfType<MapEntity>() : session.Document.Entities;
        MapEntity[] instances = candidates.Where(entity => XModelGeometry.IsModel(entity) &&
            session.Visibility.CanSelect(session.Document, entity) &&
            entity.Properties["model"].Equals(fromName, StringComparison.Ordinal) && fromName != replacement.Name).ToArray();
        if (instances.Length > 0)
            session.Edit(() => { foreach (var entity in instances) entity.Properties["model"] = replacement.Name; });
        return instances.Length;
    }

    internal static int DropToSurface(EditorSession session, Func<string, XModelSource?> resolveModel,
        Func<string, MaterialSource?>? resolveMaterial)
    {
        var selected = session.Selection.Items.OfType<MapEntity>().Where(entity => XModelGeometry.IsModel(entity) &&
            session.Visibility.CanSelect(session.Document, entity)).ToHashSet();
        MapDocument scene = session.Scene.Document;
        var excluded = scene.Entities.Where(entity => session.Scene.Owner(entity) is MapEntity source && selected.Contains(source)).ToHashSet();
        var positions = new List<(MapEntity Entity, Vector3 Position)>();
        foreach (MapEntity entity in selected)
        {
            if (resolveModel(entity.Properties["model"]) is not { } source)
                throw new ArgumentException($"Model '{entity.Properties["model"]}' is unavailable. Load its raw asset folder first.");
            var bounds = XModelGeometry.Bounds(entity, source);
            Vector3 origin = EditorSession.EntityOrigin(entity);
            Vector3 rayOrigin = new(origin.X, origin.Y, bounds.Min.Z + 0.01f);
            if (SurfaceRaycast.TryHit(scene, rayOrigin, -Vector3.UnitZ, resolveModel, resolveMaterial, excluded,
                    out Vector3 hit, out _))
            {
                Vector3 destination = origin + Vector3.UnitZ * (hit.Z - bounds.Min.Z);
                if (Vector3.DistanceSquared(destination, origin) > 0.000001f) positions.Add((entity, destination));
            }
        }
        if (positions.Count > 0)
            session.Edit(() => { foreach (var (entity, position) in positions) SetOrigin(entity, position); });
        return positions.Count;
    }

    private static void SetOrigin(MapEntity entity, Vector3 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            throw new ArgumentException("The model position must be finite.");
        entity.Properties["origin"] = FormattableString.Invariant($"{position.X:G9} {position.Y:G9} {position.Z:G9}");
    }
}
