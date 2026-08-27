using IW4.Render.Techniques;
using System.Numerics;
using IW4.Render.Execution;
using IW4.Render.Materials;

namespace IW4.Render;

public sealed class XModelRenderScene
{
    internal XModelRenderScene(
        string name,
        IReadOnlyList<XModelRenderLod> lods,
        int defaultLodIndex,
        RenderBounds bounds,
        IReadOnlyList<XModelRenderBone> bones,
        IReadOnlyList<string> diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(lods);
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Name = name;
        Lods = Array.AsReadOnly(lods.ToArray());
        DefaultLodIndex = defaultLodIndex;
        Bounds = bounds;
        Bones = Array.AsReadOnly(bones.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public string Name { get; }

    public IReadOnlyList<XModelRenderLod> Lods { get; }

    public int DefaultLodIndex { get; }

    public RenderBounds Bounds { get; }

    public IReadOnlyList<XModelRenderBone> Bones { get; }

    public IReadOnlyList<string> Diagnostics { get; }
}

public sealed class XModelRenderLod
{
    internal XModelRenderLod(
        int lodIndex,
        float distance,
        RenderBounds bounds,
        IReadOnlyList<XModelRenderSurface> surfaces)
    {
        ArgumentNullException.ThrowIfNull(surfaces);

        LodIndex = lodIndex;
        Distance = distance;
        Bounds = bounds;
        Surfaces = Array.AsReadOnly(surfaces.ToArray());
        TriangleCount = checked(Surfaces.Sum(surface =>
            surface.Indices.Count / 3));
        CollisionTriangleCount = checked(Surfaces.Sum(surface => surface.CollisionIndices.Count / 3));
        VertexCount = checked(Surfaces.Sum(surface =>
            surface.Positions.Count));
        HasCompleteSkinning =
            Surfaces.Count > 0 &&
            Surfaces.All(surface => surface.HasCompleteSkinning);
    }

    public int LodIndex { get; }

    public float Distance { get; }

    public RenderBounds Bounds { get; }

    public IReadOnlyList<XModelRenderSurface> Surfaces { get; }

    public int TriangleCount { get; }

    public int VertexCount { get; }
    public int CollisionTriangleCount { get; }

    internal bool HasCompleteSkinning { get; }
}

public sealed class XModelRenderSurface
{
    internal XModelRenderSurface(
        int geometrySurfaceIndex,
        int parentMaterialIndex,
        string materialName,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<XModelRenderSkinningVertex>? skinningVertices,
        IReadOnlyList<uint> indices,
        IReadOnlyList<uint> collisionIndices,
        RenderBounds bounds,
        int selectedTechniqueSlot,
        string selectedTechniqueName,
        IReadOnlyList<XModelRenderAuthoredPass> authoredPasses,
        bool authoredGroupReady,
        string authoredMaterialStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialName);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(collisionIndices);
        ArgumentNullException.ThrowIfNull(authoredPasses);
        ArgumentNullException.ThrowIfNull(authoredMaterialStatus);
        if (indices.Count % 3 != 0)
        {
            throw new ArgumentException(
                "XModel surface indices must contain complete triangles.",
                nameof(indices));
        }
        if (skinningVertices is not null &&
            skinningVertices.Count != positions.Count)
        {
            throw new ArgumentException(
                "XModel surface skinning must cover every projected vertex.",
                nameof(skinningVertices));
        }
        int exactRsxPayloadLength = checked(
            positions.Count *
            Geometry.XSurfaceVertexDecoder.RsxVertexInputCount *
            Geometry.XSurfaceVertexDecoder.RsxVertexInputComponentCount);
        if (authoredPasses.Any(pass =>
                pass.RsxVertexInputs.Length != 0 &&
                pass.RsxVertexInputs.Length != exactRsxPayloadLength))
        {
            throw new ArgumentException(
                "XModel authored-pass RSX vertex payloads must be empty when blocked or contain one 16-vec4 slab per projected vertex.",
                nameof(authoredPasses));
        }

        GeometrySurfaceIndex = geometrySurfaceIndex;
        ParentMaterialIndex = parentMaterialIndex;
        MaterialName = materialName;
        Positions = Array.AsReadOnly(positions.ToArray());
        SkinningVertices = skinningVertices is null
            ? null
            : Array.AsReadOnly(skinningVertices.ToArray());
        Indices = Array.AsReadOnly(indices.ToArray());
        CollisionIndices = Array.AsReadOnly(collisionIndices.ToArray());
        Bounds = bounds;
        SelectedTechniqueSlot = selectedTechniqueSlot;
        SelectedTechniqueName = selectedTechniqueName;
        AuthoredPasses = Array.AsReadOnly(authoredPasses.ToArray());
        AuthoredGroupReady = authoredGroupReady;
        AuthoredMaterialStatus = authoredMaterialStatus;
    }

    public int GeometrySurfaceIndex { get; }

    public int ParentMaterialIndex { get; }

    public string MaterialName { get; }

    public IReadOnlyList<Vector3> Positions { get; }

    internal bool HasCompleteSkinning => SkinningVertices is not null;

    public IReadOnlyList<uint> Indices { get; }
    public IReadOnlyList<uint> CollisionIndices { get; }

    public RenderBounds Bounds { get; }

    public int SelectedTechniqueSlot { get; }

    public string SelectedTechniqueName { get; }

    public int AuthoredPassCount => AuthoredPasses.Count;

    public bool AuthoredGroupReady { get; }

    public string AuthoredMaterialStatus { get; }

    internal IReadOnlyList<XModelRenderAuthoredPass> AuthoredPasses { get; }

    internal IReadOnlyList<XModelRenderSkinningVertex>?
        SkinningVertices { get; }
}

internal readonly record struct XModelRenderBoneInfluence(
    int BoneIndex,
    float Weight);

internal sealed record XModelRenderSkinningVertex(
    Vector3 BindPosition,
    Vector3 BindNormal,
    Vector3 BindTangent,
    XModelRenderBoneInfluence[] Influences);

internal sealed class XModelRenderAuthoredPass
{
    internal const string ViewerReflectionProbeResourceIdentity =
        "XMODEL_VIEWER_REFLECTION_PROBE";

    internal XModelRenderAuthoredPass(
        int groupId,
        int groupPassIndex,
        MaterialPassIdentity pass,
        MaterialSamplerIdentity? primarySampler,
        RenderState state,
        ShaderExecutionContract shaderExecution,
        IReadOnlyList<MaterialSamplerBinding> materialSamplers,
        float[] rsxVertexInputs,
        string diagnostic)
    {
        if (groupId < 0)
            throw new ArgumentOutOfRangeException(nameof(groupId));
        if (groupPassIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(groupPassIndex));
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(shaderExecution);
        ArgumentNullException.ThrowIfNull(materialSamplers);
        ArgumentNullException.ThrowIfNull(rsxVertexInputs);
        ArgumentNullException.ThrowIfNull(diagnostic);

        GroupId = groupId;
        GroupPassIndex = groupPassIndex;
        Pass = pass;
        PrimarySampler = primarySampler;
        State = state;
        ShaderExecution = shaderExecution;
        MaterialSamplers = Array.AsReadOnly(materialSamplers.ToArray());
        RsxVertexInputs = rsxVertexInputs.ToArray();
        Diagnostic = diagnostic;
    }

    internal int GroupId { get; }

    internal int GroupPassIndex { get; }

    internal MaterialPassIdentity Pass { get; }

    internal MaterialSamplerIdentity? PrimarySampler { get; }

    internal RenderState State { get; }

    internal ShaderExecutionContract ShaderExecution { get; }

    internal IReadOnlyList<MaterialSamplerBinding>
        MaterialSamplers { get; }

    internal float[] RsxVertexInputs { get; }

    internal string Diagnostic { get; }
}

public sealed class XModelRenderBone
{
    internal XModelRenderBone(
        int boneIndex,
        string name,
        Vector3 position)
    {
        if (boneIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(boneIndex));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        BoneIndex = boneIndex;
        Name = name;
        Position = position;
    }

    public int BoneIndex { get; }

    public string Name { get; }

    public Vector3 Position { get; }
}
