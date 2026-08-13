using System.Numerics;

namespace IW4.Assets.XModel.Export;

/// <summary>
/// The complete, lossless XMODEL_EXPORT version-6 handoff for one loaded IW4
/// XModel LOD. Object names are deterministic surface identities (surf0,
/// surf1, ...); IW4 runtime assets do not retain Maya object names.
/// </summary>
public sealed record XModelExportDocument(
    IReadOnlyList<XModelExportBone> Bones,
    IReadOnlyList<XModelExportVertex> Vertices,
    IReadOnlyList<XModelExportTriangle> Triangles,
    IReadOnlyList<XModelExportObject> Objects,
    IReadOnlyList<XModelExportMaterial> Materials);

public sealed record XModelExportBone(
    string Name,
    int ParentIndex,
    Vector3 GlobalOffset,
    Quaternion GlobalRotation);

public sealed record XModelExportVertex(
    Vector3 Position,
    IReadOnlyList<XModelExportBoneWeight> Weights);

public sealed record XModelExportBoneWeight(int BoneIndex, float Weight);

public sealed record XModelExportTriangle(
    int ObjectIndex,
    int MaterialIndex,
    XModelExportCorner First,
    XModelExportCorner Second,
    XModelExportCorner Third);

public sealed record XModelExportCorner(
    int VertexIndex,
    Vector3 Normal,
    Vector4 Color,
    Vector2 Uv0);

public sealed record XModelExportObject(string SurfaceIdentity);

/// <summary>
/// IW4 retains material identity, but not source shader properties. The Phong
/// fields written for this row are the OpenAssetTools XModelCommon v6 handoff
/// defaults, not recovered IW4 material properties. ColorMapPath is populated
/// only from one unambiguous semantic-2 color image; otherwise it is empty.
/// </summary>
public sealed record XModelExportMaterial(
    string Name,
    string ColorMapPath);
