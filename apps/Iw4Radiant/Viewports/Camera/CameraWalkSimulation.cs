using System.Numerics;
using Iw4Radiant.Compilation;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using JoltPhysicsSharp;

namespace Iw4Radiant.Viewports.Camera;

internal sealed class CameraWalkSimulation : IDisposable
{
    private static readonly Lazy<bool> FoundationReady = new(() => Foundation.Init());
    private static readonly ObjectLayer StaticLayer = 0;
    private static readonly ObjectLayer PlayerLayer = 1;
    private readonly ObjectLayerPairFilterTable _pairFilter;
    private readonly BroadPhaseLayerInterfaceTable _broadPhase;
    private readonly ObjectVsBroadPhaseLayerFilterTable _broadPhaseFilter;
    private readonly PhysicsSystem _physics;
    private readonly List<Shape> _worldShapes;
    private readonly List<BodyID> _bodyIds;
    private readonly Shape _playerShape;
    private readonly CharacterVirtual _player;
    private readonly ExtendedUpdateSettings _updateSettings;
    private readonly float _lowestSurface;
    private bool _disposed;

    // Xbox playerBox at 0x82008140 has midpoint (0,0,35), half-size (15,15,35).
    // Match that envelope with an editor capsule; native trace shape and PS3 parity remain unproven.
    // Xbox PM_CheckDuck (0x82106B70) sets the standing view-height target to 60;
    // Jump_RegisterDvars (0x820FBFB0) registers jump_height = 39.
    // Speed/gravity and grounded stair behavior remain editor choices, not recovered PS3 PMove.
    internal const float EyeHeight = 60;
    private const float PlayerRadius = 15;
    private const float PlayerHeight = 70;
    private const float WalkSpeed = 190;
    private const float JumpHeight = 39;
    private const float Gravity = 800;
    private static readonly float JumpSpeed = MathF.Sqrt(2 * Gravity * JumpHeight);
    private const float StepHeight = 18;
    private const float WalkableNormal = 0.7f;
    internal static string ProfileDescription => FormattableString.Invariant(
        $"Editor approximation (PS3 parity unverified): capsule {PlayerRadius * 2} wide × {PlayerHeight} high, eye {EyeHeight} above feet; {WalkSpeed} units/s walk, {StepHeight}-unit step, nominal {JumpHeight}-unit jump, {Gravity} units/s² gravity, {MathF.Acos(WalkableNormal) * 180 / MathF.PI:0.0}° maximum slope.");

    private CameraWalkSimulation(ObjectLayerPairFilterTable pairFilter,
        BroadPhaseLayerInterfaceTable broadPhase, ObjectVsBroadPhaseLayerFilterTable broadPhaseFilter,
        PhysicsSystem physics, List<Shape> worldShapes, List<BodyID> bodyIds,
        Shape playerShape, CharacterVirtual player, float lowestSurface)
    {
        _pairFilter = pairFilter;
        _broadPhase = broadPhase;
        _broadPhaseFilter = broadPhaseFilter;
        _physics = physics;
        _worldShapes = worldShapes;
        _bodyIds = bodyIds;
        _playerShape = playerShape;
        _player = player;
        _lowestSurface = lowestSurface;
        _updateSettings = new ExtendedUpdateSettings
        {
            StickToFloorStepDown = -Vector3.UnitZ * StepHeight,
            WalkStairsStepUp = Vector3.UnitZ * StepHeight,
            WalkStairsMinStepForward = 1f,
            WalkStairsStepForwardTest = 8f,
            WalkStairsCosAngleForwardContact = MathF.Cos(75 * MathF.PI / 180),
            WalkStairsStepDownExtra = Vector3.Zero
        };
    }

    internal Vector3 Eye => _player.Position + Vector3.UnitZ * EyeHeight;

    internal static CameraWalkSimulation Create(EditorSession session, Func<string, MaterialSource?> resolveMaterial)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resolveMaterial);
        MapDocument document = PrefabLibrary.ExpandForCompilation(session.Document, session.FilePath);
        foreach (MapEntity entity in document.Entities)
            if (entity.PreservedPrimitives.Count != 0)
                throw new NotSupportedException($"Walk collision cannot classify preserved primitives in '{entity.ClassName}'. Remove or convert them before entering Walk.");
        foreach (MapEntity group in document.Entities.Where(entity => entity.ClassName == "func_group").ToArray())
            MapOrganization.Ungroup(document, group);
        var brushes = new List<Vector3[]>();
        var terrainVertices = new List<Vector3>();
        var terrainTriangles = new List<IndexedTriangle>();
        float lowestSurface = float.PositiveInfinity;

        foreach (MapEntity entity in document.Entities)
        {
            if (entity.ClassName.StartsWith("trigger_", StringComparison.Ordinal)) continue;
            if (entity != document.World && (entity.Brushes.Count != 0 || entity.Terrains.Count != 0))
                throw new NotSupportedException($"Walk collision supports static world/group geometry; brush entity '{entity.ClassName}' may move or use runtime collision rules. Convert it to world geometry before entering Walk.");
            foreach (MapBrush brush in entity.Brushes)
            {
                BrushGeometry.Validate(brush);
                BrushKind kind = BrushContents.ReadForCompilation(brush);
                bool clip = brush.Faces.All(face => ClipBrushMaterial.IsPlayerClip(face.Material));
                if (!clip && brush.Faces.Any(face => ClipBrushMaterial.IsPlayerClip(face.Material)))
                    throw new NotSupportedException("Walk collision requires clip_player on every face of a player clip brush.");
                if (clip && kind != BrushKind.Structural)
                    throw new NotSupportedException("Player clip brushes cannot also have a contents override.");
                if (kind is BrushKind.NonColliding or BrushKind.WeaponClip) continue;
                if (!clip)
                {
                    bool anySky = false, anyWater = false, anySolid = false;
                    foreach (MapFace face in brush.Faces)
                    {
                        if (CaulkMaterial.IsCaulk(face.Material)) { anySolid = true; continue; }
                        MaterialSource material = resolveMaterial(face.Material) ??
                            throw new InvalidDataException($"Walk collision material '{face.Material}' is unavailable.");
                        anySky |= material.IsSky;
                        anyWater |= material.IsWater;
                        anySolid |= !material.IsSky && !material.IsWater;
                    }
                    if ((anySky || anyWater) && anySolid || anySky && anyWater)
                        throw new NotSupportedException("Walk collision cannot classify a brush mixing sky, water, and solid faces.");
                    if (!anySolid) continue;
                }
                Vector3[] vertices = brush.GetVertices().ToArray();
                if (vertices.Length < 4 || vertices.Any(vertex => !BrushGeometry.IsFinite(vertex)))
                    throw new InvalidDataException("Walk collision found an invalid convex brush.");
                brushes.Add(vertices);
                foreach (Vector3 vertex in vertices) lowestSurface = MathF.Min(lowestSurface, vertex.Z);
            }
            if (entity.Terrains.Count == 0) continue;
            foreach (MapTerrain terrain in entity.Terrains)
            {
                if (TerrainContents.ReadNonColliding(terrain)) continue;
                if (ClipBrushMaterial.IsPlayerClip(terrain.Material))
                    throw new NotSupportedException("Walk collision requires player clips to be brushes, not terrain meshes.");
                MaterialSource material = resolveMaterial(terrain.Material) ??
                    throw new InvalidDataException($"Walk collision material '{terrain.Material}' is unavailable.");
                if (material.IsSky || material.IsWater)
                    throw new NotSupportedException($"Walk collision does not support sky or water terrain '{terrain.Material}'.");
                MapSurfaceCompiler.ValidateTerrain(terrain);
                MapTerrain surface = terrain.GetSurface();
                int first = terrainVertices.Count;
                terrainVertices.AddRange(surface.Vertices);
                foreach ((int a, int b, int c) in surface.GetTriangles())
                {
                    Vector3 normal = Vector3.Cross(surface.Vertices[b] - surface.Vertices[a],
                        surface.Vertices[c] - surface.Vertices[a]);
                    if (normal.LengthSquared() <= 0.00000001f && terrain.IsCurve) continue;
                    if (!float.IsFinite(normal.LengthSquared()) || normal.LengthSquared() <= 0)
                        throw new InvalidDataException($"Walk collision terrain '{terrain.Material}' has a degenerate triangle.");
                    terrainTriangles.Add(new IndexedTriangle(first + a, first + b, first + c));
                }
                foreach (Vector3 vertex in surface.Vertices) lowestSurface = MathF.Min(lowestSurface, vertex.Z);
            }
        }
        if (brushes.Count == 0 && terrainTriangles.Count == 0)
            throw new InvalidOperationException("Walk mode needs at least one solid world brush, player clip, or terrain surface.");
        if (brushes.Count + (terrainTriangles.Count > 0 ? 1 : 0) > 32767)
            throw new NotSupportedException("Walk collision supports at most 32767 static bodies.");
        try
        {
            if (!FoundationReady.Value)
                throw new InvalidOperationException("Jolt Physics could not initialize for walk mode.");
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new InvalidOperationException("The Jolt native library is unavailable for walk mode on this system.", exception);
        }

        ObjectLayerPairFilterTable? pairFilter = null;
        BroadPhaseLayerInterfaceTable? broadPhase = null;
        ObjectVsBroadPhaseLayerFilterTable? broadPhaseFilter = null;
        PhysicsSystem? physics = null;
        Shape? playerShape = null;
        CharacterVirtual? player = null;
        var worldShapes = new List<Shape>();
        var bodyIds = new List<BodyID>();
        try
        {
            pairFilter = new ObjectLayerPairFilterTable(2);
            pairFilter.EnableCollision(StaticLayer, PlayerLayer);
            broadPhase = new BroadPhaseLayerInterfaceTable(2, 2);
            broadPhase.MapObjectToBroadPhaseLayer(StaticLayer, 0);
            broadPhase.MapObjectToBroadPhaseLayer(PlayerLayer, 1);
            broadPhaseFilter = new ObjectVsBroadPhaseLayerFilterTable(broadPhase, 2, pairFilter, 2);
            physics = new PhysicsSystem(new PhysicsSystemSettings
            {
                MaxBodies = Math.Max(1024, brushes.Count + 16),
                ObjectLayerPairFilter = pairFilter,
                BroadPhaseLayerInterface = broadPhase,
                ObjectVsBroadPhaseLayerFilter = broadPhaseFilter
            });
            physics.Gravity = -Vector3.UnitZ * Gravity;
            foreach (Vector3[] points in brushes)
            {
                using var settings = new ConvexHullShapeSettings(points);
                Shape shape = settings.Create();
                worldShapes.Add(shape);
                AddStaticBody(physics, shape);
            }
            if (terrainTriangles.Count > 0)
            {
                using var settings = new MeshShapeSettings(terrainVertices.ToArray(), terrainTriangles.ToArray());
                Shape shape = settings.Create();
                worldShapes.Add(shape);
                AddStaticBody(physics, shape);
            }
            physics.OptimizeBroadPhase();

            using var capsule = new CapsuleShape((PlayerHeight - 2 * PlayerRadius) / 2, PlayerRadius);
            using var translated = new RotatedTranslatedShapeSettings(
                Vector3.UnitZ * PlayerHeight / 2,
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2), capsule);
            playerShape = translated.Create();
            var playerSettings = new CharacterVirtualSettings
            {
                Up = Vector3.UnitZ,
                Shape = playerShape,
                SupportingVolume = new Plane(Vector3.UnitZ, -PlayerRadius),
                MaxSlopeAngle = MathF.Acos(WalkableNormal)
            };
            player = new CharacterVirtual(playerSettings, Vector3.Zero, Quaternion.Identity, 0, physics);
            return new CameraWalkSimulation(pairFilter, broadPhase, broadPhaseFilter, physics,
                worldShapes, bodyIds, playerShape, player, lowestSurface);

            void AddStaticBody(PhysicsSystem system, Shape shape)
            {
                using var settings = new BodyCreationSettings(shape, Vector3.Zero,
                    Quaternion.Identity, MotionType.Static, StaticLayer);
                BodyID body = system.BodyInterface.CreateAndAddBody(settings, Activation.DontActivate);
                if (body.IsInvalid)
                    throw new InvalidOperationException("Jolt could not allocate a static walk collision body.");
                bodyIds.Add(body);
            }
        }
        catch
        {
            player?.Dispose();
            playerShape?.Dispose();
            if (physics is not null)
            {
                foreach (BodyID body in bodyIds) physics.BodyInterface.RemoveAndDestroyBody(body);
                physics.Dispose();
            }
            foreach (Shape shape in worldShapes) shape.Dispose();
            broadPhaseFilter?.Dispose();
            broadPhase?.Dispose();
            pairFilter?.Dispose();
            throw;
        }
    }

    internal bool TrySpawn(Vector3 feet, out string error)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CameraWalkSimulation));
        error = "";
        if (!BrushGeometry.IsFinite(feet)) { error = "The walk spawn position is not finite."; return false; }
        var contacts = new List<CollideShapeResult>();
        // JoltPhysicsSharp 2.22.0 transposes Matrix4x4 before copying it to native columns.
        // Cancel that conversion so the center-of-mass translation reaches column 3;
        // otherwise every spawn query checks the capsule at world origin.
        Matrix4x4 transform = Matrix4x4.Transpose(
            Matrix4x4.CreateTranslation(feet + _playerShape.CenterOfMass));
        _physics.NarrowPhaseQuery.CollideShape(_playerShape, Vector3.One, transform,
            Vector3.Zero, CollisionCollectorType.AllHit, contacts);
        float penetration = contacts.Count == 0 ? 0 : contacts.Max(contact => contact.PenetrationDepth);
        if (penetration > 0.01f)
        {
            error = FormattableString.Invariant($"The player capsule overlaps blocking map geometry by {penetration:G6} map units.");
            return false;
        }
        _player.Position = feet;
        _player.LinearVelocity = Vector3.Zero;
        _player.RefreshContacts(PlayerLayer, _physics);
        return true;
    }

    internal void Step(Vector3 desiredDirectionNormalized, bool jump, float seconds)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CameraWalkSimulation));
        if (!BrushGeometry.IsFinite(desiredDirectionNormalized) || !float.IsFinite(seconds) || seconds <= 0 || seconds > 0.1f)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Walk input and step duration must be finite and bounded.");
        Vector3 horizontal = new(desiredDirectionNormalized.X, desiredDirectionNormalized.Y, 0);
        if (horizontal.LengthSquared() > 1) horizontal = Vector3.Normalize(horizontal);
        Vector3 velocity = _player.LinearVelocity;
        float vertical = _player.GroundState == GroundState.OnGround ? MathF.Max(0, velocity.Z) : velocity.Z;
        if (jump && _player.GroundState == GroundState.OnGround) vertical = JumpSpeed;
        else vertical -= Gravity * seconds;
        _player.LinearVelocity = horizontal * WalkSpeed + Vector3.UnitZ * vertical;
        _player.ExtendedUpdate(seconds, _updateSettings, PlayerLayer, _physics);
        if (_player.Position.Z < _lowestSurface - 2 * PlayerHeight)
            throw new InvalidOperationException("Walk mode fell below the map collision. Reset to a player spawn or exit Walk.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _player.Dispose();
        _playerShape.Dispose();
        foreach (BodyID body in _bodyIds) _physics.BodyInterface.RemoveAndDestroyBody(body);
        _physics.Dispose();
        foreach (Shape shape in _worldShapes) shape.Dispose();
        _broadPhaseFilter.Dispose();
        _broadPhase.Dispose();
        _pairFilter.Dispose();
    }
}
