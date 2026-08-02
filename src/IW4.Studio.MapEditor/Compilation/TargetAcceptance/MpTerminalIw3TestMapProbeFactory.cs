using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Compilation.Lighting;
using IW4.Studio.MapEditor.Compilation.RenderWorld;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.SourceDocuments;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Recreates the playable enclosure from the COD4 SDK mp_test.map while
/// retaining mp_terminal as the PS3 target identity. The sky shell, compiler
/// volumes, reflection probes, and decorative helicopters are deliberately
/// excluded from this first structural map probe.
/// </summary>
public static class MpTerminalIw3TestMapProbeFactory
{
    public const float FloorHalfExtent = 832f;

    public const float FloorCollisionHalfDepth = 8f;

    public const float InnerWallCoordinate = 816f;

    public const float WallCollisionCenterCoordinate = 824f;

    public const float WallCollisionHalfThickness = 8f;

    public const float WallHeight = 128f;

    public const float WallCollisionHalfHeight = WallHeight / 2f;

    private const float FloorTextureTileSpan = 26f;

    private const float WallTextureHorizontalTileSpan = 26f;

    private const float WallTextureVerticalTileSpan = 2f;

    private static readonly MapDocumentId FixtureDocumentId =
        new(
            Guid.Parse(
                "6d705f74-6573-4d69-a16e-6172656e6130"));

    private static readonly MapObjectId ArenaRenderObjectId =
        new(
            Guid.Parse(
                "6d705f74-6573-4d69-a16e-6172656e6131"));

    private static readonly MapObjectId FloorCollisionObjectId =
        new(
            Guid.Parse(
                "6d705f74-6573-4d69-a16e-6172656e6132"));

    private static readonly MapObjectId NorthWallCollisionObjectId =
        new(
            Guid.Parse(
                "6d705f74-6573-4d69-a16e-6172656e6133"));

    private static readonly MapObjectId SouthWallCollisionObjectId =
        new(
            Guid.Parse(
                "6d705f74-6573-4d69-a16e-6172656e6134"));

    private static readonly MapObjectId EastWallCollisionObjectId =
        new(
            Guid.Parse(
                "6d705f74-6573-4d69-a16e-6172656e6135"));

    private static readonly MapObjectId WestWallCollisionObjectId =
        new(
            Guid.Parse(
                "6d705f74-6573-4d69-a16e-6172656e6136"));

    /// <summary>
    /// Creates one aggregate render mesh and the five physical world brushes
    /// from mp_test.map. Existing minimal-probe entity and startup profiles
    /// continue to supply worldspawn, Free-for-All spawn, and intermission
    /// entities for the mp_terminal target identity.
    /// </summary>
    public static Iw4SceneDocument CreateCanonicalSource()
    {
        GfxWorldTargetMaterialDependencyEvidence material =
            GfxWorldTargetMaterialDependencyCatalog
                .CommonMpChemLightGlow;
        return new Iw4SceneDocument(
            Iw4SceneFormat.CurrentVersion,
            FixtureDocumentId,
            documentRevision: 0,
            MpTerminalMinimalTargetProbeFactory.MapAssetName,
            MpTerminalMinimalTargetProbeFactory.TargetZoneName,
            MapCompilerProfiles.MinimalMultiplayerTargetProbe,
            MinimalMultiplayerMapTargetProbeCandidate
                .EntityProfileIdentity,
            MinimalMultiplayerMapTargetStartupProfile
                .OfflineSplitScreenFreeForAll,
            [CreateRenderArena(material.AssetKey.LogicalName)],
            CreateCollisionArena());
    }

    public static MpTerminalMinimalTargetProbeCompilation Compile() =>
        MpTerminalMinimalTargetProbeSceneCompiler.Compile(
            CreateCanonicalSource());

    private static AuthoredIndexedRenderMeshSource CreateRenderArena(
        string materialName)
    {
        var vertices = new List<AuthoredRenderVertex>();
        var triangles = new List<AuthoredIndexedRenderTriangle>();

        AddQuad(
            vertices,
            triangles,
            new MapVector3(-FloorHalfExtent, -FloorHalfExtent, 0),
            new MapVector3(FloorHalfExtent, -FloorHalfExtent, 0),
            new MapVector3(FloorHalfExtent, FloorHalfExtent, 0),
            new MapVector3(-FloorHalfExtent, FloorHalfExtent, 0),
            normal: new MapVector3(0, 0, 1),
            tangent: new MapVector3(1, 0, 0),
            textureWidth: FloorTextureTileSpan,
            textureHeight: FloorTextureTileSpan);

        // North face points inward toward negative Y.
        AddQuad(
            vertices,
            triangles,
            new MapVector3(-FloorHalfExtent, InnerWallCoordinate, 0),
            new MapVector3(FloorHalfExtent, InnerWallCoordinate, 0),
            new MapVector3(
                FloorHalfExtent,
                InnerWallCoordinate,
                WallHeight),
            new MapVector3(
                -FloorHalfExtent,
                InnerWallCoordinate,
                WallHeight),
            normal: new MapVector3(0, -1, 0),
            tangent: new MapVector3(1, 0, 0),
            textureWidth: WallTextureHorizontalTileSpan,
            textureHeight: WallTextureVerticalTileSpan);

        // South face points inward toward positive Y.
        AddQuad(
            vertices,
            triangles,
            new MapVector3(FloorHalfExtent, -InnerWallCoordinate, 0),
            new MapVector3(-FloorHalfExtent, -InnerWallCoordinate, 0),
            new MapVector3(
                -FloorHalfExtent,
                -InnerWallCoordinate,
                WallHeight),
            new MapVector3(
                FloorHalfExtent,
                -InnerWallCoordinate,
                WallHeight),
            normal: new MapVector3(0, 1, 0),
            tangent: new MapVector3(-1, 0, 0),
            textureWidth: WallTextureHorizontalTileSpan,
            textureHeight: WallTextureVerticalTileSpan);

        // East face points inward toward negative X.
        AddQuad(
            vertices,
            triangles,
            new MapVector3(InnerWallCoordinate, FloorHalfExtent, 0),
            new MapVector3(InnerWallCoordinate, -FloorHalfExtent, 0),
            new MapVector3(
                InnerWallCoordinate,
                -FloorHalfExtent,
                WallHeight),
            new MapVector3(
                InnerWallCoordinate,
                FloorHalfExtent,
                WallHeight),
            normal: new MapVector3(-1, 0, 0),
            tangent: new MapVector3(0, -1, 0),
            textureWidth: WallTextureHorizontalTileSpan,
            textureHeight: WallTextureVerticalTileSpan);

        // West face points inward toward positive X.
        AddQuad(
            vertices,
            triangles,
            new MapVector3(-InnerWallCoordinate, -FloorHalfExtent, 0),
            new MapVector3(-InnerWallCoordinate, FloorHalfExtent, 0),
            new MapVector3(
                -InnerWallCoordinate,
                FloorHalfExtent,
                WallHeight),
            new MapVector3(
                -InnerWallCoordinate,
                -FloorHalfExtent,
                WallHeight),
            normal: new MapVector3(1, 0, 0),
            tangent: new MapVector3(0, 1, 0),
            textureWidth: WallTextureHorizontalTileSpan,
            textureHeight: WallTextureVerticalTileSpan);

        return new AuthoredIndexedRenderMeshSource(
            ArenaRenderObjectId,
            new StandaloneWorldRenderMeshOwnership(),
            materialName,
            RenderTriangleWinding.CounterClockwiseFrontFace,
            vertices,
            triangles);
    }

    private static IReadOnlyList<AuthoredConvexBrushCollisionSource>
        CreateCollisionArena()
    {
        var material = new AuthoredCollisionMaterialInput(
            "clip_player",
            surfaceFlags:
                MpTerminalMinimalTargetProbeFactory
                    .FloorCollisionSurfaceFlags,
            contents:
                MpTerminalMinimalTargetProbeFactory
                    .FloorCollisionContents);

        return
        [
            CreateCollisionBox(
                FloorCollisionObjectId,
                center: new MapVector3(
                    0,
                    0,
                    -FloorCollisionHalfDepth),
                halfSize: new MapVector3(
                    FloorHalfExtent,
                    FloorHalfExtent,
                    FloorCollisionHalfDepth),
                material: material),
            CreateCollisionBox(
                NorthWallCollisionObjectId,
                center: new MapVector3(
                    0,
                    WallCollisionCenterCoordinate,
                    WallCollisionHalfHeight),
                halfSize: new MapVector3(
                    FloorHalfExtent,
                    WallCollisionHalfThickness,
                    WallCollisionHalfHeight),
                material: material),
            CreateCollisionBox(
                SouthWallCollisionObjectId,
                center: new MapVector3(
                    0,
                    -WallCollisionCenterCoordinate,
                    WallCollisionHalfHeight),
                halfSize: new MapVector3(
                    FloorHalfExtent,
                    WallCollisionHalfThickness,
                    WallCollisionHalfHeight),
                material: material),
            CreateCollisionBox(
                EastWallCollisionObjectId,
                center: new MapVector3(
                    WallCollisionCenterCoordinate,
                    0,
                    WallCollisionHalfHeight),
                halfSize: new MapVector3(
                    WallCollisionHalfThickness,
                    FloorHalfExtent,
                    WallCollisionHalfHeight),
                material: material),
            CreateCollisionBox(
                WestWallCollisionObjectId,
                center: new MapVector3(
                    -WallCollisionCenterCoordinate,
                    0,
                    WallCollisionHalfHeight),
                halfSize: new MapVector3(
                    WallCollisionHalfThickness,
                    FloorHalfExtent,
                    WallCollisionHalfHeight),
                material: material)
        ];
    }

    private static AuthoredConvexBrushCollisionSource CreateCollisionBox(
        MapObjectId objectId,
        MapVector3 center,
        MapVector3 halfSize,
        AuthoredCollisionMaterialInput material) =>
        AuthoredCollisionPrimitiveFactory
            .CreateStandaloneAxisAlignedBox(
                objectId,
                new MapBounds(center, halfSize),
                material);

    private static void AddQuad(
        ICollection<AuthoredRenderVertex> vertices,
        ICollection<AuthoredIndexedRenderTriangle> triangles,
        MapVector3 point0,
        MapVector3 point1,
        MapVector3 point2,
        MapVector3 point3,
        MapVector3 normal,
        MapVector3 tangent,
        float textureWidth,
        float textureHeight)
    {
        int firstVertex = vertices.Count;
        vertices.Add(
            Vertex(
                point0,
                textureU: 0,
                textureV: 0,
                normal: normal,
                tangent: tangent));
        vertices.Add(
            Vertex(
                point1,
                textureU: textureWidth,
                textureV: 0,
                normal: normal,
                tangent: tangent));
        vertices.Add(
            Vertex(
                point2,
                textureU: textureWidth,
                textureV: textureHeight,
                normal: normal,
                tangent: tangent));
        vertices.Add(
            Vertex(
                point3,
                textureU: 0,
                textureV: textureHeight,
                normal: normal,
                tangent: tangent));
        triangles.Add(
            new AuthoredIndexedRenderTriangle(
                firstVertex,
                firstVertex + 1,
                firstVertex + 2));
        triangles.Add(
            new AuthoredIndexedRenderTriangle(
                firstVertex,
                firstVertex + 2,
                firstVertex + 3));
    }

    private static AuthoredRenderVertex Vertex(
        MapVector3 position,
        float textureU,
        float textureV,
        MapVector3 normal,
        MapVector3 tangent) =>
        new(
            position,
            new AuthoredRenderColor(255, 255, 255, 255),
            new AuthoredRenderUv(textureU, textureV),
            new AuthoredRenderUv(0, 0),
            normal,
            tangent);
}
