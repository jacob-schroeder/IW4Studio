using System.Numerics;
using Iw4Radiant.Compilation;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using Iw4Radiant.Rendering;
using JoltPhysicsSharp;

namespace Iw4Radiant.Viewports.Camera;

// Editor-only rigid bodies. The authored entities are changed only by Apply in CameraViewport.
internal sealed class CameraPrefabPlacementSimulation : IDisposable
{
    private static readonly ObjectLayer StaticLayer = 0, MovingLayer = 1;
    private const float Gravity = 800;
    private readonly ObjectLayerPairFilterTable _pairFilter;
    private readonly BroadPhaseLayerInterfaceTable _broadPhase;
    private readonly ObjectVsBroadPhaseLayerFilterTable _broadPhaseFilter;
    private readonly PhysicsSystem _physics;
    private readonly JobSystemThreadPool _jobs;
    private readonly List<Shape> _shapes;
    private readonly List<BodyID> _bodyIds;
    private readonly List<PlacedBody> _placed;
    private readonly IReadOnlyDictionary<MapEntity, MapEntity> _preview;
    private readonly IReadOnlyDictionary<MapBrush, MapEntity> _clipOwners;
    private readonly Dictionary<MapEntity, Matrix4x4> _drawTransforms = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;
    private const int MaximumMovingEntities = 128;
    private const int MaximumLinkedClips = 64;

    private sealed record PreparedBody(MapEntity Source, MapEntity Pose, Vector3 Origin, Quaternion Rotation,
        Vector3[][] Hulls, (Vector3 Center, Vector3 HalfExtent)? Box, MapBrush[] Clips);

    private sealed record PreparedWorld(PreparedBody[] Moving, Vector3[][] StaticBrushes,
        Vector3[] TerrainVertices, IndexedTriangle[] TerrainTriangles);

    private sealed class PlacedBody(MapEntity source, MapEntity pose, BodyID id, Vector3 origin,
        Quaternion rotation, MapBrush[] clips)
    {
        internal MapEntity Source { get; } = source;
        internal MapEntity Pose { get; } = pose;
        internal BodyID Id { get; } = id;
        internal Vector3 InitialOrigin { get; } = origin;
        internal Quaternion InitialRotation { get; } = rotation;
        internal MapBrush[] Clips { get; } = clips;
        internal Vector3 Origin { get; set; } = origin;
        internal Quaternion Rotation { get; set; } = rotation;
    }

    private CameraPrefabPlacementSimulation(ObjectLayerPairFilterTable pairFilter,
        BroadPhaseLayerInterfaceTable broadPhase, ObjectVsBroadPhaseLayerFilterTable broadPhaseFilter,
        PhysicsSystem physics, JobSystemThreadPool jobs, List<Shape> shapes, List<BodyID> bodyIds,
        List<PlacedBody> placed)
    {
        _pairFilter = pairFilter;
        _broadPhase = broadPhase;
        _broadPhaseFilter = broadPhaseFilter;
        _physics = physics;
        _jobs = jobs;
        _shapes = shapes;
        _bodyIds = bodyIds;
        _placed = placed;
        _preview = placed.ToDictionary<PlacedBody, MapEntity, MapEntity>(body => body.Source,
            body => body.Pose, ReferenceEqualityComparer.Instance);
        var clipOwners = new Dictionary<MapBrush, MapEntity>(ReferenceEqualityComparer.Instance);
        foreach (PlacedBody body in placed)
        {
            _drawTransforms.Add(body.Source, Matrix4x4.Identity);
            foreach (MapBrush clip in body.Clips) clipOwners.Add(clip, body.Source);
        }
        _clipOwners = clipOwners;
    }

    internal IReadOnlyDictionary<MapEntity, MapEntity> Preview => _preview;
    internal IReadOnlyDictionary<MapBrush, MapEntity> ClipOwners => _clipOwners;
    internal IReadOnlyDictionary<MapEntity, Matrix4x4> DrawTransforms => _drawTransforms;
    internal int ObjectCount => _placed.Count;
    internal bool HasChanges { get; private set; }

    internal static bool CanStart(EditorSession session)
    {
        object[] selection = session.Selection.Items.ToArray();
        if (selection.Length == 0) return false;
        MapEntity[] entities = selection.OfType<MapEntity>().ToArray();
        MapBrush[] clips = selection.OfType<MapBrush>().ToArray();
        if (entities.Length == 0 || entities.Length > MaximumMovingEntities ||
            entities.Length + clips.Length != selection.Length ||
            entities.Any(entity => !session.Document.Entities.Contains(entity) ||
                !session.Visibility.CanSelect(session.Document, entity) ||
                entity.ClassName is not ("misc_prefab" or "misc_model" or "script_model") ||
                entity.ClassName == "script_model" && !XModelGeometry.IsModel(entity) ||
                entity.PreservedPrimitives.Count != 0 || entity.Brushes.Count != 0 || entity.Terrains.Count != 0))
            return false;
        return clips.Length == 0 || clips.Length <= MaximumLinkedClips && entities.Length == 1 &&
            entities[0].ClassName is ("misc_model" or "script_model") && clips.All(clip =>
                session.Document.World.Brushes.Contains(clip) &&
                session.Visibility.CanSelect(session.Document, clip) &&
                clip.Faces.Count > 0 && clip.Faces.All(face => ClipBrushMaterial.IsPlayerClip(face.Material)));
    }

    internal static Task<CameraPrefabPlacementSimulation> CreateAsync(EditorSession session,
        Func<string, MaterialSource?> resolveMaterial, Func<string, XModelSource?>? resolveModel,
        CancellationToken cancellationToken)
    {
        PreparedWorld prepared = Prepare(session, resolveMaterial, resolveModel);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => CreateNative(prepared, cancellationToken), cancellationToken);
    }

    private static PreparedWorld Prepare(EditorSession session,
        Func<string, MaterialSource?> resolveMaterial, Func<string, XModelSource?>? resolveModel)
    {
        MapEntity[] selected = session.Selection.Items.OfType<MapEntity>().ToArray();
        MapBrush[] linkedClips = session.Selection.Items.OfType<MapBrush>().ToArray();
        if (linkedClips.Length > MaximumLinkedClips)
            throw new NotSupportedException($"Physics placement supports at most {MaximumLinkedClips} selected player clip brushes with one model.");
        if (!CanStart(session))
            throw new ArgumentException("Select 1–128 whole, visible prefab or model instances, or one model with 1–64 whole, visible world player clip brushes. Other geometry and multiple models with clips are unavailable.");

        var moving = new List<PreparedBody>();
        foreach (MapEntity source in selected)
        {
            Vector3 origin = EditorSession.EntityOrigin(source);
            Quaternion rotation = Quaternion.CreateFromRotationMatrix(EntityOrientation.Rotation(source));
            if (!Finite(origin) || !Finite(rotation)) throw new ArgumentException("The selected instance has an invalid transform.");
            var pose = new MapEntity();
            foreach (var pair in source.Properties) pose.Properties.Add(pair.Key, pair.Value);
            var hulls = new List<Vector3[]>();
            (Vector3 Center, Vector3 HalfExtent)? box = null;
            if (source.ClassName == "misc_prefab")
            {
                MapDocument preview = session.Prefabs.GetPreview(source, session.FilePath) ??
                    throw new ArgumentException(session.Prefabs.Error(source, session.FilePath) ?? "The prefab source is unavailable.");
                if (preview.Terrains.Any() || preview.Entities.Any(entity =>
                    entity.ClassName is not ("worldspawn" or "func_group") || entity.PreservedPrimitives.Count != 0))
                    throw new NotSupportedException("Physics prefab bodies support brush-only world/group entities; terrain, point entities, lights and embedded models are unavailable.");
                Quaternion inverse = Quaternion.Inverse(rotation);
                foreach (MapBrush brush in preview.Brushes)
                {
                    BrushGeometry.Validate(brush);
                    if (BrushContents.ReadForCompilation(brush) is BrushKind.NonColliding or BrushKind.WeaponClip ||
                        brush.Faces.Any(UnsupportedMovingFace))
                        throw new NotSupportedException("Moving prefab brushes must be solid; sky, water and clip surfaces are unavailable in Physics placement.");
                    Vector3[] points = brush.GetVertices().Select(point => Vector3.Transform(point - origin, inverse)).ToArray();
                    if (points.Length < 4 || points.Any(point => !Finite(point)))
                        throw new InvalidDataException("A prefab brush has invalid convex collision geometry.");
                    hulls.Add(points);
                }
                if (hulls.Count == 0) throw new NotSupportedException("Physics prefab placement needs at least one brush in each prefab.");
            }
            else
            {
                if (!source.Properties.TryGetValue("model", out string? name) || string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("A selected model has no model asset reference.");
                XModelSource model = resolveModel?.Invoke(name) ?? throw new ArgumentException($"Model '{name}' is unavailable. Load its raw asset folder first.");
                foreach (string materialName in model.Document.Triangles.Select(triangle =>
                             model.Document.Materials[triangle.MaterialIndex].Name).Distinct(StringComparer.Ordinal))
                {
                    MaterialSource? material = resolveMaterial(materialName);
                    if (material is { IsSky: true } or { IsWater: true })
                        throw new NotSupportedException($"Moving model '{name}' uses sky or water material '{materialName}', which Physics placement cannot preview.");
                }
                if (linkedClips.Length == 0)
                {
                    var bounds = model.Bounds;
                    Vector3 scale = XModelGeometry.Scale(source);
                    Vector3 center = (bounds.Min + bounds.Max) / 2 * scale;
                    Vector3 half = (bounds.Max - bounds.Min) / 2 * scale;
                    if (!Finite(center) || !Finite(half) || half.X <= 0 || half.Y <= 0 || half.Z <= 0)
                        throw new NotSupportedException($"Model '{name}' needs nondegenerate local bounds for a box collider.");
                    box = (center, half);
                }
                else
                {
                    Quaternion inverse = Quaternion.Inverse(rotation);
                    foreach (MapBrush clip in linkedClips)
                    {
                        BrushGeometry.Validate(clip);
                        Vector3[] points = clip.GetVertices()
                            .Select(point => Vector3.Transform(point - origin, inverse)).ToArray();
                        if (points.Length < 4 || points.Any(point => !Finite(point)))
                            throw new InvalidDataException("A selected player clip brush has invalid convex collision geometry.");
                        hulls.Add(points);
                    }
                }
            }
            moving.Add(new PreparedBody(source, pose, origin, rotation, hulls.ToArray(), box, linkedClips));
        }

        var selectedSet = selected.ToHashSet();
        var selectedClipSet = new HashSet<MapBrush>(linkedClips, ReferenceEqualityComparer.Instance);
        var collision = CameraStaticPhysicsCollision.Prepare(
            session, resolveMaterial, selectedSet, selectedClipSet, "Physics placement");
        if (collision.Brushes.Length + moving.Count + (collision.TerrainTriangles.Length > 0 ? 1 : 0) > 32767)
            throw new NotSupportedException("Physics placement supports at most 32767 collision bodies.");
        return new PreparedWorld(moving.ToArray(), collision.Brushes, collision.TerrainVertices,
            collision.TerrainTriangles);

        bool UnsupportedMovingFace(MapFace face)
        {
            if (ClipBrushMaterial.IsPlayerClip(face.Material)) return true;
            if (CaulkMaterial.IsCaulk(face.Material)) return false;
            MaterialSource material = resolveMaterial(face.Material) ??
                throw new InvalidDataException($"Physics placement material '{face.Material}' is unavailable.");
            return material.IsSky || material.IsWater;
        }
    }

    private static CameraPrefabPlacementSimulation CreateNative(PreparedWorld prepared, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!CameraWalkSimulation.FoundationAvailable)
                throw new InvalidOperationException("Jolt Physics could not initialize for placement.");
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new InvalidOperationException("The Jolt native library is unavailable for placement on this system.", exception);
        }

        ObjectLayerPairFilterTable? pairFilter = null;
        BroadPhaseLayerInterfaceTable? broadPhase = null;
        ObjectVsBroadPhaseLayerFilterTable? broadPhaseFilter = null;
        PhysicsSystem? physics = null;
        JobSystemThreadPool? jobs = null;
        var shapes = new List<Shape>();
        var ids = new List<BodyID>();
        var placed = new List<PlacedBody>();
        try
        {
            pairFilter = new ObjectLayerPairFilterTable(2);
            pairFilter.EnableCollision(StaticLayer, MovingLayer);
            pairFilter.EnableCollision(MovingLayer, MovingLayer);
            broadPhase = new BroadPhaseLayerInterfaceTable(2, 2);
            broadPhase.MapObjectToBroadPhaseLayer(StaticLayer, 0);
            broadPhase.MapObjectToBroadPhaseLayer(MovingLayer, 1);
            broadPhaseFilter = new ObjectVsBroadPhaseLayerFilterTable(broadPhase, 2, pairFilter, 2);
            physics = new PhysicsSystem(new PhysicsSystemSettings
            {
                MaxBodies = Math.Max(1024, prepared.StaticBrushes.Length + prepared.Moving.Length + 16),
                ObjectLayerPairFilter = pairFilter,
                BroadPhaseLayerInterface = broadPhase,
                ObjectVsBroadPhaseLayerFilter = broadPhaseFilter
            });
            physics.Gravity = -Vector3.UnitZ * Gravity;
            jobs = new JobSystemThreadPool();
            foreach (Vector3[] points in prepared.StaticBrushes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var settings = new ConvexHullShapeSettings(points);
                Shape shape = settings.Create();
                shapes.Add(shape);
                AddBody(shape, Vector3.Zero, Quaternion.Identity, MotionType.Static, StaticLayer, Activation.DontActivate);
            }
            if (prepared.TerrainTriangles.Length > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var settings = new MeshShapeSettings(prepared.TerrainVertices, prepared.TerrainTriangles);
                Shape shape = settings.Create();
                shapes.Add(shape);
                AddBody(shape, Vector3.Zero, Quaternion.Identity, MotionType.Static, StaticLayer, Activation.DontActivate);
            }
            foreach (var item in prepared.Moving)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Shape shape;
                if (item.Box is { } box)
                {
                    using var boxSettings = new BoxShapeSettings(box.HalfExtent);
                    Shape boxShape = boxSettings.Create();
                    shapes.Add(boxShape);
                    using var offset = new RotatedTranslatedShapeSettings(box.Center, Quaternion.Identity, boxShape);
                    shape = offset.Create();
                }
                else if (item.Hulls.Length == 1)
                {
                    using var hull = new ConvexHullShapeSettings(item.Hulls[0]);
                    shape = hull.Create();
                }
                else
                {
                    using var compound = new StaticCompoundShapeSettings();
                    foreach (Vector3[] points in item.Hulls)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var hull = new ConvexHullShapeSettings(points);
                        Shape child = hull.Create();
                        shapes.Add(child);
                        compound.AddShape(Vector3.Zero, Quaternion.Identity, child);
                    }
                    shape = compound.Create();
                }
                shapes.Add(shape);
                BodyID id = AddBody(shape, item.Origin, item.Rotation, MotionType.Dynamic, MovingLayer, Activation.Activate);
                placed.Add(new PlacedBody(item.Source, item.Pose, id, item.Origin, item.Rotation, item.Clips));
            }
            cancellationToken.ThrowIfCancellationRequested();
            physics.OptimizeBroadPhase();
            cancellationToken.ThrowIfCancellationRequested();
            return new CameraPrefabPlacementSimulation(pairFilter, broadPhase, broadPhaseFilter,
                physics, jobs, shapes, ids, placed);

            BodyID AddBody(Shape shape, Vector3 position, Quaternion rotation, MotionType motion,
                ObjectLayer layer, Activation activation)
            {
                using var settings = new BodyCreationSettings(shape, position, rotation, motion, layer);
                BodyID id = physics.BodyInterface.CreateAndAddBody(settings, activation);
                if (id.IsInvalid) throw new InvalidOperationException("Jolt could not allocate a placement collision body.");
                ids.Add(id);
                return id;
            }
        }
        catch
        {
            if (physics is not null)
            {
                foreach (BodyID id in ids) physics.BodyInterface.RemoveAndDestroyBody(id);
                physics.Dispose();
            }
            jobs?.Dispose();
            foreach (Shape shape in shapes) shape.Dispose();
            broadPhaseFilter?.Dispose();
            broadPhase?.Dispose();
            pairFilter?.Dispose();
            throw;
        }

    }

    internal void Step(float seconds)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CameraPrefabPlacementSimulation));
        PhysicsUpdateError error = _physics.Update(seconds, 1, _jobs);
        if (error != PhysicsUpdateError.None)
            throw new InvalidOperationException($"Jolt placement update failed: {error}.");
    }

    internal bool AllSleeping => _placed.All(body => !_physics.BodyInterface.IsActive(body.Id));

    internal bool Reset()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CameraPrefabPlacementSimulation));
        foreach (PlacedBody body in _placed)
        {
            _physics.BodyInterface.SetPositionRotationAndVelocity(body.Id, body.InitialOrigin,
                body.InitialRotation, Vector3.Zero, Vector3.Zero);
            _physics.BodyInterface.ActivateBody(body.Id);
        }
        return CapturePoses();
    }

    internal bool Apply(EditorSession session)
    {
        var changed = _placed.Where(body => Vector3.DistanceSquared(body.Origin, body.InitialOrigin) > 0.000001f ||
            MathF.Abs(Quaternion.Dot(body.Rotation, body.InitialRotation)) < 0.999999f).ToArray();
        if (changed.Length == 0) return false;
        var transforms = changed.Select(body =>
        {
            Matrix4x4 delta = Matrix4x4.Transpose(Matrix4x4.CreateFromQuaternion(body.InitialRotation)) *
                Matrix4x4.CreateFromQuaternion(body.Rotation);
            Matrix4x4 transform = Matrix4x4.CreateTranslation(-body.InitialOrigin) *
                delta * Matrix4x4.CreateTranslation(body.Origin);
            return (Body: body, Transform: transform);
        }).ToArray();
        // Validate all transformed clip volumes before changing any authored entity or brush.
        foreach (var (body, transform) in transforms)
            foreach (MapBrush clip in body.Clips) clip.Clone().Transform(transform, session.TextureLock);
        session.Edit(() =>
        {
            foreach (var (body, transform) in transforms)
            {
                SelectionTransforms.ApplyEntity(body.Source, transform, session.TextureLock);
                foreach (MapBrush clip in body.Clips) clip.Transform(transform, session.TextureLock);
            }
        });
        return true;
    }

    internal bool CapturePoses()
    {
        bool changed = false;
        foreach (PlacedBody body in _placed)
        {
            Vector3 position = _physics.BodyInterface.GetPosition(body.Id);
            Quaternion rotation = _physics.BodyInterface.GetRotation(body.Id);
            if (!Finite(position) || !Finite(rotation))
                throw new InvalidOperationException("Placement simulation produced a non-finite transform.");
            if (Vector3.DistanceSquared(position, body.Origin) <= 0.000001f &&
                MathF.Abs(Quaternion.Dot(rotation, body.Rotation)) >= 0.999999f) continue;
            body.Origin = position;
            body.Rotation = rotation;
            Matrix4x4 delta = Matrix4x4.Transpose(Matrix4x4.CreateFromQuaternion(body.InitialRotation)) *
                Matrix4x4.CreateFromQuaternion(rotation);
            Matrix4x4 transform = Matrix4x4.CreateTranslation(-body.InitialOrigin) *
                delta * Matrix4x4.CreateTranslation(position);
            _drawTransforms[body.Source] = transform;
            changed = true;
        }
        HasChanges = _placed.Any(body => Vector3.DistanceSquared(body.Origin, body.InitialOrigin) > 0.000001f ||
            MathF.Abs(Quaternion.Dot(body.Rotation, body.InitialRotation)) < 0.999999f);
        return changed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (BodyID id in _bodyIds) _physics.BodyInterface.RemoveAndDestroyBody(id);
        _physics.Dispose();
        _jobs.Dispose();
        foreach (Shape shape in _shapes) shape.Dispose();
        _broadPhaseFilter.Dispose();
        _broadPhase.Dispose();
        _pairFilter.Dispose();
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Finite(Quaternion value) => float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W);
}
