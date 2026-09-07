using System.Numerics;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.XModel;
using IW4.AssetExchange.XModel;

namespace MapConverter.Game.IW3.PC.Models;

/// <summary>
/// Compiles rigid IW3 visual geometry while retaining faces whose exported
/// attributes cannot produce a per-triangle tangent. XMODEL_EXPORT omits the
/// source tangent stream, so those faces use the angle-weighted tangent-space
/// reconstruction used by OpenAssetTools when it authors native XModel
/// vertices. Positions, normals, colors, UVs, and triangles remain unchanged.
/// </summary>
internal static class Iw3RigidSurfaceCompiler
{
    private const float UvDeterminantTolerance = 0.0000001f;
    private const float TangentLengthTolerance = 0.001f;
    private const int DObjSkelMatSize = 0x40;
    private const int MaximumExpandedTriangleCount = ushort.MaxValue / 3;

    internal static XModelExportLodCompileResult Compile(
        XModelExportDocument tangentSource,
        XModelExportDocument rigidPartitions,
        int boneCount,
        bool compileCollisionTrees)
    {
        ArgumentNullException.ThrowIfNull(tangentSource);
        ArgumentNullException.ThrowIfNull(rigidPartitions);
        if (tangentSource.Triangles.Count != rigidPartitions.Triangles.Count)
        {
            return Failure(
                "The tangent-source and rigid-partition triangle tables do not " +
                "have the same length.");
        }

        IndexedTriangle[] reconstructedTriangles = rigidPartitions.Triangles
            .Select((triangle, index) => new IndexedTriangle(index, triangle))
            .Where(value => RequiresReconstructedTangent(
                rigidPartitions,
                value.Triangle))
            .ToArray();
        if (reconstructedTriangles.Length == 0)
        {
            return XModelExportLodCompiler.Compile(
                rigidPartitions,
                boneCount,
                compileCollisionTrees);
        }

        if (!TryCalculateTangents(
                tangentSource,
                out IReadOnlyList<Vector3> tangentByCorner,
                out string? tangentBlocker))
        {
            return Failure(tangentBlocker!);
        }

        XModelExportDocument? regularDocument = SelectRegularTriangles(
            rigidPartitions);
        XModelExportLodCompileResult? regular = regularDocument is null
            ? null
            : XModelExportLodCompiler.Compile(
                regularDocument,
                boneCount,
                compileCollisionTrees);
        if (regular is not null && !regular.IsSuccess)
            return regular;

        var surfaces = new List<XSurface>(
            (regular?.Surfaces.Count ?? 0) + reconstructedTriangles.Length);
        var materialIndices = new List<int>(surfaces.Capacity);
        var partBits = new uint[6];
        if (regular is not null)
        {
            surfaces.AddRange(regular.Surfaces);
            materialIndices.AddRange(regular.ImportedMaterialIndices);
            OrPartBits(partBits, regular.PartBits);
        }

        foreach (IGrouping<(int Object, int Material), IndexedTriangle> partition
                 in reconstructedTriangles
                     .GroupBy(value => (
                         Object: value.Triangle.ObjectIndex,
                         Material: value.Triangle.MaterialIndex))
                     .OrderBy(group => group.Key.Object)
                     .ThenBy(group => group.Key.Material))
        {
            IndexedTriangle[] triangles = partition.ToArray();
            for (int offset = 0;
                 offset < triangles.Length;
                 offset += MaximumExpandedTriangleCount)
            {
                int count = Math.Min(
                    MaximumExpandedTriangleCount,
                    triangles.Length - offset);
                if (!TryCompileReconstructedTangentSurface(
                        rigidPartitions,
                        triangles.AsSpan(offset, count),
                        tangentByCorner,
                        boneCount,
                        partition.Key.Object,
                        partition.Key.Material,
                        out XSurface? surface,
                        out string? blocker))
                {
                    return Failure(blocker!);
                }
                if (compileCollisionTrees &&
                    !XModelCollisionTreeCompiler.TryAttach(
                        surface!,
                        $"object {partition.Key.Object} material " +
                        $"{partition.Key.Material} reconstructed triangles " +
                        $"{offset}-{offset + count - 1}",
                        out surface,
                        out string? collisionBlocker))
                {
                    return Failure(collisionBlocker!);
                }
                surfaces.Add(surface!);
                materialIndices.Add(partition.Key.Material);
                OrPartBits(partBits, surface!.PartBits);
            }
        }

        if (surfaces.Count > byte.MaxValue)
        {
            return Failure(
                "The imported LOD surface count exceeds the XModel byte limit " +
                "after retaining source-reconstructed tangent triangles.");
        }
        return new XModelExportLodCompileResult(
            Array.AsReadOnly(surfaces.ToArray()),
            Array.AsReadOnly(materialIndices.ToArray()),
            Array.AsReadOnly(partBits),
            []);
    }

    internal static XModelExportLodCompileResult CoalesceStaticSurfaces(
        XModelExportLodCompileResult compiled)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        if (!compiled.IsSuccess || compiled.Surfaces.Count <= GfxStaticModelDrawInst.MaxLodSurfaceCount)
            return compiled;
        if (compiled.Surfaces.Count != compiled.ImportedMaterialIndices.Count)
            return Failure("Static surface coalescing requires one material row per surface.");

        var groups = compiled.Surfaces.Select((surface, index) => (
                Surface: surface,
                Material: compiled.ImportedMaterialIndices[index]))
            .GroupBy(value => (value.Material, value.Surface.TileMode,
                value.Surface.StreamFlags, value.Surface.Pad03))
            .ToArray();
        if (groups.Length > GfxStaticModelDrawInst.MaxLodSurfaceCount)
        {
            return Failure(
                $"The static LOD needs {groups.Length} incompatible material/stream groups; " +
                $"the native draw limit is {GfxStaticModelDrawInst.MaxLodSurfaceCount} surfaces.");
        }

        var surfaces = new List<XSurface>(groups.Length);
        var materialIndices = new List<int>(groups.Length);
        foreach (var group in groups)
        {
            XSurface[] sources = group.Select(value => value.Surface).ToArray();
            XSurface first = sources[0];
            if (sources.Length == 1)
            {
                surfaces.Add(first);
                materialIndices.Add(group.Key.Material);
                continue;
            }

            int vertexCount = sources.Sum(surface => (int)surface.VertCount);
            int triangleCount = sources.Sum(surface => (int)surface.TriCount);
            if (vertexCount > ushort.MaxValue || triangleCount > ushort.MaxValue)
                return Failure($"Static material {group.Key.Material} exceeds native vertex or triangle counts after coalescing.");

            var verts0 = new byte[checked(vertexCount * XSurfaceVertexCodec.StreamStride)];
            var verts1 = new byte[verts0.Length];
            var indices = new ushort[checked(triangleCount * 3)];
            var rigidLists = new List<XRigidVertList>();
            var partBits = new uint[6];
            int vertexBase = 0;
            int triangleBase = 0;
            foreach (XSurface source in sources)
            {
                if (source.Deformed || source.VertListCount == 0 ||
                    source.VertListCount != source.VertList.Count ||
                    source.Verts0.Count != source.VertCount * XSurfaceVertexCodec.StreamStride ||
                    source.Verts1.Count != source.Verts0.Count ||
                    source.TriIndices.Count != source.TriCount * 3)
                {
                    return Failure($"Static material {group.Key.Material} has incomplete rigid geometry for coalescing.");
                }

                // Export positions are already model-space. Retain both packed
                // streams, including the reconstructed tangent bytes, verbatim.
                int byteBase = vertexBase * XSurfaceVertexCodec.StreamStride;
                for (int index = 0; index < source.Verts0.Count; index++)
                {
                    verts0[byteBase + index] = source.Verts0[index];
                    verts1[byteBase + index] = source.Verts1[index];
                }
                for (int index = 0; index < source.TriIndices.Count; index++)
                {
                    ushort vertex = source.TriIndices[index];
                    if (vertex >= source.VertCount)
                        return Failure($"Static material {group.Key.Material} has an out-of-range triangle vertex.");
                    indices[triangleBase * 3 + index] = checked((ushort)(vertexBase + vertex));
                }
                foreach (XRigidVertList rigid in source.VertList)
                {
                    XSurfaceCollisionTree? tree = rigid.CollisionTree;
                    if (tree is not null)
                    {
                        if (!XModelCollisionTreeValidator.TryValidate(tree, rigid, source,
                                $"static material {group.Key.Material} source collision", out string? blocker))
                            return Failure(blocker ?? "The source static collision tree is invalid.");
                        var leafs = new XSurfaceCollisionLeaf[tree.Leafs.Count];
                        for (int index = 0; index < leafs.Length; index++)
                        {
                            int rebased = tree.Leafs[index].TriangleBeginIndex + triangleBase;
                            if (rebased > ushort.MaxValue)
                                return Failure($"Static material {group.Key.Material} collision leaf exceeds its native encoding after coalescing.");
                            leafs[index] = new XSurfaceCollisionLeaf(checked((ushort)rebased));
                        }
                        tree = new XSurfaceCollisionTree
                        {
                            Trans = tree.Trans,
                            Scale = tree.Scale,
                            NodeCount = tree.NodeCount,
                            Nodes = tree.Nodes,
                            LeafCount = leafs.Length,
                            Leafs = Array.AsReadOnly(leafs)
                        };
                    }
                    rigidLists.Add(new XRigidVertList
                    {
                        BoneOffset = rigid.BoneOffset,
                        VertCount = rigid.VertCount,
                        TriOffset = checked((ushort)(rigid.TriOffset + triangleBase)),
                        TriCount = rigid.TriCount,
                        CollisionTree = tree
                    });
                }
                OrPartBits(partBits, source.PartBits);
                vertexBase += source.VertCount;
                triangleBase += source.TriCount;
            }
            var merged = new XSurface
            {
                TileMode = first.TileMode,
                DeformedRaw = 0,
                StreamFlags = first.StreamFlags,
                Pad03 = first.Pad03,
                VertCount = checked((ushort)vertexCount),
                TriCount = checked((ushort)triangleCount),
                TriIndices = Array.AsReadOnly(indices),
                Verts0 = Array.AsReadOnly(verts0),
                Verts1 = Array.AsReadOnly(verts1),
                VertListCount = rigidLists.Count,
                VertList = Array.AsReadOnly(rigidLists.ToArray()),
                PartBits = Array.AsReadOnly(partBits)
            };
            foreach (XRigidVertList rigid in merged.VertList)
            {
                // The owning validator also rejects any encoded leaf carry
                // that would change its pair flag or leave its rebased range.
                if (rigid.CollisionTree is { } tree &&
                    !XModelCollisionTreeValidator.TryValidate(tree, rigid, merged,
                        $"static material {group.Key.Material} merged collision", out string? blocker))
                    return Failure(blocker ?? "The coalesced static collision tree is invalid.");
            }
            surfaces.Add(merged);
            materialIndices.Add(group.Key.Material);
        }
        return new XModelExportLodCompileResult(
            Array.AsReadOnly(surfaces.ToArray()),
            Array.AsReadOnly(materialIndices.ToArray()),
            compiled.PartBits,
            []);
    }

    private static XModelExportDocument? SelectRegularTriangles(
        XModelExportDocument source)
    {
        XModelExportTriangle[] triangles = source.Triangles
            .Where(triangle => !RequiresReconstructedTangent(source, triangle))
            .ToArray();
        if (triangles.Length == 0)
            return null;

        var usedObjects = triangles
            .Select(triangle => triangle.ObjectIndex)
            .ToHashSet();
        var objects = new List<XModelExportObject>(usedObjects.Count);
        var remappedObjectIndices = new Dictionary<int, int>();
        for (int objectIndex = 0;
             objectIndex < source.Objects.Count;
             objectIndex++)
        {
            if (!usedObjects.Contains(objectIndex))
                continue;
            remappedObjectIndices.Add(objectIndex, objects.Count);
            objects.Add(source.Objects[objectIndex]);
        }
        if (remappedObjectIndices.Count != usedObjects.Count)
        {
            // Leave the invalid object reference for the strict shared compiler
            // to report rather than silently omitting its triangle.
            return source;
        }

        XModelExportTriangle[] remappedTriangles = triangles
            .Select(triangle => triangle with
            {
                ObjectIndex = remappedObjectIndices[triangle.ObjectIndex]
            })
            .ToArray();
        return new XModelExportDocument(
            source.Bones,
            source.Vertices,
            Array.AsReadOnly(remappedTriangles),
            Array.AsReadOnly(objects.ToArray()),
            source.Materials);
    }

    private static bool TryCompileReconstructedTangentSurface(
        XModelExportDocument document,
        ReadOnlySpan<IndexedTriangle> triangles,
        IReadOnlyList<Vector3> tangentByCorner,
        int boneCount,
        int objectIndex,
        int materialIndex,
        out XSurface? surface,
        out string? blocker)
    {
        surface = null;
        blocker = null;
        if ((uint)objectIndex >= (uint)document.Objects.Count)
        {
            blocker =
                $"Source-reconstructed tangent object row {objectIndex} is out " +
                "of range.";
            return false;
        }
        if ((uint)materialIndex >= (uint)document.Materials.Count)
        {
            blocker =
                $"Source-reconstructed tangent material row {materialIndex} is " +
                "out of range.";
            return false;
        }
        if (triangles.Length == 0 ||
            triangles.Length > MaximumExpandedTriangleCount)
        {
            blocker =
                $"Object {objectIndex} material {materialIndex} has an invalid " +
                "source-reconstructed tangent triangle chunk size.";
            return false;
        }

        int vertexCount = checked(triangles.Length * 3);
        var verts0 = new byte[checked(
            vertexCount * XSurfaceVertexCodec.StreamStride)];
        var verts1 = new byte[verts0.Length];
        var indices = new ushort[vertexCount];
        int? rigidBoneIndex = null;
        int outputVertexIndex = 0;
        for (int localTriangleIndex = 0;
             localTriangleIndex < triangles.Length;
             localTriangleIndex++)
        {
            IndexedTriangle indexedTriangle = triangles[localTriangleIndex];
            XModelExportTriangle triangle = indexedTriangle.Triangle;
            if (triangle.ObjectIndex != objectIndex ||
                triangle.MaterialIndex != materialIndex)
            {
                blocker =
                    $"Object {objectIndex} material {materialIndex} contains a " +
                    "mismatched source-reconstructed tangent triangle row.";
                return false;
            }

            XModelExportCorner[] corners =
                [triangle.First, triangle.Second, triangle.Third];
            for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
            {
                XModelExportCorner corner = corners[cornerIndex];
                if ((uint)corner.VertexIndex >= (uint)document.Vertices.Count)
                {
                    blocker =
                        $"Object {objectIndex} material {materialIndex} triangle " +
                        $"{indexedTriangle.Index} corner {cornerIndex} references " +
                        "an out-of-range vertex.";
                    return false;
                }
                XModelExportVertex vertex = document.Vertices[corner.VertexIndex];
                if (vertex.Weights.Count != 1 ||
                    (uint)vertex.Weights[0].BoneIndex >= (uint)boneCount)
                {
                    blocker =
                        $"Object {objectIndex} material {materialIndex} triangle " +
                        $"{indexedTriangle.Index} corner {cornerIndex} is not " +
                        "rigidly weighted to a representable bone.";
                    return false;
                }
                int boneIndex = vertex.Weights[0].BoneIndex;
                if (rigidBoneIndex.HasValue && rigidBoneIndex.Value != boneIndex)
                {
                    blocker =
                        $"Object {objectIndex} material {materialIndex} contains " +
                        "source-reconstructed tangent triangles spanning " +
                        "multiple rigid bones.";
                    return false;
                }
                rigidBoneIndex = boneIndex;

                int tangentIndex = checked(indexedTriangle.Index * 3 + cornerIndex);
                if ((uint)tangentIndex >= (uint)tangentByCorner.Count)
                {
                    blocker =
                        $"Object {objectIndex} material {materialIndex} triangle " +
                        $"{indexedTriangle.Index} has no reconstructed tangent.";
                    return false;
                }
                try
                {
                    XSurfaceVertexCodec.WriteVertex(
                        verts0,
                        verts1,
                        outputVertexIndex,
                        vertex.Position,
                        corner.Uv0,
                        corner.Color,
                        corner.Normal,
                        tangentByCorner[tangentIndex]);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    blocker =
                        $"Object {objectIndex} material {materialIndex} triangle " +
                        $"{indexedTriangle.Index} corner {cornerIndex} cannot be " +
                        $"represented by the native vertex stream: {exception.Message}";
                    return false;
                }
                indices[outputVertexIndex] = checked((ushort)outputVertexIndex);
                outputVertexIndex++;
            }
        }

        var partBits = new uint[6];
        SetPartBit(partBits, rigidBoneIndex!.Value);
        surface = new XSurface
        {
            DeformedRaw = 0,
            StreamFlags = XSurfaceStreamFlags.None,
            VertCount = checked((ushort)vertexCount),
            TriCount = checked((ushort)triangles.Length),
            TriIndices = Array.AsReadOnly(indices),
            VertexInfo = new XSurfaceVertexInfo(),
            Verts0 = Array.AsReadOnly(verts0),
            Verts1 = Array.AsReadOnly(verts1),
            VertListCount = 1,
            VertList =
            [
                new XRigidVertList
                {
                    BoneOffset = checked((ushort)(
                        rigidBoneIndex.Value * DObjSkelMatSize)),
                    VertCount = checked((ushort)vertexCount),
                    TriOffset = 0,
                    TriCount = checked((ushort)triangles.Length)
                }
            ],
            PartBits = Array.AsReadOnly(partBits)
        };
        return true;
    }

    private static bool TryCalculateTangents(
        XModelExportDocument document,
        out IReadOnlyList<Vector3> tangentByCorner,
        out string? blocker)
    {
        blocker = null;
        tangentByCorner = [];
        var vertexIndexByIdentity = new Dictionary<TangentVertexIdentity, int>();
        var vertices = new List<TangentVertex>();
        var vertexIndexByCorner = new int[checked(
            document.Triangles.Count * 3)];

        for (int triangleIndex = 0;
             triangleIndex < document.Triangles.Count;
             triangleIndex++)
        {
            XModelExportTriangle triangle = document.Triangles[triangleIndex];
            if ((uint)triangle.ObjectIndex >= (uint)document.Objects.Count)
            {
                blocker =
                    $"Triangle {triangleIndex} references out-of-range object row " +
                    $"{triangle.ObjectIndex}.";
                return false;
            }
            XModelExportCorner[] corners =
                [triangle.First, triangle.Second, triangle.Third];
            for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
            {
                XModelExportCorner corner = corners[cornerIndex];
                if ((uint)corner.VertexIndex >= (uint)document.Vertices.Count)
                {
                    blocker =
                        $"Triangle {triangleIndex} corner {cornerIndex} references " +
                        $"out-of-range vertex row {corner.VertexIndex}.";
                    return false;
                }
                XModelExportVertex sourceVertex =
                    document.Vertices[corner.VertexIndex];
                if (!IsFinite(sourceVertex.Position) ||
                    !IsFinite(corner.Normal) ||
                    !IsFinite(corner.Uv0) ||
                    !IsFinite(corner.Color))
                {
                    blocker =
                        $"Triangle {triangleIndex} corner {cornerIndex} has a " +
                        "non-finite position, normal, color, or UV.";
                    return false;
                }

                var identity = new TangentVertexIdentity(
                    triangle.ObjectIndex,
                    corner.VertexIndex,
                    corner.Normal,
                    corner.Color,
                    corner.Uv0);
                if (!vertexIndexByIdentity.TryGetValue(
                        identity,
                        out int tangentVertexIndex))
                {
                    tangentVertexIndex = vertices.Count;
                    vertexIndexByIdentity.Add(identity, tangentVertexIndex);
                    vertices.Add(new TangentVertex(
                        sourceVertex.Position,
                        corner.Normal));
                }
                vertexIndexByCorner[triangleIndex * 3 + cornerIndex] =
                    tangentVertexIndex;
            }
        }

        for (int triangleIndex = 0;
             triangleIndex < document.Triangles.Count;
             triangleIndex++)
        {
            int baseCorner = triangleIndex * 3;
            TangentVertex first = vertices[vertexIndexByCorner[baseCorner]];
            TangentVertex second = vertices[vertexIndexByCorner[baseCorner + 1]];
            TangentVertex third = vertices[vertexIndexByCorner[baseCorner + 2]];
            XModelExportTriangle triangle = document.Triangles[triangleIndex];
            CalculateTriangleDirections(
                first.Position,
                second.Position,
                third.Position,
                triangle.First.Uv0,
                triangle.Second.Uv0,
                triangle.Third.Uv0,
                out Vector3 tangent,
                out Vector3 binormal);
            Vector3 exteriorAngles = CalculateExteriorAngles(
                first.Position,
                second.Position,
                third.Position);
            Accumulate(first, tangent, binormal, exteriorAngles.X);
            Accumulate(second, tangent, binormal, exteriorAngles.Y);
            Accumulate(third, tangent, binormal, exteriorAngles.Z);
        }

        var finalTangents = new Vector3[vertices.Count];
        for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
        {
            TangentVertex vertex = vertices[vertexIndex];
            Vector3 tangent = vertex.AccumulatedTangent -
                vertex.Normal * Vector3.Dot(
                    vertex.Normal,
                    vertex.AccumulatedTangent);
            if (NormalizeWithLength(ref tangent) < TangentLengthTolerance)
            {
                tangent = Vector3.Cross(
                    vertex.AccumulatedBinormal,
                    vertex.Normal);
                if (NormalizeWithLength(ref tangent) < TangentLengthTolerance)
                    tangent = OrthogonalDirection(vertex.Normal);
            }
            if (!IsFinite(tangent))
            {
                blocker =
                    $"Reconstructed tangent vertex {vertexIndex} is non-finite.";
                return false;
            }
            finalTangents[vertexIndex] = tangent;
        }

        Vector3[] cornerTangents = vertexIndexByCorner
            .Select(index => finalTangents[index])
            .ToArray();
        tangentByCorner = Array.AsReadOnly(cornerTangents);
        return true;
    }

    private static void CalculateTriangleDirections(
        Vector3 firstPosition,
        Vector3 secondPosition,
        Vector3 thirdPosition,
        Vector2 firstUv,
        Vector2 secondUv,
        Vector2 thirdUv,
        out Vector3 tangent,
        out Vector3 binormal)
    {
        Vector2 firstDeltaUv = secondUv - firstUv;
        Vector2 secondDeltaUv = thirdUv - firstUv;
        Vector3 firstDeltaPosition = secondPosition - firstPosition;
        Vector3 secondDeltaPosition = thirdPosition - firstPosition;
        float determinant =
            firstDeltaUv.X * secondDeltaUv.Y -
            firstDeltaUv.Y * secondDeltaUv.X;
        if (determinant >= 0f)
        {
            tangent = firstDeltaPosition * secondDeltaUv.Y -
                secondDeltaPosition * firstDeltaUv.Y;
            binormal = secondDeltaPosition * firstDeltaUv.X -
                firstDeltaPosition * secondDeltaUv.X;
        }
        else
        {
            tangent = secondDeltaPosition * firstDeltaUv.Y -
                firstDeltaPosition * secondDeltaUv.Y;
            binormal = firstDeltaPosition * secondDeltaUv.X -
                secondDeltaPosition * firstDeltaUv.X;
        }
        NormalizeWithLength(ref tangent);
        NormalizeWithLength(ref binormal);
    }

    private static Vector3 CalculateExteriorAngles(
        Vector3 first,
        Vector3 second,
        Vector3 third)
    {
        Vector3 firstToSecond = first - second;
        Vector3 secondToThird = second - third;
        Vector3 thirdToFirst = third - first;
        NormalizeWithLength(ref firstToSecond);
        NormalizeWithLength(ref secondToThird);
        NormalizeWithLength(ref thirdToFirst);
        return new Vector3(
            AngleBetween(firstToSecond, thirdToFirst),
            AngleBetween(secondToThird, firstToSecond),
            AngleBetween(thirdToFirst, secondToThird));
    }

    private static float AngleBetween(Vector3 first, Vector3 second)
    {
        float dot = Vector3.Dot(first, second);
        if (dot <= -1f)
            return -MathF.PI;
        if (dot >= 1f)
            return MathF.PI;
        return MathF.Acos(dot);
    }

    private static void Accumulate(
        TangentVertex vertex,
        Vector3 tangent,
        Vector3 binormal,
        float exteriorAngle)
    {
        vertex.AccumulatedTangent += tangent * exteriorAngle;
        vertex.AccumulatedBinormal += binormal * exteriorAngle;
    }

    private static Vector3 OrthogonalDirection(Vector3 normal)
    {
        float xSquared = normal.X * normal.X;
        float ySquared = normal.Y * normal.Y;
        float zSquared = normal.Z * normal.Z;
        int axis = xSquared <= ySquared
            ? (xSquared <= zSquared ? 0 : 2)
            : (ySquared <= zSquared ? 1 : 2);
        float component = axis switch
        {
            0 => normal.X,
            1 => normal.Y,
            _ => normal.Z
        };
        Vector3 tangent = normal * -component;
        tangent = axis switch
        {
            0 => tangent with { X = tangent.X + 1f },
            1 => tangent with { Y = tangent.Y + 1f },
            _ => tangent with { Z = tangent.Z + 1f }
        };
        NormalizeWithLength(ref tangent);
        return tangent;
    }

    private static float NormalizeWithLength(ref Vector3 value)
    {
        float length = value.Length();
        if (!float.IsFinite(length) || length <= 0f)
        {
            value = Vector3.Zero;
            return 0f;
        }
        value /= length;
        return length;
    }

    private static bool HasDegenerateUv(XModelExportTriangle triangle)
    {
        Vector2 first = triangle.Second.Uv0 - triangle.First.Uv0;
        Vector2 second = triangle.Third.Uv0 - triangle.First.Uv0;
        float determinant = first.X * second.Y - first.Y * second.X;
        return !float.IsFinite(determinant) ||
            MathF.Abs(determinant) < UvDeterminantTolerance;
    }

    private static bool RequiresReconstructedTangent(
        XModelExportDocument document,
        XModelExportTriangle triangle)
    {
        if (HasDegenerateUv(triangle))
            return true;

        XModelExportCorner[] corners =
            [triangle.First, triangle.Second, triangle.Third];
        if (corners.Any(corner =>
                (uint)corner.VertexIndex >= (uint)document.Vertices.Count ||
                !IsFinite(corner.Normal)))
        {
            return false;
        }
        Vector3 firstPosition =
            document.Vertices[triangle.First.VertexIndex].Position;
        Vector3 secondPosition =
            document.Vertices[triangle.Second.VertexIndex].Position;
        Vector3 thirdPosition =
            document.Vertices[triangle.Third.VertexIndex].Position;
        if (!IsFinite(firstPosition) ||
            !IsFinite(secondPosition) ||
            !IsFinite(thirdPosition))
        {
            return false;
        }

        Vector2 firstUv = triangle.Second.Uv0 - triangle.First.Uv0;
        Vector2 secondUv = triangle.Third.Uv0 - triangle.First.Uv0;
        float determinant =
            firstUv.X * secondUv.Y - firstUv.Y * secondUv.X;
        Vector3 rawTangent =
            ((secondPosition - firstPosition) * secondUv.Y -
             (thirdPosition - firstPosition) * firstUv.Y) / determinant;
        foreach (XModelExportCorner corner in corners)
        {
            Vector3 tangent = rawTangent -
                corner.Normal * Vector3.Dot(rawTangent, corner.Normal);
            if (!IsFinite(tangent) || tangent.LengthSquared() <= 0f)
                return true;
        }
        return false;
    }

    private static void SetPartBit(uint[] bits, int boneIndex)
    {
        if ((uint)boneIndex >= (uint)(bits.Length * 32))
        {
            throw new InvalidDataException(
                "Bone index exceeds the six-word IW4 part-bit table.");
        }
        bits[boneIndex / 32] |= 0x80000000u >> (boneIndex % 32);
    }

    private static void OrPartBits(
        uint[] destination,
        IReadOnlyList<uint> source)
    {
        if (source.Count != destination.Length)
        {
            throw new InvalidDataException(
                "Compiled XSurface part bits do not contain six words.");
        }
        for (int index = 0; index < destination.Length; index++)
            destination[index] |= source[index];
    }

    private static XModelExportLodCompileResult Failure(string blocker) => new(
        [],
        [],
        Array.AsReadOnly(new uint[6]),
        Array.AsReadOnly(new[] { blocker }));

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private readonly record struct IndexedTriangle(
        int Index,
        XModelExportTriangle Triangle);

    private readonly record struct TangentVertexIdentity(
        int ObjectIndex,
        int PositionAndWeightIndex,
        Vector3 Normal,
        Vector4 Color,
        Vector2 Uv);

    private sealed class TangentVertex(Vector3 position, Vector3 normal)
    {
        internal Vector3 Position { get; } = position;
        internal Vector3 Normal { get; } = normal;
        internal Vector3 AccumulatedTangent { get; set; }
        internal Vector3 AccumulatedBinormal { get; set; }
    }
}
