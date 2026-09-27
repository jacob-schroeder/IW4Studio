using System.Numerics;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using JoltPhysicsSharp;

namespace Iw4Radiant.Viewports.Camera;

// Editor-only preview. Shards and their owners never enter the authored map.
internal sealed class CameraGlassShatterSimulation : IDisposable
{
    private static readonly ObjectLayer StaticLayer = 0, MovingLayer = 1;
    private const float Gravity = 800;
    private const int MaximumPanes = 16;
    private readonly ObjectLayerPairFilterTable _pairFilter;
    private readonly BroadPhaseLayerInterfaceTable _broadPhase;
    private readonly ObjectVsBroadPhaseLayerFilterTable _broadPhaseFilter;
    private readonly PhysicsSystem _physics;
    private readonly JobSystemThreadPool _jobs;
    private readonly List<Shape> _shapes;
    private readonly List<BodyID> _bodyIds;
    private readonly List<ShardBody> _shards;
    private readonly Dictionary<MapEntity, Matrix4x4> _drawTransforms = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    private sealed record PreparedShard(MapBrush Brush, MapEntity Owner, Vector3 Center,
        Vector3[] LocalPoints, Vector3 InitialVelocity);
    private sealed record PreparedWorld(MapBrush[] Sources, PreparedShard[] Shards,
        Vector3[][] StaticBrushes, Vector3[] TerrainVertices, IndexedTriangle[] TerrainTriangles);
    private sealed class ShardBody(MapEntity owner, BodyID id, Vector3 center)
    {
        internal MapEntity Owner { get; } = owner;
        internal BodyID Id { get; } = id;
        internal Vector3 InitialCenter { get; } = center;
        internal Vector3 Center { get; set; } = center;
        internal Quaternion Rotation { get; set; } = Quaternion.Identity;
    }

    private CameraGlassShatterSimulation(ObjectLayerPairFilterTable pairFilter,
        BroadPhaseLayerInterfaceTable broadPhase, ObjectVsBroadPhaseLayerFilterTable broadPhaseFilter,
        PhysicsSystem physics, JobSystemThreadPool jobs, List<Shape> shapes, List<BodyID> bodyIds,
        List<ShardBody> shards, MapBrush[] sources, PreparedShard[] prepared)
    {
        _pairFilter = pairFilter;
        _broadPhase = broadPhase;
        _broadPhaseFilter = broadPhaseFilter;
        _physics = physics;
        _jobs = jobs;
        _shapes = shapes;
        _bodyIds = bodyIds;
        _shards = shards;
        Sources = sources.ToHashSet<MapBrush>(ReferenceEqualityComparer.Instance);
        Fragments = prepared.ToDictionary<PreparedShard, MapBrush, MapEntity>(shard => shard.Brush, shard => shard.Owner,
            ReferenceEqualityComparer.Instance);
        foreach (ShardBody shard in shards) _drawTransforms.Add(shard.Owner, Matrix4x4.Identity);
    }

    internal IReadOnlySet<MapBrush> Sources { get; }
    internal IReadOnlyDictionary<MapBrush, MapEntity> Fragments { get; }
    internal IReadOnlyDictionary<MapEntity, Matrix4x4> DrawTransforms => _drawTransforms;
    internal int ShardCount => _shards.Count;
    internal bool AllSleeping => _shards.All(shard => !_physics.BodyInterface.IsActive(shard.Id));

    internal static bool CanStart(EditorSession session)
    {
        object[] selected = session.Selection.Items.ToArray();
        return selected.Length is > 0 and <= MaximumPanes && selected.All(item =>
            item is MapBrush brush && session.Document.World.Brushes.Contains(brush) &&
            session.Visibility.CanSelect(session.Document, brush) && BrushGlass.IsGlass(brush));
    }

    internal static Task<CameraGlassShatterSimulation> CreateAsync(EditorSession session,
        Func<string, MaterialSource?> resolveMaterial, Vector3 rayOrigin, Vector3 rayDirection,
        CancellationToken cancellationToken)
    {
        PreparedWorld prepared = Prepare(session, resolveMaterial, rayOrigin, rayDirection);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => CreateNative(prepared, cancellationToken), cancellationToken);
    }

    private static PreparedWorld Prepare(EditorSession session, Func<string, MaterialSource?> resolveMaterial,
        Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (!CanStart(session))
            throw new ArgumentException($"Select 1–{MaximumPanes} whole, visible breakable glass world brushes for Shatter.");
        MapBrush[] sources = session.Selection.Items.Cast<MapBrush>().ToArray();
        var shards = new List<PreparedShard>();
        foreach (MapBrush source in sources)
        {
            (MapPolygon pane, float thickness) = BrushGlass.ReadPane(source);
            (string shatteredMaterial, _) = BrushGlass.Read(source) ??
                throw new InvalidDataException("A breakable glass brush needs a shattered material and physics preset.");
            MaterialSource material = resolveMaterial(shatteredMaterial) ??
                throw new InvalidDataException($"Shattered glass material '{shatteredMaterial}' is unavailable.");
            if (material.IsSky || material.IsWater)
                throw new NotSupportedException("Shattered glass material must be a drawable solid surface.");
            Vector3 origin = pane.Vertices[0];
            Vector3 u = pane.Vertices[1] - origin;
            Vector3 v = pane.Vertices[3] - origin;
            float width = u.Length(), height = v.Length();
            Vector3 axisU = u / width, axisV = v / height;
            Vector3 depth = -pane.Face.Normal * thickness;
            Vector2 impact = new(width * 0.5f, height * 0.5f);
            double facing = BrushGeometry.Dot(pane.Face.Normal, rayDirection);
            if (Math.Abs(facing) > 1e-6)
            {
                double distance = BrushGeometry.Dot(pane.Face.Normal, origin - rayOrigin) / facing;
                Vector3 hit = rayOrigin + rayDirection * (float)distance - origin;
                Vector2 candidate = new((float)BrushGeometry.Dot(hit, axisU), (float)BrushGeometry.Dot(hit, axisV));
                if (distance >= 0 && candidate.X >= 0 && candidate.X <= width &&
                    candidate.Y >= 0 && candidate.Y <= height)
                    impact = candidate;
            }
            Vector3 impactCenter = origin + axisU * impact.X + axisV * impact.Y + depth * 0.5f;
            foreach (Vector2[] polygon in CameraGlassFracture.Create(width, height, impact))
            {
                Vector3[] front = polygon.Select(point => origin + axisU * point.X + axisV * point.Y).ToArray();
                MapBrush fragment = CreatePrism(front, depth, shatteredMaterial, pane.Face.Projection);
                Vector3[] points = fragment.GetVertices().ToArray();
                Vector3 center = points.Aggregate(Vector3.Zero, (sum, point) => sum + point) / points.Length;
                Vector3 outward = Vector3.Normalize(center - impactCenter + pane.Face.Normal * 0.25f);
                // Preserve the preview's velocity range; native impulses are not recovered.
                float distanceFraction = Math.Clamp(Vector3.Distance(center, impactCenter) / MathF.Max(width, height), 0, 1);
                Vector3 velocity = outward * (35 + (1 - distanceFraction) * 24) +
                    Vector3.UnitZ * (28 + (1 - distanceFraction) * 8);
                shards.Add(new PreparedShard(fragment, new MapEntity(), center,
                    points.Select(point => point - center).ToArray(), velocity));
            }
        }
        var excluded = sources.ToHashSet<MapBrush>(ReferenceEqualityComparer.Instance);
        var collision = CameraStaticPhysicsCollision.Prepare(session, resolveMaterial,
            new HashSet<MapEntity>(ReferenceEqualityComparer.Instance), excluded,
            "Shatter preview", requireStaticWorld: false);
        if (collision.Brushes.Length + shards.Count + (collision.TerrainTriangles.Length > 0 ? 1 : 0) > 32767)
            throw new NotSupportedException("Shatter preview exceeds the Jolt collision-body limit.");
        return new PreparedWorld(sources, shards.ToArray(), collision.Brushes,
            collision.TerrainVertices, collision.TerrainTriangles);
    }

    private static MapBrush CreatePrism(Vector3[] front, Vector3 depth,
        string material, string projection)
    {
        Vector3[] points = [.. front, .. front.Select(point => point + depth)];
        Vector3 center = points.Aggregate(Vector3.Zero, (sum, point) => sum + point) / points.Length;
        var brush = new MapBrush();
        AddFace(front[0], front[1], front[2]);
        AddFace(front[0] + depth, front[2] + depth, front[1] + depth);
        for (int edge = 0; edge < front.Length; edge++)
        {
            Vector3 a = front[edge], b = front[(edge + 1) % front.Length];
            AddFace(a, a + depth, b + depth);
        }
        BrushGeometry.Validate(brush);
        return brush;

        void AddFace(Vector3 first, Vector3 second, Vector3 third)
        {
            if (Vector3.Dot(BrushGeometry.FaceNormal(first, second, third), center - first) > 0)
                (second, third) = (third, second);
            brush.Faces.Add(new MapFace
            {
                A = first, B = second, C = third, Material = material, Projection = projection
            });
        }
    }

    private static CameraGlassShatterSimulation CreateNative(PreparedWorld prepared, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            if (!CameraWalkSimulation.FoundationAvailable)
                throw new InvalidOperationException("Jolt Physics could not initialize for Shatter.");
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new InvalidOperationException("The Jolt native library is unavailable for Shatter.", exception);
        }
        ObjectLayerPairFilterTable? pairFilter = null;
        BroadPhaseLayerInterfaceTable? broadPhase = null;
        ObjectVsBroadPhaseLayerFilterTable? broadPhaseFilter = null;
        PhysicsSystem? physics = null;
        JobSystemThreadPool? jobs = null;
        var shapes = new List<Shape>();
        var ids = new List<BodyID>();
        var bodies = new List<ShardBody>();
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
                MaxBodies = Math.Max(1024, prepared.StaticBrushes.Length + prepared.Shards.Length + 16),
                ObjectLayerPairFilter = pairFilter,
                BroadPhaseLayerInterface = broadPhase,
                ObjectVsBroadPhaseLayerFilter = broadPhaseFilter
            });
            physics.Gravity = -Vector3.UnitZ * Gravity;
            jobs = new JobSystemThreadPool();
            foreach (Vector3[] points in prepared.StaticBrushes)
            {
                token.ThrowIfCancellationRequested();
                using var settings = new ConvexHullShapeSettings(points);
                Shape shape = settings.Create();
                shapes.Add(shape);
                AddBody(shape, Vector3.Zero, MotionType.Static, StaticLayer, Activation.DontActivate);
            }
            if (prepared.TerrainTriangles.Length > 0)
            {
                token.ThrowIfCancellationRequested();
                using var settings = new MeshShapeSettings(prepared.TerrainVertices, prepared.TerrainTriangles);
                Shape shape = settings.Create();
                shapes.Add(shape);
                AddBody(shape, Vector3.Zero, MotionType.Static, StaticLayer, Activation.DontActivate);
            }
            foreach (PreparedShard shard in prepared.Shards)
            {
                token.ThrowIfCancellationRequested();
                using var settings = new ConvexHullShapeSettings(shard.LocalPoints);
                Shape shape = settings.Create();
                shapes.Add(shape);
                BodyID id = AddBody(shape, shard.Center, MotionType.Dynamic, MovingLayer, Activation.Activate);
                physics.BodyInterface.SetPositionRotationAndVelocity(id, shard.Center,
                    Quaternion.Identity, shard.InitialVelocity, Vector3.Zero);
                bodies.Add(new ShardBody(shard.Owner, id, shard.Center));
            }
            token.ThrowIfCancellationRequested();
            physics.OptimizeBroadPhase();
            return new CameraGlassShatterSimulation(pairFilter, broadPhase, broadPhaseFilter,
                physics, jobs, shapes, ids, bodies, prepared.Sources, prepared.Shards);

            BodyID AddBody(Shape shape, Vector3 position, MotionType motion,
                ObjectLayer layer, Activation activation)
            {
                using var settings = new BodyCreationSettings(shape, position, Quaternion.Identity, motion, layer);
                BodyID id = physics.BodyInterface.CreateAndAddBody(settings, activation);
                if (id.IsInvalid) throw new InvalidOperationException("Jolt could not allocate a shatter body.");
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
        if (_disposed) throw new ObjectDisposedException(nameof(CameraGlassShatterSimulation));
        PhysicsUpdateError error = _physics.Update(seconds, 1, _jobs);
        if (error != PhysicsUpdateError.None)
            throw new InvalidOperationException($"Jolt shatter update failed: {error}.");
    }

    internal bool CapturePoses()
    {
        bool changed = false;
        foreach (ShardBody shard in _shards)
        {
            Vector3 center = _physics.BodyInterface.GetPosition(shard.Id);
            Quaternion rotation = _physics.BodyInterface.GetRotation(shard.Id);
            if (!Finite(center) || !Finite(rotation))
                throw new InvalidOperationException("Shatter preview produced a non-finite transform.");
            if (Vector3.DistanceSquared(center, shard.Center) <= 0.000001f &&
                MathF.Abs(Quaternion.Dot(rotation, shard.Rotation)) >= 0.999999f) continue;
            shard.Center = center;
            shard.Rotation = rotation;
            _drawTransforms[shard.Owner] = Matrix4x4.CreateTranslation(-shard.InitialCenter) *
                Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(center);
            changed = true;
        }
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

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Finite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W);
}
