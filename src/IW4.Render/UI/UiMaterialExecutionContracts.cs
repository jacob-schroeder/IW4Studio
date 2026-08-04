using IW4.Assets.Zone;
using IW4.Render.Execution;
using IW4.Render.Materials;
using IW4.Render.Textures;

namespace IW4.Render.UI;

public enum UiMaterialColorOperation
{
    TextureMultiplyVertexColor = 0
}

public enum UiMaterialUvAuthority
{
    FullTexture = 0,
    EvaluatedAtlasFrame = 1
}

public enum UiMaterialAtlasStatus
{
    NotAuthored = 0,
    SingleCellFullTexture = 1,
    EvaluatedUvs = 2,
    EvaluationRequired = 3,
    InvalidDimensions = 4
}

public readonly record struct UiMaterialAtlasState(
    byte AuthoredRowCount,
    byte AuthoredColumnCount,
    UiMaterialAtlasStatus Status)
{
    public bool IsAuthored =>
        AuthoredRowCount > 0 && AuthoredColumnCount > 0;
}

/// <summary>
/// Resource-table key supplied by the host after resolving the active
/// canonical image provider. Image bytes remain in the host resource table;
/// only descriptor bytes needed for exact sampler decoding cross this
/// boundary.
/// </summary>
public sealed record UiMaterialTextureResource(
    string ResourceKey,
    string ImageName,
    long CanonicalPoolRevision,
    XAssetPoolAddress CanonicalImageSlot,
    int Width,
    int Height,
    int Depth,
    byte MapType,
    byte DimensionCount,
    byte MultiFaceControl,
    byte DescriptorPad0F,
    byte DescriptorPad1B);

public sealed record UiMaterialTextureBinding(
    string ResourceKey,
    string ImageName,
    long CanonicalPoolRevision,
    XAssetPoolAddress CanonicalImageSlot,
    int TextureTableOrdinal,
    uint NameHash,
    byte TextureSemantic,
    byte AuthoredSamplerState,
    MapRenderSamplerState SamplerState);

public sealed record UiMaterialPassIdentity(
    string MaterialName,
    long CanonicalPoolRevision,
    XAssetPoolAddress CanonicalMaterialSlot,
    string TechniqueSetName,
    int TechniqueSlot,
    string TechniqueName,
    int PassIndex,
    string VertexProgramName,
    string PixelProgramName);

public abstract class UiDrawPacket
{
    protected UiDrawPacket(long drawOrder)
    {
        if (drawOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(drawOrder));
        DrawOrder = drawOrder;
    }

    public long DrawOrder { get; }
}

/// <summary>
/// Complete renderer-neutral execution packet for the PS3-proven
/// 2d/slot-4/trivial_vertcol_simple2d path. The texture payload is referenced
/// by key so presentation backends can own upload and lifetime policy.
/// </summary>
public sealed class UiMaterialDrawPacket : UiDrawPacket
{
    private readonly UiMaterialExecutionDiagnostic[] _diagnostics;

    internal UiMaterialDrawPacket(
        long drawOrder,
        UiMaterialQuad quad,
        UiMaterialPassIdentity identity,
        UiMaterialTextureBinding texture,
        UiMaterialAtlasState atlas,
        MapRenderState state,
        MapRenderShaderExecutionContract shaderExecution,
        IReadOnlyList<UiMaterialExecutionDiagnostic> diagnostics)
        : base(drawOrder)
    {
        Quad = quad ?? throw new ArgumentNullException(nameof(quad));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Texture = texture ?? throw new ArgumentNullException(nameof(texture));
        if (string.IsNullOrWhiteSpace(texture.ResourceKey))
        {
            throw new ArgumentException(
                "A UI material packet requires a texture resource key.",
                nameof(texture));
        }
        Atlas = atlas;
        State = state;
        ShaderExecution = shaderExecution ??
            throw new ArgumentNullException(nameof(shaderExecution));
        if (!ShaderExecution.ProgramExecutionReady)
        {
            throw new ArgumentException(
                "A UI material packet requires an executable translated " +
                "shader contract.",
                nameof(shaderExecution));
        }
        ArgumentNullException.ThrowIfNull(diagnostics);
        _diagnostics = diagnostics.ToArray();
        if (_diagnostics.Any(diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "A UI material packet cannot contain null diagnostics.",
                nameof(diagnostics));
        }
        if (_diagnostics.Any(diagnostic =>
                diagnostic.Severity ==
                UiMaterialExecutionDiagnosticSeverity.Blocker))
        {
            throw new ArgumentException(
                "An executable UI material packet cannot retain blockers.",
                nameof(diagnostics));
        }

        Diagnostics = Array.AsReadOnly(_diagnostics);
    }

    public UiMaterialQuad Quad { get; }

    public UiMaterialPassIdentity Identity { get; }

    public UiMaterialTextureBinding Texture { get; }

    public UiMaterialAtlasState Atlas { get; }

    public MapRenderState State { get; }

    public MapRenderShaderExecutionContract ShaderExecution { get; }

    public IReadOnlyList<UiMaterialExecutionDiagnostic> Diagnostics { get; }

    public UiMaterialColorOperation ColorOperation { get; } =
        UiMaterialColorOperation.TextureMultiplyVertexColor;
}

public sealed class UiRenderScene
{
    private readonly UiDrawPacket[] _drawPackets;

    public UiRenderScene(
        int viewportWidth,
        int viewportHeight,
        IReadOnlyList<UiDrawPacket> drawPackets)
    {
        if (viewportWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (viewportHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        ArgumentNullException.ThrowIfNull(drawPackets);

        _drawPackets = drawPackets.ToArray();
        if (_drawPackets.Any(packet => packet is null))
        {
            throw new ArgumentException(
                "A UI render scene cannot contain null draw packets.",
                nameof(drawPackets));
        }
        for (int index = 1; index < _drawPackets.Length; index++)
        {
            if (_drawPackets[index - 1].DrawOrder >=
                _drawPackets[index].DrawOrder)
            {
                throw new ArgumentException(
                    "UI draw packets must be supplied in strictly increasing draw order.",
                    nameof(drawPackets));
            }
        }

        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        DrawPackets = Array.AsReadOnly(_drawPackets);
    }

    public int ViewportWidth { get; }

    public int ViewportHeight { get; }

    public IReadOnlyList<UiDrawPacket> DrawPackets { get; }
}

public sealed record UiMaterialDrawRequest(
    long DrawOrder,
    string MaterialName,
    long CanonicalPoolRevision,
    UiMaterialQuad Quad,
    UiMaterialUvAuthority UvAuthority = UiMaterialUvAuthority.FullTexture);
