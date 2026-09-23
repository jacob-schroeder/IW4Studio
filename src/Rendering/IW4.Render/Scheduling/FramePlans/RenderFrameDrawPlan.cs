using System.Collections.Immutable;
using System.Numerics;

using IW4.Render.Execution.FixedFunction;
using IW4.Render.Resources;
using IW4.Render.Shaders;
using IW4.Render.Textures;

namespace IW4.Render.Scheduling.FramePlans;

public readonly record struct RenderGeometrySlice
{
    public RenderGeometrySlice(
        RenderSemanticIdentity geometry,
        RenderSemanticIdentity vertexLayout,
        RenderPrimitiveTopology topology,
        int firstVertex,
        int vertexCount,
        int firstIndex,
        int indexCount,
        RenderIndexFormat indexFormat)
    {
        RequireKind(geometry, RenderSemanticResourceKind.Geometry);
        RequireKind(vertexLayout, RenderSemanticResourceKind.VertexLayout);
        if (!Enum.IsDefined(topology))
            throw new ArgumentOutOfRangeException(nameof(topology));
        if (!Enum.IsDefined(indexFormat))
            throw new ArgumentOutOfRangeException(nameof(indexFormat));
        if (firstVertex < 0)
            throw new ArgumentOutOfRangeException(nameof(firstVertex));
        if (vertexCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if (firstIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(firstIndex));
        if (indexCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(indexCount));

        Geometry = geometry;
        VertexLayout = vertexLayout;
        Topology = topology;
        FirstVertex = firstVertex;
        VertexCount = vertexCount;
        FirstIndex = firstIndex;
        IndexCount = indexCount;
        IndexFormat = indexFormat;
    }

    public RenderSemanticIdentity Geometry { get; }

    public RenderSemanticIdentity VertexLayout { get; }

    public RenderPrimitiveTopology Topology { get; }

    public int FirstVertex { get; }

    public int VertexCount { get; }

    public int FirstIndex { get; }

    public int IndexCount { get; }

    public RenderIndexFormat IndexFormat { get; }

    internal static void RequireKind(
        RenderSemanticIdentity identity,
        RenderSemanticResourceKind expected)
    {
        if (identity.Kind != expected || string.IsNullOrWhiteSpace(identity.Value))
        {
            throw new ArgumentException(
                $"Expected a valid {expected} identity.",
                nameof(identity));
        }
    }
}

public readonly record struct RenderInstanceSlice
{
    public RenderInstanceSlice(
        RenderSemanticIdentity instances,
        int firstInstance,
        int instanceCount)
    {
        RenderGeometrySlice.RequireKind(
            instances,
            RenderSemanticResourceKind.Instances);
        if (firstInstance < 0)
            throw new ArgumentOutOfRangeException(nameof(firstInstance));
        if (instanceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(instanceCount));

        Instances = instances;
        FirstInstance = firstInstance;
        InstanceCount = instanceCount;
    }

    public RenderSemanticIdentity Instances { get; }

    public int FirstInstance { get; }

    public int InstanceCount { get; }
}

public readonly record struct RenderDrawRange
{
    public RenderDrawRange(
        int firstIndex,
        int indexCount,
        int baseVertex,
        int firstInstance,
        int instanceCount)
    {
        if (firstIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(firstIndex));
        if (indexCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(indexCount));
        if (firstInstance < 0)
            throw new ArgumentOutOfRangeException(nameof(firstInstance));
        if (instanceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(instanceCount));

        FirstIndex = firstIndex;
        IndexCount = indexCount;
        BaseVertex = baseVertex;
        FirstInstance = firstInstance;
        InstanceCount = instanceCount;
    }

    public int FirstIndex { get; }

    public int IndexCount { get; }

    public int BaseVertex { get; }

    public int FirstInstance { get; }

    public int InstanceCount { get; }
}

public readonly record struct RenderDrawSortKey(
    long Primary,
    long Secondary,
    long SourceOrdinal);

public sealed class RenderShaderProgramDescriptor
{
    public RenderShaderProgramDescriptor(
        RenderSemanticIdentity identity,
        string vertexProgramIdentity,
        string fragmentProgramIdentity,
        RenderShaderAbiDescriptor abi,
        RenderAuthoredRsxProgramDescriptor? authoredRsxProgram = null)
    {
        RenderGeometrySlice.RequireKind(
            identity,
            RenderSemanticResourceKind.ShaderProgram);
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexProgramIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentProgramIdentity);
        ArgumentNullException.ThrowIfNull(abi);
        if (authoredRsxProgram is not null &&
            (!string.Equals(
                vertexProgramIdentity,
                authoredRsxProgram.VertexProgram.Identity,
                StringComparison.Ordinal) ||
             !string.Equals(
                 fragmentProgramIdentity,
                 authoredRsxProgram.FragmentProgram.Identity,
                 StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Authored RSX stage identities must exactly match the retained IR pair.",
                nameof(authoredRsxProgram));
        }

        Identity = identity;
        VertexProgramIdentity = vertexProgramIdentity;
        FragmentProgramIdentity = fragmentProgramIdentity;
        Abi = abi;
        AuthoredRsxProgram = authoredRsxProgram;
    }

    public RenderSemanticIdentity Identity { get; }

    public string VertexProgramIdentity { get; }

    public string FragmentProgramIdentity { get; }

    public RenderShaderAbiDescriptor Abi { get; }

    public RenderAuthoredRsxProgramDescriptor? AuthoredRsxProgram { get; }

    internal bool ContentEquals(RenderShaderProgramDescriptor? other) =>
        other is not null &&
        Identity == other.Identity &&
        string.Equals(
            VertexProgramIdentity,
            other.VertexProgramIdentity,
            StringComparison.Ordinal) &&
        string.Equals(
            FragmentProgramIdentity,
            other.FragmentProgramIdentity,
            StringComparison.Ordinal) &&
        Abi.ContentEquals(other.Abi) &&
        (AuthoredRsxProgram is null
            ? other.AuthoredRsxProgram is null
            : AuthoredRsxProgram.ContentEquals(other.AuthoredRsxProgram));
}

public sealed class RenderMaterialDescriptor
{
    public RenderMaterialDescriptor(
        RenderSemanticIdentity identity,
        string materialName,
        string techniqueName,
        string passClass,
        int passIndex)
    {
        RenderGeometrySlice.RequireKind(
            identity,
            RenderSemanticResourceKind.Material);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialName);
        ArgumentException.ThrowIfNullOrWhiteSpace(techniqueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passClass);
        if (passIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(passIndex));

        Identity = identity;
        MaterialName = materialName;
        TechniqueName = techniqueName;
        PassClass = passClass;
        PassIndex = passIndex;
    }

    public RenderSemanticIdentity Identity { get; }

    public string MaterialName { get; }

    public string TechniqueName { get; }

    public string PassClass { get; }

    public int PassIndex { get; }

    internal bool ContentEquals(RenderMaterialDescriptor? other) =>
        other is not null &&
        Identity == other.Identity &&
        string.Equals(MaterialName, other.MaterialName, StringComparison.Ordinal) &&
        string.Equals(TechniqueName, other.TechniqueName, StringComparison.Ordinal) &&
        string.Equals(PassClass, other.PassClass, StringComparison.Ordinal) &&
        PassIndex == other.PassIndex;
}

/// <summary>
/// Backend-neutral pipeline intent. FixedState names an immutable semantic
/// state descriptor; it is not a command or backend pipeline object.
/// </summary>
public sealed class RenderPipelineDescriptor
{
    public RenderPipelineDescriptor(
        RenderSemanticIdentity identity,
        RenderShaderProgramDescriptor shaderProgram,
        RenderSemanticIdentity vertexLayout,
        RenderFixedStateDescriptor fixedState,
        RenderPrimitiveTopology topology,
        ImmutableArray<RenderAttachmentPixelFormat> colorAttachmentFormats,
        RenderAttachmentPixelFormat? depthStencilAttachmentFormat,
        RenderMultisampleStateDescriptor multisample,
        RenderSemanticIdentity? instanceLayout = null)
    {
        RenderGeometrySlice.RequireKind(
            identity,
            RenderSemanticResourceKind.Pipeline);
        ArgumentNullException.ThrowIfNull(shaderProgram);
        RenderGeometrySlice.RequireKind(
            vertexLayout,
            RenderSemanticResourceKind.VertexLayout);
        if (instanceLayout is { } instanceLayoutIdentity)
        {
            RenderGeometrySlice.RequireKind(
                instanceLayoutIdentity,
                RenderSemanticResourceKind.InstanceLayout);
        }
        ArgumentNullException.ThrowIfNull(fixedState);
        if (!Enum.IsDefined(topology))
            throw new ArgumentOutOfRangeException(nameof(topology));
        if (colorAttachmentFormats.IsDefault)
            throw new ArgumentException(
                "Color attachment formats must be initialized.",
                nameof(colorAttachmentFormats));
        if (ContainsUndefinedFormat(colorAttachmentFormats))
            throw new ArgumentOutOfRangeException(nameof(colorAttachmentFormats));
        if (ContainsFormat(
                colorAttachmentFormats,
                RenderAttachmentPixelFormat.Depth24Stencil8))
        {
            throw new ArgumentException(
                "A depth-stencil format cannot occupy a color attachment slot.",
                nameof(colorAttachmentFormats));
        }
        if (depthStencilAttachmentFormat is { } depthFormat &&
            (!Enum.IsDefined(depthFormat) ||
             depthFormat != RenderAttachmentPixelFormat.Depth24Stencil8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(depthStencilAttachmentFormat));
        }
        if (multisample.SampleCount <= 0 || multisample.SampleMask == 0)
            throw new ArgumentException(
                "Multisample state must be initialized.",
                nameof(multisample));
        if (depthStencilAttachmentFormat is null &&
            (fixedState.Depth.TestEnabled ||
             fixedState.Depth.WriteEnabled ||
             fixedState.Stencil.Enabled))
        {
            throw new ArgumentException(
                "Depth or stencil state requires a depth-stencil attachment.",
                nameof(fixedState));
        }
        if (colorAttachmentFormats.IsEmpty &&
            (fixedState.Blend.Enabled ||
             fixedState.ColorWriteMask != RenderColorWriteMask.None))
        {
            throw new ArgumentException(
                "Color output state requires at least one color attachment.",
                nameof(fixedState));
        }
        if (ContainsFormat(
                colorAttachmentFormats,
                RenderAttachmentPixelFormat.R32UnsignedInteger) &&
            fixedState.Blend.Enabled)
        {
            throw new ArgumentException(
                "Integer color attachments cannot use blending.",
                nameof(fixedState));
        }

        Identity = identity;
        ShaderProgram = shaderProgram;
        VertexLayout = vertexLayout;
        InstanceLayout = instanceLayout;
        FixedState = fixedState;
        Topology = topology;
        ColorAttachmentFormats = colorAttachmentFormats;
        DepthStencilAttachmentFormat = depthStencilAttachmentFormat;
        Multisample = multisample;
    }

    public RenderSemanticIdentity Identity { get; }

    public RenderShaderProgramDescriptor ShaderProgram { get; }

    public RenderSemanticIdentity VertexLayout { get; }

    public RenderSemanticIdentity? InstanceLayout { get; }

    public RenderFixedStateDescriptor FixedState { get; }

    public RenderPrimitiveTopology Topology { get; }

    public ImmutableArray<RenderAttachmentPixelFormat>
        ColorAttachmentFormats { get; }

    public RenderAttachmentPixelFormat? DepthStencilAttachmentFormat { get; }

    public RenderMultisampleStateDescriptor Multisample { get; }

    public int SampleCount => Multisample.SampleCount;

    private static bool ContainsUndefinedFormat(
        ImmutableArray<RenderAttachmentPixelFormat> formats)
    {
        for (var index = 0; index < formats.Length; index++)
        {
            if (!Enum.IsDefined(formats[index]))
                return true;
        }

        return false;
    }

    private static bool ContainsFormat(
        ImmutableArray<RenderAttachmentPixelFormat> formats,
        RenderAttachmentPixelFormat expected)
    {
        for (var index = 0; index < formats.Length; index++)
        {
            if (formats[index] == expected)
                return true;
        }

        return false;
    }
}

public sealed class RenderTextureSamplerBinding
{
    public RenderTextureSamplerBinding(
        RenderShaderBindingPoint bindingPoint,
        RenderSemanticIdentity texture,
        RenderSemanticIdentity sampler,
        RenderTextureDimension textureDimension)
    {
        bindingPoint.Validate(nameof(bindingPoint));
        RenderGeometrySlice.RequireKind(
            texture,
            RenderSemanticResourceKind.Texture);
        RenderGeometrySlice.RequireKind(
            sampler,
            RenderSemanticResourceKind.Sampler);
        if (!Enum.IsDefined(textureDimension))
            throw new ArgumentOutOfRangeException(nameof(textureDimension));

        BindingPoint = bindingPoint;
        Texture = texture;
        Sampler = sampler;
        TextureDimension = textureDimension;
    }

    public RenderShaderBindingPoint BindingPoint { get; }

    public RenderSemanticIdentity Texture { get; }

    public RenderSemanticIdentity Sampler { get; }

    public RenderTextureDimension TextureDimension { get; }
}

public sealed class RenderDynamicConstantBinding
{
    public RenderDynamicConstantBinding(
        RenderSemanticIdentity identity,
        RenderShaderBindingPoint bindingPoint,
        RenderDynamicConstantEncoding encoding,
        RenderShaderCoordinateSpace coordinateSpace,
        ImmutableArray<Vector4> values)
    {
        bindingPoint.Validate(nameof(bindingPoint));
        RenderGeometrySlice.RequireKind(
            identity,
            RenderSemanticResourceKind.DynamicConstant);
        if (!Enum.IsDefined(encoding))
            throw new ArgumentOutOfRangeException(nameof(encoding));
        if (!Enum.IsDefined(coordinateSpace))
            throw new ArgumentOutOfRangeException(nameof(coordinateSpace));
        if (values.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A dynamic constant binding requires initialized values.",
                nameof(values));
        }
        if (values.Any(value =>
                !float.IsFinite(value.X) ||
                !float.IsFinite(value.Y) ||
                !float.IsFinite(value.Z) ||
                !float.IsFinite(value.W)))
        {
            throw new ArgumentOutOfRangeException(nameof(values));
        }
        if (encoding == RenderDynamicConstantEncoding.Matrix4x4Rows &&
            values.Length % 4 != 0)
        {
            throw new ArgumentException(
                "Matrix4x4 row constants require a whole number of four-row matrices.",
                nameof(values));
        }

        Identity = identity;
        BindingPoint = bindingPoint;
        Encoding = encoding;
        CoordinateSpace = coordinateSpace;
        Values = values;
    }

    public RenderSemanticIdentity Identity { get; }

    public RenderShaderBindingPoint BindingPoint { get; }

    public RenderDynamicConstantEncoding Encoding { get; }

    public RenderShaderCoordinateSpace CoordinateSpace { get; }

    public ImmutableArray<Vector4> Values { get; }
}

public sealed class RenderDrawPlan
{
    public RenderDrawPlan(
        RenderSemanticIdentity identity,
        RenderPipelineDescriptor pipeline,
        RenderMaterialDescriptor material,
        RenderGeometrySlice geometry,
        RenderInstanceSlice? instances,
        ImmutableArray<RenderTextureSamplerBinding> textures,
        ImmutableArray<RenderDynamicConstantBinding> dynamicConstants,
        RenderDrawRange range,
        RenderDrawSortKey sortKey,
        long? pickingIdentity,
        RenderPreviewDrawRequirement previewRequirement)
    {
        RenderGeometrySlice.RequireKind(
            identity,
            RenderSemanticResourceKind.Draw);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(material);
        if (textures.IsDefault || ContainsNull(textures))
        {
            throw new ArgumentException(
                "Texture bindings must be initialized and non-null.",
                nameof(textures));
        }
        if (dynamicConstants.IsDefault ||
            ContainsNull(dynamicConstants))
        {
            throw new ArgumentException(
                "Dynamic constants must be initialized and non-null.",
                nameof(dynamicConstants));
        }
        if (HasDuplicateTextureBindingPoint(textures))
        {
            throw new ArgumentException(
                "Texture binding points must be unique within one draw.",
                nameof(textures));
        }
        if (HasDuplicateDynamicConstantBindingPoint(dynamicConstants))
        {
            throw new ArgumentException(
                "Dynamic-constant binding points must be unique within one draw.",
                nameof(dynamicConstants));
        }
        if (HasOverlappingBindingPoint(textures, dynamicConstants))
        {
            throw new ArgumentException(
                "A shader binding point cannot be both texture and dynamic constants.");
        }
        ValidateShaderAbiBindings(
            pipeline.ShaderProgram.Abi,
            textures,
            dynamicConstants);
        if (pipeline.Topology != geometry.Topology ||
            pipeline.VertexLayout != geometry.VertexLayout)
        {
            throw new ArgumentException(
                "Draw geometry must match its pipeline topology and vertex layout.",
                nameof(geometry));
        }
        if (range.IndexCount <= 0 || range.InstanceCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(range),
                "A draw range must select positive index and instance counts.");
        }
        if (range.FirstIndex < geometry.FirstIndex ||
            checked(range.FirstIndex + range.IndexCount) >
                checked(geometry.FirstIndex + geometry.IndexCount))
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }
        if (instances is null &&
            (range.FirstInstance != 0 || range.InstanceCount != 1))
        {
            throw new ArgumentException(
                "A non-instanced draw must select exactly instance zero.",
                nameof(range));
        }
        if (instances.HasValue != pipeline.InstanceLayout.HasValue)
        {
            throw new ArgumentException(
                "A draw and its pipeline must agree on the presence of a reusable instance layout.",
                nameof(instances));
        }
        if (instances is { } instanceSlice &&
            (range.FirstInstance < instanceSlice.FirstInstance ||
             checked(range.FirstInstance + range.InstanceCount) >
                checked(instanceSlice.FirstInstance +
                        instanceSlice.InstanceCount)))
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }
        if (pickingIdentity is < 0)
            throw new ArgumentOutOfRangeException(nameof(pickingIdentity));
        if ((previewRequirement &
             ~(RenderPreviewDrawRequirement.VisibleInPreview |
               RenderPreviewDrawRequirement.EligibleForScreenshot |
               RenderPreviewDrawRequirement.EligibleForIsolation)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previewRequirement));
        }

        Identity = identity;
        Pipeline = pipeline;
        Material = material;
        Geometry = geometry;
        Instances = instances;
        Textures = textures;
        DynamicConstants = dynamicConstants;
        Range = range;
        SortKey = sortKey;
        PickingIdentity = pickingIdentity;
        PreviewRequirement = previewRequirement;
    }

    public RenderSemanticIdentity Identity { get; }

    public RenderPipelineDescriptor Pipeline { get; }

    public RenderMaterialDescriptor Material { get; }

    public RenderGeometrySlice Geometry { get; }

    public RenderInstanceSlice? Instances { get; }

    public ImmutableArray<RenderTextureSamplerBinding> Textures { get; }

    public ImmutableArray<RenderDynamicConstantBinding> DynamicConstants
        { get; }

    public RenderDrawRange Range { get; }

    public RenderDrawSortKey SortKey { get; }

    public long? PickingIdentity { get; }

    public RenderPreviewDrawRequirement PreviewRequirement { get; }

    private static void ValidateShaderAbiBindings(
        RenderShaderAbiDescriptor abi,
        ImmutableArray<RenderTextureSamplerBinding> textures,
        ImmutableArray<RenderDynamicConstantBinding> dynamicConstants)
    {
        if (abi.Requirements.Length !=
            checked(textures.Length + dynamicConstants.Length))
        {
            throw new ArgumentException(
                "Draw bindings do not exactly satisfy the shader ABI.");
        }

        foreach (RenderShaderBindingRequirement requirement in
                 abi.Requirements)
        {
            switch (requirement.Kind)
            {
                case RenderShaderBindingKind.SampledTexture:
                    RenderTextureSamplerBinding? texture =
                        FindTextureBinding(
                            textures,
                            requirement.BindingPoint);
                    if (texture is null ||
                        texture.TextureDimension !=
                            requirement.ExpectedTextureDimension)
                    {
                        throw new ArgumentException(
                            $"Draw does not satisfy sampled-texture ABI binding {requirement.BindingPoint}.");
                    }
                    break;
                case RenderShaderBindingKind.DynamicConstants:
                    RenderDynamicConstantBinding? constants =
                        FindDynamicConstantBinding(
                            dynamicConstants,
                            requirement.BindingPoint);
                    if (constants is null ||
                        constants.Encoding !=
                            requirement.ExpectedConstantEncoding ||
                        constants.CoordinateSpace !=
                            requirement.ExpectedCoordinateSpace ||
                        constants.Values.Length !=
                            requirement.ExpectedVectorCount)
                    {
                        throw new ArgumentException(
                            $"Draw does not satisfy dynamic-constant ABI binding {requirement.BindingPoint}.");
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(requirement.Kind));
            }
        }
    }

    private static bool ContainsNull<T>(ImmutableArray<T> values)
        where T : class
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] is null)
                return true;
        }

        return false;
    }

    private static bool HasDuplicateTextureBindingPoint(
        ImmutableArray<RenderTextureSamplerBinding> bindings)
    {
        for (var index = 0; index < bindings.Length; index++)
        {
            RenderShaderBindingPoint bindingPoint =
                bindings[index].BindingPoint;
            for (int candidate = index + 1;
                 candidate < bindings.Length;
                 candidate++)
            {
                if (bindings[candidate].BindingPoint == bindingPoint)
                    return true;
            }
        }

        return false;
    }

    private static bool HasDuplicateDynamicConstantBindingPoint(
        ImmutableArray<RenderDynamicConstantBinding> bindings)
    {
        for (var index = 0; index < bindings.Length; index++)
        {
            RenderShaderBindingPoint bindingPoint =
                bindings[index].BindingPoint;
            for (int candidate = index + 1;
                 candidate < bindings.Length;
                 candidate++)
            {
                if (bindings[candidate].BindingPoint == bindingPoint)
                    return true;
            }
        }

        return false;
    }

    private static bool HasOverlappingBindingPoint(
        ImmutableArray<RenderTextureSamplerBinding> textures,
        ImmutableArray<RenderDynamicConstantBinding> dynamicConstants)
    {
        for (var textureIndex = 0;
             textureIndex < textures.Length;
             textureIndex++)
        {
            RenderShaderBindingPoint bindingPoint =
                textures[textureIndex].BindingPoint;
            for (var constantIndex = 0;
                 constantIndex < dynamicConstants.Length;
                 constantIndex++)
            {
                if (dynamicConstants[constantIndex].BindingPoint ==
                    bindingPoint)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static RenderTextureSamplerBinding? FindTextureBinding(
        ImmutableArray<RenderTextureSamplerBinding> bindings,
        RenderShaderBindingPoint bindingPoint)
    {
        for (var index = 0; index < bindings.Length; index++)
        {
            if (bindings[index].BindingPoint == bindingPoint)
                return bindings[index];
        }

        return null;
    }

    private static RenderDynamicConstantBinding? FindDynamicConstantBinding(
        ImmutableArray<RenderDynamicConstantBinding> bindings,
        RenderShaderBindingPoint bindingPoint)
    {
        for (var index = 0; index < bindings.Length; index++)
        {
            if (bindings[index].BindingPoint == bindingPoint)
                return bindings[index];
        }

        return null;
    }
}
