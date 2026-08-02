using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IW4.Studio.MapEditor.Compilation;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Compilation.RenderWorld;
using IW4.Studio.MapEditor.Compilation.TargetAcceptance;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.SourceDocuments;

/// <summary>
/// Strict, deterministic JSON codec for the Studio-owned .iw4scene format.
/// Unknown fields and unsupported discriminants fail closed. Serialization
/// always emits normalized identities and stable source-object ordering.
/// </summary>
public static class Iw4SceneJsonCodec
{
    private const string StandaloneWorld = "standaloneWorld";
    private const string IndexedRenderMesh = "indexedRenderMesh";
    private const string ConvexBrushCollision = "convexBrush";
    private const string TriangleMeshCollision = "triangleMesh";
    private const string CounterClockwiseFrontFace =
        "counterClockwiseFrontFace";
    private const string ClockwiseFrontFace = "clockwiseFrontFace";
    private const string OfflineSplitScreenFreeForAll =
        "offlineSplitScreenFreeForAll";

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling =
                JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth = 64
        };

    public static byte[] Serialize(Iw4SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.SerializeToUtf8Bytes(
            ToWire(document),
            SerializerOptions);
    }

    public static string SerializeToString(Iw4SceneDocument document) =>
        Encoding.UTF8.GetString(Serialize(document));

    public static Iw4SceneDocument Deserialize(
        ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new InvalidDataException(
                "An .iw4scene document cannot be empty.");
        }

        try
        {
            WireDocument? wire =
                JsonSerializer.Deserialize<WireDocument>(
                    utf8Json,
                    SerializerOptions);
            return FromWire(
                wire ??
                throw new InvalidDataException(
                    "The .iw4scene root cannot be null."));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is JsonException or
                  ArgumentException or
                  NotSupportedException or
                  OverflowException)
        {
            throw new InvalidDataException(
                "The .iw4scene document is not valid canonical source.",
                exception);
        }
    }

    public static Iw4SceneDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return Deserialize(Encoding.UTF8.GetBytes(json));
    }

    private static WireDocument ToWire(Iw4SceneDocument source) =>
        new()
        {
            Format = Iw4SceneFormat.Identity,
            Version = new WireVersion
            {
                Major = source.FormatVersion.Major,
                Minor = source.FormatVersion.Minor
            },
            DocumentId = source.DocumentId.ToString(),
            DocumentRevision = source.DocumentRevision,
            MapAssetName = source.MapAssetName,
            TargetZoneName = source.TargetZoneName,
            CompilerProfile = source.CompilerProfile.ProfileId,
            EntityProfile = source.EntityProfileIdentity,
            StartupProfile = source.StartupProfile switch
            {
                MinimalMultiplayerMapTargetStartupProfile
                    .OfflineSplitScreenFreeForAll =>
                        OfflineSplitScreenFreeForAll,
                _ => throw new NotSupportedException(
                    $"Unsupported startup profile " +
                    $"'{source.StartupProfile}'.")
            },
            RenderMeshes =
                source.RenderMeshes.Select(ToWire).ToArray(),
            CollisionSources =
                source.CollisionSources.Select(ToWire).ToArray()
        };

    private static WireRenderMesh ToWire(
        AuthoredIndexedRenderMeshSource source) =>
        new()
        {
            Kind = IndexedRenderMesh,
            Id = source.ObjectId.ToString(),
            Ownership = source.Ownership switch
            {
                StandaloneWorldRenderMeshOwnership => StandaloneWorld,
                _ => throw new NotSupportedException(
                    "Unsupported .iw4scene render ownership.")
            },
            Material = source.SymbolicMaterialName,
            TriangleWinding = source.TriangleWinding switch
            {
                RenderTriangleWinding.CounterClockwiseFrontFace =>
                    CounterClockwiseFrontFace,
                RenderTriangleWinding.ClockwiseFrontFace =>
                    ClockwiseFrontFace,
                _ => throw new NotSupportedException(
                    "Unsupported .iw4scene triangle winding.")
            },
            Vertices = source.Vertices.Select(value =>
                new WireRenderVertex
                {
                    Position = ToWire(value.Position),
                    Color = new WireColor
                    {
                        Red = value.Color.Red,
                        Green = value.Color.Green,
                        Blue = value.Color.Blue,
                        Alpha = value.Color.Alpha
                    },
                    TextureCoordinates =
                        ToWire(value.TextureCoordinates),
                    LightmapCoordinates =
                        ToWire(value.LightmapCoordinates),
                    Normal = ToWire(value.Normal),
                    Tangent = ToWire(value.Tangent)
                }).ToArray(),
            Triangles = source.Triangles.Select(value =>
                new WireTriangle
                {
                    Index0 = value.Index0,
                    Index1 = value.Index1,
                    Index2 = value.Index2
                }).ToArray()
        };

    private static WireCollisionSource ToWire(
        AuthoredCollisionSource source) =>
        source switch
        {
            AuthoredConvexBrushCollisionSource brush =>
                new WireCollisionSource
                {
                    Kind = ConvexBrushCollision,
                    Id = brush.ObjectId.ToString(),
                    Ownership = StandaloneWorld,
                    Contents = brush.Contents,
                    Faces = brush.Faces.Select(value =>
                        new WireCollisionFace
                        {
                            Plane = new WirePlane
                            {
                                Normal = ToWire(
                                    value.Plane.Normal),
                                Distance = value.Plane.Distance
                            },
                            Material = ToWire(value.Material),
                            Winding = value.Winding
                                .Select(ToWire)
                                .ToArray()
                        }).ToArray()
                },

            AuthoredIndexedTriangleMeshCollisionSource mesh =>
                new WireCollisionSource
                {
                    Kind = TriangleMeshCollision,
                    Id = mesh.ObjectId.ToString(),
                    Ownership = StandaloneWorld,
                    Vertices = mesh.Vertices.Select(ToWire).ToArray(),
                    Triangles = mesh.Triangles.Select(value =>
                        new WireCollisionTriangle
                        {
                            Vertex0 = value.Vertex0,
                            Vertex1 = value.Vertex1,
                            Vertex2 = value.Vertex2,
                            Walkability = new WireWalkability
                            {
                                Edge01 = value.Walkability.Edge01,
                                Edge12 = value.Walkability.Edge12,
                                Edge20 = value.Walkability.Edge20
                            },
                            Material = ToWire(value.Material)
                        }).ToArray()
                },

            _ => throw new NotSupportedException(
                "Unsupported .iw4scene collision-source kind.")
        };

    private static WireCollisionMaterial ToWire(
        AuthoredCollisionMaterialInput material) =>
        new()
        {
            Name = material.ExactName,
            SurfaceFlags = material.SurfaceFlags,
            Contents = material.Contents
        };

    private static WireVector3 ToWire(MapVector3 value) =>
        new()
        {
            X = value.X,
            Y = value.Y,
            Z = value.Z
        };

    private static WireUv ToWire(AuthoredRenderUv value) =>
        new()
        {
            U = value.U,
            V = value.V
        };

    private static Iw4SceneDocument FromWire(WireDocument wire)
    {
        if (!string.Equals(
                wire.Format,
                Iw4SceneFormat.Identity,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Expected .iw4scene format identity " +
                $"'{Iw4SceneFormat.Identity}'.");
        }
        WireVersion version = Require(wire.Version, "version");
        WireRenderMesh[] renderMeshes =
            Require(wire.RenderMeshes, "renderMeshes");
        WireCollisionSource[] collisionSources =
            Require(wire.CollisionSources, "collisionSources");

        return new Iw4SceneDocument(
            new Iw4SceneFormatVersion(
                version.Major,
                version.Minor),
            new MapDocumentId(ParseId(wire.DocumentId, "documentId")),
            wire.DocumentRevision,
            RequireText(wire.MapAssetName, "mapAssetName"),
            RequireText(wire.TargetZoneName, "targetZoneName"),
            new MapCompilerProfileIdentity(
                RequireText(
                    wire.CompilerProfile,
                    "compilerProfile")),
            RequireText(wire.EntityProfile, "entityProfile"),
            ParseStartupProfile(wire.StartupProfile),
            renderMeshes.Select(FromWire),
            collisionSources.Select(FromWire));
    }

    private static AuthoredIndexedRenderMeshSource FromWire(
        WireRenderMesh wire)
    {
        if (!string.Equals(
                wire.Kind,
                IndexedRenderMesh,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported render-source kind '{wire.Kind}'.");
        }
        RequireStandaloneOwnership(wire.Ownership, "render");
        WireRenderVertex[] vertices =
            Require(wire.Vertices, "renderMeshes[].vertices");
        WireTriangle[] triangles =
            Require(wire.Triangles, "renderMeshes[].triangles");

        return new AuthoredIndexedRenderMeshSource(
            new MapObjectId(ParseId(wire.Id, "renderMeshes[].id")),
            new StandaloneWorldRenderMeshOwnership(),
            RequireText(wire.Material, "renderMeshes[].material"),
            ParseTriangleWinding(wire.TriangleWinding),
            vertices.Select(value =>
                new AuthoredRenderVertex(
                    FromWire(
                        Require(
                            value.Position,
                            "renderMeshes[].vertices[].position")),
                    FromWire(
                        Require(
                            value.Color,
                            "renderMeshes[].vertices[].color")),
                    FromWire(
                        Require(
                            value.TextureCoordinates,
                            "renderMeshes[].vertices[]" +
                            ".textureCoordinates")),
                    FromWire(
                        Require(
                            value.LightmapCoordinates,
                            "renderMeshes[].vertices[]" +
                            ".lightmapCoordinates")),
                    FromWire(
                        Require(
                            value.Normal,
                            "renderMeshes[].vertices[].normal")),
                    FromWire(
                        Require(
                            value.Tangent,
                            "renderMeshes[].vertices[].tangent")))),
            triangles.Select(value =>
                new AuthoredIndexedRenderTriangle(
                    value.Index0,
                    value.Index1,
                    value.Index2)));
    }

    private static AuthoredCollisionSource FromWire(
        WireCollisionSource wire)
    {
        RequireStandaloneOwnership(wire.Ownership, "collision");
        var objectId =
            new MapObjectId(
                ParseId(wire.Id, "collisionSources[].id"));

        return wire.Kind switch
        {
            ConvexBrushCollision => FromWireBrush(objectId, wire),
            TriangleMeshCollision => FromWireTriangleMesh(
                objectId,
                wire),
            _ => throw new InvalidDataException(
                $"Unsupported collision-source kind '{wire.Kind}'.")
        };
    }

    private static AuthoredConvexBrushCollisionSource FromWireBrush(
        MapObjectId objectId,
        WireCollisionSource wire)
    {
        if (wire.Contents is not { } contents ||
            wire.Faces is null ||
            wire.Vertices is not null ||
            wire.Triangles is not null)
        {
            throw new InvalidDataException(
                "A convex-brush collision row requires contents and faces " +
                "only.");
        }

        return new AuthoredConvexBrushCollisionSource(
            objectId,
            new StandaloneWorldCollisionSourceOwnership(),
            wire.Faces.Select(value =>
            {
                WirePlane plane =
                    Require(
                        value.Plane,
                        "collisionSources[].faces[].plane");
                return new AuthoredConvexBrushFace(
                    new AuthoredCollisionPlane(
                        FromWire(
                            Require(
                                plane.Normal,
                                "collisionSources[].faces[]" +
                                ".plane.normal")),
                        plane.Distance),
                    Require(
                            value.Winding,
                            "collisionSources[].faces[].winding")
                        .Select(FromWire),
                    FromWire(
                        Require(
                            value.Material,
                            "collisionSources[].faces[].material")));
            }),
            contents);
    }

    private static AuthoredIndexedTriangleMeshCollisionSource
        FromWireTriangleMesh(
            MapObjectId objectId,
            WireCollisionSource wire)
    {
        if (wire.Contents is not null ||
            wire.Faces is not null ||
            wire.Vertices is null ||
            wire.Triangles is null)
        {
            throw new InvalidDataException(
                "A triangle-mesh collision row requires vertices and " +
                "triangles only.");
        }

        return new AuthoredIndexedTriangleMeshCollisionSource(
            objectId,
            new StandaloneWorldCollisionSourceOwnership(),
            wire.Vertices.Select(FromWire),
            wire.Triangles.Select(value =>
                new AuthoredIndexedCollisionTriangle(
                    value.Vertex0,
                    value.Vertex1,
                    value.Vertex2,
                    new AuthoredTriangleEdgeWalkability(
                        Require(
                            value.Walkability,
                            "collisionSources[].triangles[]" +
                            ".walkability").Edge01,
                        value.Walkability.Edge12,
                        value.Walkability.Edge20),
                    FromWire(
                        Require(
                            value.Material,
                            "collisionSources[].triangles[]" +
                            ".material")))));
    }

    private static MapVector3 FromWire(WireVector3 value) =>
        new(value.X, value.Y, value.Z);

    private static AuthoredRenderUv FromWire(WireUv value) =>
        new(value.U, value.V);

    private static AuthoredRenderColor FromWire(WireColor value) =>
        new(value.Red, value.Green, value.Blue, value.Alpha);

    private static AuthoredCollisionMaterialInput FromWire(
        WireCollisionMaterial value) =>
        new(
            RequireText(
                value.Name,
                "collisionSources[].material.name"),
            value.SurfaceFlags,
            value.Contents);

    private static RenderTriangleWinding ParseTriangleWinding(
        string? value) =>
        value switch
        {
            CounterClockwiseFrontFace =>
                RenderTriangleWinding.CounterClockwiseFrontFace,
            ClockwiseFrontFace =>
                RenderTriangleWinding.ClockwiseFrontFace,
            _ => throw new InvalidDataException(
                $"Unsupported render triangle winding '{value}'.")
        };

    private static MinimalMultiplayerMapTargetStartupProfile
        ParseStartupProfile(string? value) =>
        value switch
        {
            OfflineSplitScreenFreeForAll =>
                MinimalMultiplayerMapTargetStartupProfile
                    .OfflineSplitScreenFreeForAll,
            _ => throw new InvalidDataException(
                $"Unsupported target startup profile '{value}'.")
        };

    private static void RequireStandaloneOwnership(
        string? value,
        string domain)
    {
        if (!string.Equals(
                value,
                StandaloneWorld,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $".iw4scene 1.0 supports standalone-world {domain} " +
                "ownership only.");
        }
    }

    private static Guid ParseId(string? value, string path)
    {
        if (!Guid.TryParseExact(value, "D", out Guid parsed) ||
            parsed == Guid.Empty)
        {
            throw new InvalidDataException(
                $"{path} must be a non-empty D-format GUID.");
        }

        return parsed;
    }

    private static string RequireText(string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{path} is required.");

        return value;
    }

    private static T Require<T>(T? value, string path)
        where T : class =>
        value ??
        throw new InvalidDataException($"{path} is required.");

    private sealed class WireDocument
    {
        [JsonPropertyOrder(0)]
        public required string Format { get; init; }

        [JsonPropertyOrder(1)]
        public required WireVersion Version { get; init; }

        [JsonPropertyOrder(2)]
        public required string DocumentId { get; init; }

        [JsonPropertyOrder(3)]
        public required long DocumentRevision { get; init; }

        [JsonPropertyOrder(4)]
        public required string MapAssetName { get; init; }

        [JsonPropertyOrder(5)]
        public required string TargetZoneName { get; init; }

        [JsonPropertyOrder(6)]
        public required string CompilerProfile { get; init; }

        [JsonPropertyOrder(7)]
        public required string EntityProfile { get; init; }

        [JsonPropertyOrder(8)]
        public required string StartupProfile { get; init; }

        [JsonPropertyOrder(9)]
        public required WireRenderMesh[] RenderMeshes { get; init; }

        [JsonPropertyOrder(10)]
        public required WireCollisionSource[] CollisionSources
        {
            get;
            init;
        }
    }

    private sealed class WireVersion
    {
        [JsonPropertyOrder(0)]
        public required int Major { get; init; }

        [JsonPropertyOrder(1)]
        public required int Minor { get; init; }
    }

    private sealed class WireRenderMesh
    {
        [JsonPropertyOrder(0)]
        public required string Kind { get; init; }

        [JsonPropertyOrder(1)]
        public required string Id { get; init; }

        [JsonPropertyOrder(2)]
        public required string Ownership { get; init; }

        [JsonPropertyOrder(3)]
        public required string Material { get; init; }

        [JsonPropertyOrder(4)]
        public required string TriangleWinding { get; init; }

        [JsonPropertyOrder(5)]
        public required WireRenderVertex[] Vertices { get; init; }

        [JsonPropertyOrder(6)]
        public required WireTriangle[] Triangles { get; init; }
    }

    private sealed class WireRenderVertex
    {
        [JsonPropertyOrder(0)]
        public required WireVector3 Position { get; init; }

        [JsonPropertyOrder(1)]
        public required WireColor Color { get; init; }

        [JsonPropertyOrder(2)]
        public required WireUv TextureCoordinates { get; init; }

        [JsonPropertyOrder(3)]
        public required WireUv LightmapCoordinates { get; init; }

        [JsonPropertyOrder(4)]
        public required WireVector3 Normal { get; init; }

        [JsonPropertyOrder(5)]
        public required WireVector3 Tangent { get; init; }
    }

    private sealed class WireTriangle
    {
        [JsonPropertyOrder(0)]
        public required int Index0 { get; init; }

        [JsonPropertyOrder(1)]
        public required int Index1 { get; init; }

        [JsonPropertyOrder(2)]
        public required int Index2 { get; init; }
    }

    private sealed class WireCollisionSource
    {
        [JsonPropertyOrder(0)]
        public required string Kind { get; init; }

        [JsonPropertyOrder(1)]
        public required string Id { get; init; }

        [JsonPropertyOrder(2)]
        public required string Ownership { get; init; }

        [JsonPropertyOrder(3)]
        public uint? Contents { get; init; }

        [JsonPropertyOrder(4)]
        public WireCollisionFace[]? Faces { get; init; }

        [JsonPropertyOrder(5)]
        public WireVector3[]? Vertices { get; init; }

        [JsonPropertyOrder(6)]
        public WireCollisionTriangle[]? Triangles { get; init; }
    }

    private sealed class WireCollisionFace
    {
        [JsonPropertyOrder(0)]
        public required WirePlane Plane { get; init; }

        [JsonPropertyOrder(1)]
        public required WireCollisionMaterial Material { get; init; }

        [JsonPropertyOrder(2)]
        public required WireVector3[] Winding { get; init; }
    }

    private sealed class WireCollisionTriangle
    {
        [JsonPropertyOrder(0)]
        public required int Vertex0 { get; init; }

        [JsonPropertyOrder(1)]
        public required int Vertex1 { get; init; }

        [JsonPropertyOrder(2)]
        public required int Vertex2 { get; init; }

        [JsonPropertyOrder(3)]
        public required WireWalkability Walkability { get; init; }

        [JsonPropertyOrder(4)]
        public required WireCollisionMaterial Material { get; init; }
    }

    private sealed class WirePlane
    {
        [JsonPropertyOrder(0)]
        public required WireVector3 Normal { get; init; }

        [JsonPropertyOrder(1)]
        public required float Distance { get; init; }
    }

    private sealed class WireCollisionMaterial
    {
        [JsonPropertyOrder(0)]
        public required string Name { get; init; }

        [JsonPropertyOrder(1)]
        public required int SurfaceFlags { get; init; }

        [JsonPropertyOrder(2)]
        public required int Contents { get; init; }
    }

    private sealed class WireWalkability
    {
        [JsonPropertyOrder(0)]
        public required bool Edge01 { get; init; }

        [JsonPropertyOrder(1)]
        public required bool Edge12 { get; init; }

        [JsonPropertyOrder(2)]
        public required bool Edge20 { get; init; }
    }

    private sealed class WireVector3
    {
        [JsonPropertyOrder(0)]
        public required float X { get; init; }

        [JsonPropertyOrder(1)]
        public required float Y { get; init; }

        [JsonPropertyOrder(2)]
        public required float Z { get; init; }
    }

    private sealed class WireUv
    {
        [JsonPropertyOrder(0)]
        public required float U { get; init; }

        [JsonPropertyOrder(1)]
        public required float V { get; init; }
    }

    private sealed class WireColor
    {
        [JsonPropertyOrder(0)]
        public required byte Red { get; init; }

        [JsonPropertyOrder(1)]
        public required byte Green { get; init; }

        [JsonPropertyOrder(2)]
        public required byte Blue { get; init; }

        [JsonPropertyOrder(3)]
        public required byte Alpha { get; init; }
    }
}
