using System.Collections.Immutable;
using System.Numerics;

using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.OpenGl.Diagnostics;

/// <summary>
/// One concrete OpenGL diagnostic submission. It retains the exact semantic
/// draw and exact scene-lifetime backend resource binding.
/// </summary>
internal sealed class MapRenderOpenGlNormalCameraDiagnosticDrawCommand
{
    internal MapRenderOpenGlNormalCameraDiagnosticDrawCommand(
        RenderDrawPlan semanticDraw,
        MapRenderOpenGlNormalCameraDiagnosticResourceBinding resource)
    {
        ArgumentNullException.ThrowIfNull(semanticDraw);
        ArgumentNullException.ThrowIfNull(resource);
        SemanticDraw = semanticDraw;
        Resource = resource;
    }

    public RenderDrawPlan SemanticDraw { get; }

    public MapRenderOpenGlNormalCameraDiagnosticResourceBinding Resource
        { get; }

    public bool IsInstanced => Resource.IsInstanced;

    public GlMesh Mesh => Resource.Mesh;

    public GlInstancedMesh InstancedMesh => Resource.InstancedMesh;
}

/// <summary>
/// Immutable lowering of the exact normal-camera diagnostics pass. The host
/// matrix is the already-established production value and is verified against
/// the PS3-native frame constant whenever the pass contains draws.
/// </summary>
internal sealed class MapRenderOpenGlNormalCameraDiagnosticPlan
{
    internal MapRenderOpenGlNormalCameraDiagnosticPlan(
        RenderFramePlan framePlan,
        RenderPassPlan diagnosticsPass,
        MapRenderOpenGlNormalCameraDiagnosticResourceCatalog resources,
        Matrix4x4 preparedHostViewProjection,
        ImmutableArray<MapRenderOpenGlNormalCameraDiagnosticDrawCommand>
            commands)
    {
        ArgumentNullException.ThrowIfNull(framePlan);
        ArgumentNullException.ThrowIfNull(diagnosticsPass);
        ArgumentNullException.ThrowIfNull(resources);
        if (!IsFinite(preparedHostViewProjection))
            throw new ArgumentOutOfRangeException(
                nameof(preparedHostViewProjection));
        if (commands.IsDefault || commands.Any(command => command is null))
        {
            throw new ArgumentException(
                "OpenGL diagnostic commands must be initialized and non-null.",
                nameof(commands));
        }
        if (!framePlan.Passes.Any(pass =>
                ReferenceEquals(pass, diagnosticsPass)))
        {
            throw new ArgumentException(
                "The OpenGL diagnostics pass must be the exact pass owned by its frame plan.",
                nameof(diagnosticsPass));
        }
        if (commands.Length != diagnosticsPass.Draws.Length)
        {
            throw new ArgumentException(
                "OpenGL diagnostic command count must exactly match semantic draw count.",
                nameof(commands));
        }
        for (var index = 0; index < commands.Length; index++)
        {
            if (!ReferenceEquals(
                    commands[index].SemanticDraw,
                    diagnosticsPass.Draws[index]))
            {
                throw new ArgumentException(
                    "Every OpenGL diagnostic command must retain its exact semantic draw object and order.",
                    nameof(commands));
            }
        }

        FramePlan = framePlan;
        DiagnosticsPass = diagnosticsPass;
        Resources = resources;
        PreparedHostViewProjection = preparedHostViewProjection;
        Commands = commands;
    }

    public RenderFramePlan FramePlan { get; }

    public RenderPassPlan DiagnosticsPass { get; }

    public MapRenderOpenGlNormalCameraDiagnosticResourceCatalog Resources
        { get; }

    public Matrix4x4 PreparedHostViewProjection { get; }

    public ImmutableArray<MapRenderOpenGlNormalCameraDiagnosticDrawCommand>
        Commands { get; }

    private static bool IsFinite(Matrix4x4 matrix) =>
        float.IsFinite(matrix.M11) &&
        float.IsFinite(matrix.M12) &&
        float.IsFinite(matrix.M13) &&
        float.IsFinite(matrix.M14) &&
        float.IsFinite(matrix.M21) &&
        float.IsFinite(matrix.M22) &&
        float.IsFinite(matrix.M23) &&
        float.IsFinite(matrix.M24) &&
        float.IsFinite(matrix.M31) &&
        float.IsFinite(matrix.M32) &&
        float.IsFinite(matrix.M33) &&
        float.IsFinite(matrix.M34) &&
        float.IsFinite(matrix.M41) &&
        float.IsFinite(matrix.M42) &&
        float.IsFinite(matrix.M43) &&
        float.IsFinite(matrix.M44);
}
