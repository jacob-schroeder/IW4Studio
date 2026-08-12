using System.Numerics;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Lighting;
using IW4.Render.Materials;
using IW4.Render.OpenGl.Programs;
using IW4.Render.SceneBuilding;
using IW4.Render.Shaders;
using IW4.Render.Textures;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.XModel;

/// <summary>
/// Immutable result of atomically preparing the authored material groups for
/// one XModel LOD. A blocked group is never partially submitted.
/// </summary>
public sealed class XModelViewerUploadResult
{
    internal XModelViewerUploadResult(
        int executableGroupCount,
        int blockedGroupCount,
        IReadOnlyList<string> diagnostics)
    {
        if (executableGroupCount < 0)
            throw new ArgumentOutOfRangeException(nameof(executableGroupCount));
        if (blockedGroupCount < 0)
            throw new ArgumentOutOfRangeException(nameof(blockedGroupCount));
        ArgumentNullException.ThrowIfNull(diagnostics);

        ExecutableGroupCount = executableGroupCount;
        BlockedGroupCount = blockedGroupCount;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public int ExecutableGroupCount { get; }

    public int BlockedGroupCount { get; }

    public IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>
/// Retained OpenGL presentation for one backend-neutral XModel projection.
/// The caller owns the current context and invokes this object only while
/// that context is current.
/// </summary>
public sealed unsafe class SilkXModelViewerRenderer : IDisposable
{
    private const int TextureUnitCount = 16;
    private const int NeutralDynamicLightingEntry =
        MapRenderStaticModelLightingAtlas.StaticEntryCapacity;

    private readonly GL _gl;
    private readonly SilkOpenGlStateShadow _state;
    private readonly SilkOpenGlTextureParameters _textureParameters;
    private readonly MapRenderOpenGlSharedProgramCache _sharedPrograms;
    private readonly MapRenderOpenGlSharedProgramCache.UsageLease
        _sharedProgramUsage;
    private readonly HashSet<uint> _viewerOwnedProgramHandles = [];
    private readonly Dictionary<
        MapRenderOpenGlProgramKey,
        MapRenderOpenGlLinkedProgramHandleResolution>
        _viewerProgramResolutions = [];
    private readonly SilkOpenGlAuthoredMaterialExecutor _authoredMaterials;
    private readonly Dictionary<MapRenderTexture, uint> _textureHandles =
        new(ReferenceEqualityComparer.Instance);
    private readonly uint _wireframeProgram;
    private readonly int _wireframeViewProjectionLocation;
    private readonly List<AuthoredDrawGroup> _drawGroups = [];
    private WireframeGeometry _wireframe;
    private uint _neutralModelLightingAtlas;
    private uint _neutralReflectionCube;
    private bool _disposed;

    public SilkXModelViewerRenderer(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _state = new SilkOpenGlStateShadow(gl);
        _textureParameters = new SilkOpenGlTextureParameters(gl);
        _sharedPrograms = new MapRenderOpenGlSharedProgramCache(gl);
        _sharedProgramUsage = _sharedPrograms.AcquireUsageLease();
        try
        {
            _authoredMaterials = new SilkOpenGlAuthoredMaterialExecutor(
                gl,
                _state,
                ResolveLinkedProgram);
            _wireframeProgram = LinkProgram(
                WireframeVertexShaderSource,
                WireframeFragmentShaderSource);
            _wireframeViewProjectionLocation =
                RequireUniform(_wireframeProgram, "uViewProjection");
        }
        catch
        {
            if (_wireframeProgram != 0)
                _gl.DeleteProgram(_wireframeProgram);
            _sharedProgramUsage.Dispose();
            _sharedPrograms.Dispose();
            throw;
        }
    }

    public XModelViewerUploadResult Upload(XModelRenderLod? lod)
    {
        ThrowIfDisposed();
        DeleteUploadedResources();
        if (lod is null || lod.Surfaces.Count == 0)
            return new XModelViewerUploadResult(0, 0, []);

        var diagnostics = new List<string>();
        int executableGroups = 0;
        int blockedGroups = 0;
        try
        {
            _wireframe = CreateWireframeGeometry(lod.Surfaces);
            foreach (XModelRenderSurface surface in lod.Surfaces)
            {
                string identity =
                    $"surface{surface.GeometrySurfaceIndex}:{surface.MaterialName}";
                if (!surface.AuthoredGroupReady ||
                    surface.AuthoredPasses.Count == 0)
                {
                    blockedGroups++;
                    diagnostics.Add(
                        $"{identity}: {surface.AuthoredMaterialStatus}");
                    continue;
                }

                XModelRenderAuthoredPass[] packets = surface.AuthoredPasses
                    .OrderBy(packet => packet.GroupPassIndex)
                    .ToArray();
                if (!TryPrepareGroup(
                        surface,
                        packets,
                        out PreparedPass[] prepared,
                        out string? blocker))
                {
                    blockedGroups++;
                    diagnostics.Add(
                        $"{identity}: authoredGroup=blocked:{blocker}");
                    continue;
                }

                var draws = new List<AuthoredDraw>(prepared.Length);
                try
                {
                    foreach (PreparedPass pass in prepared)
                        draws.Add(CreateAuthoredDraw(surface, pass));
                }
                catch
                {
                    foreach (AuthoredDraw draw in draws)
                        DeleteAuthoredDraw(draw);
                    throw;
                }

                _drawGroups.Add(new AuthoredDrawGroup(
                    packets[0].GroupId,
                    draws.ToArray()));
                string[] viewerInputs = prepared
                    .SelectMany(pass => pass.MaterialSamplers)
                    .Where(binding => string.Equals(
                        binding.ExternalResourceIdentity,
                        AuthoredMaterialSamplerResolver
                            .XModelViewerReflectionProbeResourceIdentity,
                        StringComparison.Ordinal))
                    .Any()
                        ? ["neutralReflectionCube"]
                        : [];
                if (prepared.Any(pass =>
                        pass.RuntimeSamplerRequirements.Length != 0))
                {
                    viewerInputs = viewerInputs
                        .Append("nativeNeutralModelLightingEntry7168")
                        .ToArray();
                }
                if (viewerInputs.Length != 0)
                {
                    diagnostics.Add(
                        $"{identity}: viewerRuntimeInputs=" +
                        string.Join(',', viewerInputs));
                }
                executableGroups++;
            }
        }
        catch
        {
            DeleteUploadedResources();
            throw;
        }

        return new XModelViewerUploadResult(
            executableGroups,
            blockedGroups,
            diagnostics);
    }

    /// <summary>
    /// Creates the host-space projection used by both authored XModel draws
    /// and inspection overlays in the direct OpenGL preview framebuffer.
    /// </summary>
    public static Matrix4x4 CreateHostViewProjection(
        MapRenderCamera camera,
        float aspectRatio) =>
        MapRenderOpenGlRsxClipSpaceLowering
            .CreateDirectEditorPreviewHostViewProjection(
                MapRenderOpenGlDerivedMatrixPolicy.CreatePreviewFromCamera(
                    camera,
                    aspectRatio));

    public void Render(
        int framebuffer,
        int width,
        int height,
        MapRenderCamera camera,
        float materialTimeSeconds,
        bool showWireframe)
    {
        ThrowIfDisposed();
        if (width <= 0 || height <= 0)
            return;
        if (!float.IsFinite(materialTimeSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(materialTimeSeconds),
                "Material time must be finite.");
        }

        float aspect = width / (float)height;
        MapRenderDerivedMatrixState matrices =
            MapRenderOpenGlDerivedMatrixPolicy.CreatePreviewFromCamera(
                camera,
                aspect);
        EstablishFrameState(framebuffer, width, height);
        if (showWireframe)
        {
            DrawWireframe(CreateHostViewProjection(camera, aspect));
            return;
        }

        foreach (AuthoredDrawGroup group in _drawGroups)
        {
            foreach (AuthoredDraw draw in group.Draws)
            {
                if (!_authoredMaterials.TryApplyRenderState(
                        draw.State,
                        out string? stateBlocker))
                {
                    throw new InvalidOperationException(
                        $"Preflighted XModel authored group {group.GroupId} lost its render-state contract: {stateBlocker}");
                }
                _state.UseProgram(draw.Program.Handle);
                if (!_authoredMaterials.TryApplyConstantBindings(
                        draw.ConstantBindings,
                        matrices,
                        materialTimeSeconds,
                        static (_, _) => null,
                        out string? constantBlocker))
                {
                    throw new InvalidOperationException(
                        $"Preflighted XModel authored group {group.GroupId} lost its constant contract: {constantBlocker}");
                }
                _authoredMaterials.BindMaterialSamplers(
                    draw.MaterialSamplerBindings);
                BindRuntimeSamplers(draw.RuntimeSamplerRequirements);
                _state.BindVertexArray(draw.VertexArray);
                _gl.DrawElements(
                    PrimitiveType.Triangles,
                    draw.IndexCount,
                    DrawElementsType.UnsignedInt,
                    null);
            }
        }

        _state.ActiveTexture(0);
        _state.BindVertexArray(0);
        _state.UseProgram(0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DeleteUploadedResources();
        _authoredMaterials.Clear();
        DeleteViewerOwnedPrograms();
        _gl.DeleteProgram(_wireframeProgram);
        _sharedProgramUsage.Dispose();
        _sharedPrograms.Dispose();
        _disposed = true;
    }

    private bool TryPrepareGroup(
        XModelRenderSurface surface,
        IReadOnlyList<XModelRenderAuthoredPass> packets,
        out PreparedPass[] prepared,
        out string? blocker)
    {
        prepared = [];
        blocker = null;
        int groupId = packets[0].GroupId;
        var passes = new List<PreparedPass>(packets.Count);
        for (int index = 0; index < packets.Count; index++)
        {
            XModelRenderAuthoredPass packet = packets[index];
            if (packet.GroupId != groupId ||
                packet.GroupPassIndex != index)
            {
                blocker = "PASS_ORDER_OR_GROUP_ID_INVALID";
                return false;
            }
            if (!TryPreparePass(
                    surface,
                    packet,
                    out PreparedPass? pass,
                    out string? passBlocker))
            {
                blocker =
                    $"pass{packet.GroupPassIndex}:{passBlocker}";
                return false;
            }
            passes.Add(pass!);
        }

        prepared = passes.ToArray();
        return true;
    }

    private bool TryPreparePass(
        XModelRenderSurface surface,
        XModelRenderAuthoredPass packet,
        out PreparedPass? prepared,
        out string? blocker)
    {
        prepared = null;
        blocker = null;
        MapRenderShaderExecutionContract execution = packet.ShaderExecution;
        if (!execution.RendererProgramReady)
        {
            blocker = execution.RendererBlockers.Count == 0
                ? "RENDERER_PROGRAM_NOT_READY"
                : string.Join('|', execution.RendererBlockers);
            return false;
        }
        if (!execution.VertexInputPayloadReady)
        {
            blocker = "RSX_VERTEX_INPUT_PAYLOAD_NOT_READY";
            return false;
        }
        int expectedInputCount = checked(surface.Positions.Count *
            MapRenderOpenGlPackedRsxVertexLayout.SourceFloatStride);
        if (packet.RsxVertexInputs.Length != expectedInputCount)
        {
            blocker =
                $"RSX_VERTEX_INPUT_SIZE_EXPECTED_{expectedInputCount}_ACTUAL_{packet.RsxVertexInputs.Length}";
            return false;
        }
        if (surface.Indices.Count == 0 ||
            surface.Indices.Any(value => value >= surface.Positions.Count))
        {
            blocker = "SURFACE_INDEX_RANGE_INVALID";
            return false;
        }
        if (execution.Purpose !=
            MapRenderShaderExecutionPurpose.CameraColor)
        {
            blocker = $"EXECUTION_PURPOSE_{execution.Purpose}_UNSUPPORTED";
            return false;
        }
        if (execution.FragmentDepthExportEnabled)
        {
            blocker = "FRAGMENT_DEPTH_EXPORT_REQUIRES_OWNED_DEPTH_EXPORT_PATH";
            return false;
        }
        int[] programSamplerDestinations = execution
            .ProgramSamplerDestinations
            .Distinct()
            .Order()
            .ToArray();
        HashSet<int> programSamplerDestinationSet =
            programSamplerDestinations.ToHashSet();
        MapRenderShaderRuntimeSamplerRequirement[] runtimeSamplerRequirements =
            execution.RuntimeSamplerRequirements
                .Where(requirement => programSamplerDestinationSet.Contains(
                    requirement.Destination))
                .ToArray();
        if (!TryValidateRuntimeSamplerRequirements(
                runtimeSamplerRequirements,
                out blocker))
        {
            return false;
        }
        if (!TryValidateMaterialSamplers(
                execution,
                packet.MaterialSamplers,
                programSamplerDestinations,
                runtimeSamplerRequirements,
                out MapRenderMaterialSamplerBinding[] materialSamplers,
                out blocker))
        {
            return false;
        }

        MapRenderEditorTranslatedProgramDirectCodeConstantPlanBuildResult
            directResult =
                MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
                    .TryPlan(
                        execution.ConstantDestinations,
                        execution.CodePixelConstantPatchPlans,
                        fogRenderingEnabled: false,
                        activeFog: null,
                        directionalSun: null);
        if (!directResult.IsReady)
        {
            blocker = "DIRECT_CONSTANTS:" +
                string.Join('|', directResult.Blockers);
            return false;
        }
        MapRenderEditorTranslatedProgramDirectCodeConstantPlan directPlan =
            directResult.Plan!;
        ushort[] unsupportedDynamicRows = directPlan.DynamicSourceRows
            .Where(row => row is not
                (FrameDirectCodeConstants.GameTimeRowIndex or
                 FrameDirectCodeConstants
                     .StaticModelBaseLightingCoordsRowIndex))
            .ToArray();
        if (unsupportedDynamicRows.Length != 0)
        {
            blocker = "RUNTIME_DIRECT_ROWS_UNAVAILABLE:" +
                string.Join(',', unsupportedDynamicRows.Select(row =>
                    $"0x{row:X2}"));
            return false;
        }

        MapRenderEditorTranslatedProgramVertexConstantBindingPlanBuildResult
            vertexResult =
                MapRenderEditorTranslatedProgramVertexConstantBindingPlanner
                    .TryPlan(
                        execution.ProgramVertexConstantDestinations,
                        execution.ConstantDestinations,
                        execution.EmbeddedVertexConstants,
                        directPlan);
        if (!vertexResult.IsReady)
        {
            blocker = "VERTEX_CONSTANTS:" +
                string.Join('|', vertexResult.Blockers);
            return false;
        }
        if (vertexResult.Plan!.Bindings.Any(binding => binding.Kind ==
            MapRenderEditorTranslatedProgramVertexConstantBindingKind
                .PerInstanceStaticModelLightProbeAmbient))
        {
            blocker =
                "MODEL_LIGHTING_LIGHT_PROBE_AMBIENT_ROW_0x3A_UNAVAILABLE";
            return false;
        }
        ushort[] unsupportedDynamicPixelRows = execution
            .CodePixelConstantPatchPlans
            .Where(patch =>
                directPlan.IsDynamicSourceRow(patch.CodeIndex) &&
                patch.CodeIndex !=
                    FrameDirectCodeConstants.GameTimeRowIndex)
            .Select(patch => patch.CodeIndex)
            .Distinct()
            .Order()
            .ToArray();
        if (unsupportedDynamicPixelRows.Length != 0)
        {
            blocker = "PIXEL_DYNAMIC_DIRECT_ROWS_UNAVAILABLE:" +
                string.Join(',', unsupportedDynamicPixelRows.Select(row =>
                    $"0x{row:X2}"));
            return false;
        }

        GlRsxProgram program = _authoredMaterials.GetOrCreateProgram(
            execution,
            packet.State,
            staticModelVertexConstantPlan: null,
            useVertexPlacementDiagnostic: false,
            fragmentOutputDiagnostic: null,
            out string? programBlocker);
        if (program.Handle == 0)
        {
            blocker = "OPENGL_PROGRAM:" +
                (programBlocker ?? "LINK_OR_LOWERING_FAILED");
            return false;
        }
        if (!_authoredMaterials.TryCreateConstantBindings(
                execution,
                program,
                directPlan,
                vertexResult.Plan,
                out GlRsxConstantBinding[] constantBindings,
                out string? constantBlocker,
                MapRenderStaticModelLightingAtlas.EntryCoordinates(
                    NeutralDynamicLightingEntry)))
        {
            blocker = "OPENGL_CONSTANT_BINDINGS:" + constantBlocker;
            return false;
        }

        MapRenderOpenGlPackedRsxVertexLayout layout;
        try
        {
            layout = new MapRenderOpenGlPackedRsxVertexLayout(
                MapRenderOpenGlPackedRsxVertexLayout.ResolveAttributeMask(
                    execution));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                ArgumentOutOfRangeException)
        {
            blocker = $"OPENGL_VERTEX_LAYOUT:{exception.Message}";
            return false;
        }

        prepared = new PreparedPass(
            packet,
            layout,
            program,
            constantBindings,
            materialSamplers,
            runtimeSamplerRequirements);
        return true;
    }

    private static bool TryValidateRuntimeSamplerRequirements(
        IReadOnlyList<MapRenderShaderRuntimeSamplerRequirement> requirements,
        out string? blocker)
    {
        blocker = null;
        foreach (MapRenderShaderRuntimeSamplerRequirement requirement in
                 requirements)
        {
            if (requirement.Destination >= TextureUnitCount)
            {
                blocker =
                    $"RUNTIME_SAMPLER_DEST_{requirement.Destination}_OUT_OF_RANGE";
                return false;
            }
            if (requirement.ResourceKind !=
                    MapRenderShaderRuntimeSamplerResourceKind
                        .StaticModelLightingAtlas ||
                requirement.Status !=
                    MapRenderShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneAtlasRequired ||
                requirement.CodeSamplerArgument !=
                    (uint)MapRenderCodePixelSamplerSource.ModelLighting ||
                !string.Equals(
                    requirement.ResourceIdentity,
                    "modelLightingSampler",
                    StringComparison.Ordinal))
            {
                blocker =
                    $"RUNTIME_SAMPLER_{requirement.ResourceIdentity}@{requirement.Destination}_{requirement.ResourceKind}_{requirement.Status}_UNAVAILABLE";
                return false;
            }
        }
        return true;
    }

    private static bool TryValidateMaterialSamplers(
        MapRenderShaderExecutionContract execution,
        IReadOnlyList<MapRenderMaterialSamplerBinding> bindings,
        IReadOnlyList<int> programSamplerDestinations,
        IReadOnlyList<MapRenderShaderRuntimeSamplerRequirement>
            runtimeSamplerRequirements,
        out MapRenderMaterialSamplerBinding[] selected,
        out string? blocker)
    {
        selected = [];
        blocker = null;
        HashSet<int> programSamplerDestinationSet =
            programSamplerDestinations.ToHashSet();
        ushort[] destinations = execution.MaterialSamplerDestinations
            .Concat(execution.CustomSamplerDestinations)
            .Where(value => programSamplerDestinationSet.Contains(
                value.Destination))
            .Select(value => value.Destination)
            .Distinct()
            .Order()
            .ToArray();
        var result = new List<MapRenderMaterialSamplerBinding>(
            destinations.Length);
        foreach (ushort destination in destinations)
        {
            if (destination >= TextureUnitCount)
            {
                blocker =
                    $"MATERIAL_SAMPLER_DEST_{destination}_OUT_OF_RANGE";
                return false;
            }
            MapRenderMaterialSamplerBinding[] candidates = bindings
                .Where(binding => binding.SamplerDest == destination)
                .ToArray();
            if (candidates.Length != 1)
            {
                blocker =
                    $"MATERIAL_SAMPLER_DEST_{destination}_OWNER_COUNT_{candidates.Length}";
                return false;
            }
            MapRenderMaterialSamplerBinding candidate = candidates[0];
            string[] expectedTargets = execution.MaterialSamplerDestinations
                .Concat(execution.CustomSamplerDestinations)
                .Where(binding => binding.Destination == destination)
                .Select(binding => binding.TextureTarget)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (expectedTargets.Length != 1)
            {
                blocker =
                    $"MATERIAL_SAMPLER_DEST_{destination}_TARGET_COUNT_{expectedTargets.Length}";
                return false;
            }
            string expectedTarget = expectedTargets[0];
            if (candidate.Texture is not { } texture)
            {
                if (string.Equals(
                        candidate.ExternalResourceIdentity,
                        AuthoredMaterialSamplerResolver
                            .XModelViewerReflectionProbeResourceIdentity,
                        StringComparison.Ordinal) &&
                    candidate.SamplerArgIndex < 0 &&
                    destination == 1 &&
                    string.Equals(
                        expectedTarget,
                        "TextureCube",
                        StringComparison.Ordinal))
                {
                    result.Add(candidate);
                    continue;
                }

                blocker =
                    $"MATERIAL_SAMPLER_DEST_{destination}_{candidate.TextureName}_PAYLOAD_MISSING_OR_EXTERNAL_{candidate.ExternalResourceIdentity ?? "MISSING"}_UNSUPPORTED";
                return false;
            }
            if (!CanUploadTexture(texture, out string textureBlocker))
            {
                blocker =
                    $"MATERIAL_SAMPLER_DEST_{destination}_{texture.Name}_{textureBlocker}";
                return false;
            }
            string actualTarget = texture.Target switch
            {
                MapRenderTextureTarget.Texture2D => "Texture2D",
                MapRenderTextureTarget.TextureCube => "TextureCube",
                _ => texture.Target.ToString()
            };
            if (!string.Equals(
                    expectedTarget,
                    actualTarget,
                    StringComparison.Ordinal))
            {
                blocker =
                    $"MATERIAL_SAMPLER_DEST_{destination}_TARGET_EXPECTED_{expectedTarget}_ACTUAL_{actualTarget}";
                return false;
            }
            result.Add(candidate);
        }
        foreach (int destination in programSamplerDestinations)
        {
            if (destination is < 0 or >= TextureUnitCount)
            {
                blocker =
                    $"PROGRAM_SAMPLER_DEST_{destination}_OUT_OF_RANGE";
                return false;
            }
            int ownerCount = result.Count(binding =>
                    binding.SamplerDest == destination) +
                runtimeSamplerRequirements.Count(requirement =>
                    requirement.Destination == destination);
            if (ownerCount != 1)
            {
                blocker =
                    $"PROGRAM_SAMPLER_DEST_{destination}_OWNER_COUNT_{ownerCount}";
                return false;
            }
        }
        selected = result.ToArray();
        return true;
    }

    private AuthoredDraw CreateAuthoredDraw(
        XModelRenderSurface surface,
        PreparedPass prepared)
    {
        float[] packed = new float[
            prepared.Layout.PackedFloatCount(
                prepared.Packet.RsxVertexInputs.Length)];
        prepared.Layout.Pack(
            prepared.Packet.RsxVertexInputs,
            packed);
        uint vertexArray = 0;
        uint vertexBuffer = 0;
        uint indexBuffer = 0;
        try
        {
            vertexArray = _gl.GenVertexArray();
            vertexBuffer = _gl.GenBuffer();
            indexBuffer = _gl.GenBuffer();
            _gl.BindVertexArray(vertexArray);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexBuffer);
            fixed (float* vertexPointer = packed)
            {
                _gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    checked((nuint)(packed.Length * sizeof(float))),
                    vertexPointer,
                    BufferUsageARB.StaticDraw);
            }
            _gl.BindBuffer(
                BufferTargetARB.ElementArrayBuffer,
                indexBuffer);
            uint[] indices = surface.Indices.ToArray();
            fixed (uint* indexPointer = indices)
            {
                _gl.BufferData(
                    BufferTargetARB.ElementArrayBuffer,
                    checked((nuint)(indices.Length * sizeof(uint))),
                    indexPointer,
                    BufferUsageARB.StaticDraw);
            }
            ConfigureRsxVertexAttributes(prepared.Layout);
            _gl.BindVertexArray(0);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

            GlRsxSamplerBinding[] materialSamplerBindings = prepared
                .MaterialSamplers
                .Select(binding => new GlRsxSamplerBinding(
                    binding.SamplerDest,
                    binding.Texture is { } texture
                        ? GetOrCreateTexture(texture)
                        : GetOrCreateNeutralReflectionCube(),
                    binding.Texture is { } source
                        ? ToGlTextureTarget(source.Target)
                        : TextureTarget.TextureCubeMap))
                .ToArray();
            if (prepared.RuntimeSamplerRequirements.Length > 0 &&
                _neutralModelLightingAtlas == 0)
            {
                _neutralModelLightingAtlas =
                    CreateNeutralModelLightingAtlas();
            }

            return new AuthoredDraw(
                vertexArray,
                vertexBuffer,
                indexBuffer,
                checked((uint)surface.Indices.Count),
                prepared.Packet.State,
                prepared.Program,
                prepared.ConstantBindings,
                materialSamplerBindings,
                prepared.RuntimeSamplerRequirements);
        }
        catch
        {
            if (indexBuffer != 0)
                _gl.DeleteBuffer(indexBuffer);
            if (vertexBuffer != 0)
                _gl.DeleteBuffer(vertexBuffer);
            if (vertexArray != 0)
                _gl.DeleteVertexArray(vertexArray);
            throw;
        }
    }

    private WireframeGeometry CreateWireframeGeometry(
        IReadOnlyList<XModelRenderSurface> surfaces)
    {
        int vertexCount = checked(surfaces.Sum(surface =>
            surface.Positions.Count));
        int triangleIndexCount = checked(surfaces.Sum(surface =>
            surface.Indices.Count));
        if (vertexCount == 0 || triangleIndexCount == 0)
            return default;

        var positions = new float[checked(vertexCount * 3)];
        var edges = new uint[checked(triangleIndexCount * 2)];
        int vertexOffset = 0;
        int edgeOffset = 0;
        foreach (XModelRenderSurface surface in surfaces)
        {
            for (int vertex = 0; vertex < surface.Positions.Count; vertex++)
            {
                Vector3 value = surface.Positions[vertex];
                int destination = checked((vertexOffset + vertex) * 3);
                positions[destination] = value.X;
                positions[destination + 1] = value.Y;
                positions[destination + 2] = value.Z;
            }
            for (int triangle = 0;
                 triangle < surface.Indices.Count;
                 triangle += 3)
            {
                uint a = checked((uint)vertexOffset +
                    surface.Indices[triangle]);
                uint b = checked((uint)vertexOffset +
                    surface.Indices[triangle + 1]);
                uint c = checked((uint)vertexOffset +
                    surface.Indices[triangle + 2]);
                edges[edgeOffset++] = a;
                edges[edgeOffset++] = b;
                edges[edgeOffset++] = b;
                edges[edgeOffset++] = c;
                edges[edgeOffset++] = c;
                edges[edgeOffset++] = a;
            }
            vertexOffset = checked(vertexOffset + surface.Positions.Count);
        }

        uint vertexArray = 0;
        uint vertexBuffer = 0;
        uint indexBuffer = 0;
        try
        {
            vertexArray = _gl.GenVertexArray();
            vertexBuffer = _gl.GenBuffer();
            indexBuffer = _gl.GenBuffer();
            _gl.BindVertexArray(vertexArray);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexBuffer);
            fixed (float* vertexPointer = positions)
            {
                _gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    checked((nuint)(positions.Length * sizeof(float))),
                    vertexPointer,
                    BufferUsageARB.StaticDraw);
            }
            _gl.BindBuffer(
                BufferTargetARB.ElementArrayBuffer,
                indexBuffer);
            fixed (uint* indexPointer = edges)
            {
                _gl.BufferData(
                    BufferTargetARB.ElementArrayBuffer,
                    checked((nuint)(edges.Length * sizeof(uint))),
                    indexPointer,
                    BufferUsageARB.StaticDraw);
            }
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(
                0,
                3,
                VertexAttribPointerType.Float,
                false,
                3 * sizeof(float),
                null);
            _gl.BindVertexArray(0);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            return new WireframeGeometry(
                vertexArray,
                vertexBuffer,
                indexBuffer,
                checked((uint)edges.Length));
        }
        catch
        {
            if (indexBuffer != 0)
                _gl.DeleteBuffer(indexBuffer);
            if (vertexBuffer != 0)
                _gl.DeleteBuffer(vertexBuffer);
            if (vertexArray != 0)
                _gl.DeleteVertexArray(vertexArray);
            throw;
        }
    }

    private void ConfigureRsxVertexAttributes(
        MapRenderOpenGlPackedRsxVertexLayout layout)
    {
        uint stride = checked((uint)layout.FloatStride * sizeof(float));
        uint packedAttribute = 0;
        for (uint attribute = 0;
             attribute < MapRenderOpenGlPackedRsxVertexLayout
                 .SourceAttributeCount;
             attribute++)
        {
            if (!layout.ContainsAttribute(checked((int)attribute)))
                continue;
            _gl.EnableVertexAttribArray(attribute);
            _gl.VertexAttribPointer(
                attribute,
                4,
                VertexAttribPointerType.Float,
                false,
                stride,
                (void*)(packedAttribute *
                    MapRenderOpenGlPackedRsxVertexLayout
                        .AttributeFloatCount * sizeof(float)));
            packedAttribute++;
        }
    }

    private void EstablishFrameState(int framebuffer, int width, int height)
    {
        _state.InvalidateAll();
        _state.EstablishKnownTextureBaseline(TextureUnitCount);
        _state.BindFramebuffer(
            FramebufferTarget.Framebuffer,
            checked((uint)framebuffer));
        _gl.DrawBuffer(framebuffer == 0
            ? DrawBufferMode.Back
            : DrawBufferMode.ColorAttachment0);
        _state.Viewport(0, 0, width, height);
        _state.SetEnabled(EnableCap.ScissorTest, false);
        _state.ColorMask(true, true, true, true);
        _state.DepthMask(true);
        _state.BindVertexArray(0);
        _state.BindArrayBuffer(0);
        _state.UseProgram(0);
        _gl.DepthRange(
            MapRenderOpenGlRsxClipSpaceLowering.SceneDepthRange.Minimum,
            MapRenderOpenGlRsxClipSpaceLowering.SceneDepthRange.Maximum);
        _gl.ClearColor(0.047f, 0.059f, 0.078f, 1f);
        _gl.ClearDepth(1d);
        _gl.Clear(
            ClearBufferMask.ColorBufferBit |
            ClearBufferMask.DepthBufferBit);
    }

    private void DrawWireframe(Matrix4x4 viewProjection)
    {
        if (_wireframe.IndexCount == 0)
            return;

        _state.SetEnabled(EnableCap.FramebufferSrgb, false);
        _state.SetEnabled(EnableCap.DepthTest, true);
        _state.DepthFunc(DepthFunction.Lequal);
        _state.DepthMask(true);
        _state.SetEnabled(EnableCap.Blend, false);
        _state.SetEnabled(EnableCap.CullFace, false);
        _state.SetEnabled(EnableCap.StencilTest, false);
        _state.SetEnabled(EnableCap.PolygonOffsetFill, false);
        _state.PolygonMode(PolygonMode.Fill);
        _state.ColorMask(true, true, true, true);
        _state.LineWidth(1f);
        _state.UseProgram(_wireframeProgram);
        _state.UniformMatrix4(
            _wireframeViewProjectionLocation,
            viewProjection);
        _state.BindVertexArray(_wireframe.VertexArray);
        _gl.DrawElements(
            PrimitiveType.Lines,
            _wireframe.IndexCount,
            DrawElementsType.UnsignedInt,
            null);
        _state.BindVertexArray(0);
        _state.UseProgram(0);
    }

    private void BindRuntimeSamplers(
        IReadOnlyList<MapRenderShaderRuntimeSamplerRequirement> requirements)
    {
        foreach (MapRenderShaderRuntimeSamplerRequirement requirement in
                 requirements)
        {
            if (_neutralModelLightingAtlas == 0)
            {
                throw new InvalidOperationException(
                    "The native neutral XModel lighting cache was not uploaded.");
            }
            _state.ActiveTexture(requirement.Destination);
            _state.BindSampler(requirement.Destination, 0);
            _state.BindTexture(
                TextureTarget.Texture3D,
                _neutralModelLightingAtlas);
        }
    }

    private uint GetOrCreateTexture(MapRenderTexture texture)
    {
        if (_textureHandles.TryGetValue(texture, out uint handle))
            return handle;

        handle = CreateTexture(texture);
        _textureHandles.Add(texture, handle);
        return handle;
    }

    private uint CreateTexture(MapRenderTexture texture)
    {
        uint handle = _gl.GenTexture();
        TextureTarget target = ToGlTextureTarget(texture.Target);
        try
        {
            _gl.BindTexture(target, handle);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            if (texture.Target == MapRenderTextureTarget.Texture2D)
            {
                UploadTextureLevel(
                    TextureTarget.Texture2D,
                    0,
                    texture.Width,
                    texture.Height,
                    texture.RgbaBytes);
                for (int mipIndex = 0;
                     mipIndex < texture.MipLevels.Count;
                     mipIndex++)
                {
                    MapRenderTextureMip mip = texture.MipLevels[mipIndex];
                    UploadTextureLevel(
                        TextureTarget.Texture2D,
                        checked(mipIndex + 1),
                        mip.Width,
                        mip.Height,
                        mip.RgbaBytes);
                }
            }
            else
            {
                for (int faceIndex = 0;
                     faceIndex < texture.CubeFaces!.Count;
                     faceIndex++)
                {
                    MapRenderTextureCubeFace face =
                        texture.CubeFaces[faceIndex];
                    TextureTarget faceTarget = (TextureTarget)(
                        (int)TextureTarget.TextureCubeMapPositiveX +
                        faceIndex);
                    UploadTextureLevel(
                        faceTarget,
                        0,
                        texture.Width,
                        texture.Height,
                        face.RgbaBytes);
                    for (int mipIndex = 0;
                         mipIndex < face.MipLevels.Count;
                         mipIndex++)
                    {
                        MapRenderTextureMip mip = face.MipLevels[mipIndex];
                        UploadTextureLevel(
                            faceTarget,
                            checked(mipIndex + 1),
                            mip.Width,
                            mip.Height,
                            mip.RgbaBytes);
                    }
                }
            }
            int maximumMip = texture.MipLevels.Count;
            if (maximumMip == 0 &&
                texture.DecodedSamplerState.MipFilter !=
                    MapRenderTextureFilter.None)
            {
                _gl.GenerateMipmap(target);
                maximumMip = MaxMipLevel(texture.Width, texture.Height);
            }
            _textureParameters.Apply(texture, maximumMip, target);
            return handle;
        }
        catch
        {
            _gl.DeleteTexture(handle);
            throw;
        }
        finally
        {
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            _gl.BindTexture(target, 0);
        }
    }

    private uint CreateNeutralModelLightingAtlas()
    {
        byte[] rgba = new byte[
            MapRenderStaticModelLightingAtlas.Width *
            MapRenderStaticModelLightingAtlas.Height *
            MapRenderStaticModelLightingAtlas.Depth * 4];
        int baseX =
            (NeutralDynamicLightingEntry &
             (MapRenderStaticModelLightingAtlas.EntriesPerRow - 1)) *
            MapRenderStaticModelLightingAtlas.TileWidth;
        int baseY =
            (NeutralDynamicLightingEntry /
             MapRenderStaticModelLightingAtlas.EntriesPerRow) *
            MapRenderStaticModelLightingAtlas.TileHeight;
        for (int z = 0;
             z < MapRenderStaticModelLightingAtlas.TileDepth;
             z++)
        {
            for (int y = 0;
                 y < MapRenderStaticModelLightingAtlas.TileHeight;
                 y++)
            {
                for (int x = 0;
                     x < MapRenderStaticModelLightingAtlas.TileWidth;
                     x++)
                {
                    int offset = checked(
                        (((z * MapRenderStaticModelLightingAtlas.Height +
                           baseY + y) *
                          MapRenderStaticModelLightingAtlas.Width +
                          baseX + x) * 4));
                    rgba[offset] = 128;
                    rgba[offset + 1] = 128;
                    rgba[offset + 2] = 128;
                    rgba[offset + 3] = 0;
                }
            }
        }

        uint handle = _gl.GenTexture();
        try
        {
            _gl.BindTexture(TextureTarget.Texture3D, handle);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            fixed (byte* pixels = rgba)
            {
                _gl.TexImage3D(
                    TextureTarget.Texture3D,
                    0,
                    InternalFormat.Rgba8,
                    MapRenderStaticModelLightingAtlas.Width,
                    MapRenderStaticModelLightingAtlas.Height,
                    MapRenderStaticModelLightingAtlas.Depth,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    pixels);
            }
            _gl.TexParameter(
                TextureTarget.Texture3D,
                TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Linear);
            _gl.TexParameter(
                TextureTarget.Texture3D,
                TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
            _gl.TexParameter(
                TextureTarget.Texture3D,
                TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(
                TextureTarget.Texture3D,
                TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(
                TextureTarget.Texture3D,
                TextureParameterName.TextureWrapR,
                (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(
                TextureTarget.Texture3D,
                TextureParameterName.TextureBaseLevel,
                0);
            _gl.TexParameter(
                TextureTarget.Texture3D,
                TextureParameterName.TextureMaxLevel,
                0);
            return handle;
        }
        catch
        {
            _gl.DeleteTexture(handle);
            throw;
        }
        finally
        {
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            _gl.BindTexture(TextureTarget.Texture3D, 0);
        }
    }

    private uint GetOrCreateNeutralReflectionCube()
    {
        if (_neutralReflectionCube != 0)
            return _neutralReflectionCube;

        ReadOnlySpan<byte> neutral = [0, 0, 0, 255];
        uint handle = _gl.GenTexture();
        try
        {
            _gl.BindTexture(TextureTarget.TextureCubeMap, handle);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            fixed (byte* pixel = neutral)
            {
                for (int face = 0; face < 6; face++)
                {
                    _gl.TexImage2D(
                        (TextureTarget)(
                            (int)TextureTarget.TextureCubeMapPositiveX +
                            face),
                        0,
                        InternalFormat.Rgba8,
                        1,
                        1,
                        0,
                        PixelFormat.Rgba,
                        PixelType.UnsignedByte,
                        pixel);
                }
            }
            _gl.TexParameter(
                TextureTarget.TextureCubeMap,
                TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Linear);
            _gl.TexParameter(
                TextureTarget.TextureCubeMap,
                TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
            _gl.TexParameter(
                TextureTarget.TextureCubeMap,
                TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(
                TextureTarget.TextureCubeMap,
                TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(
                TextureTarget.TextureCubeMap,
                TextureParameterName.TextureWrapR,
                (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(
                TextureTarget.TextureCubeMap,
                TextureParameterName.TextureBaseLevel,
                0);
            _gl.TexParameter(
                TextureTarget.TextureCubeMap,
                TextureParameterName.TextureMaxLevel,
                0);
            _neutralReflectionCube = handle;
            return handle;
        }
        catch
        {
            _gl.DeleteTexture(handle);
            throw;
        }
        finally
        {
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            _gl.BindTexture(TextureTarget.TextureCubeMap, 0);
        }
    }

    private void UploadTextureLevel(
        TextureTarget target,
        int level,
        int width,
        int height,
        byte[] rgbaBytes)
    {
        fixed (byte* pixels = rgbaBytes)
        {
            _gl.TexImage2D(
                target,
                level,
                InternalFormat.Rgba8,
                checked((uint)width),
                checked((uint)height),
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixels);
        }
    }

    private static bool CanUploadTexture(
        MapRenderTexture texture,
        out string blocker)
    {
        if (texture.Target is not
            (MapRenderTextureTarget.Texture2D or
             MapRenderTextureTarget.TextureCube))
        {
            blocker = $"TARGET_{texture.Target}_UNSUPPORTED";
            return false;
        }
        if (!texture.HasCompleteDecodedRgbaPayload)
        {
            blocker = "DECODED_RGBA_CHAIN_INCOMPLETE";
            return false;
        }
        blocker = string.Empty;
        return true;
    }

    private static TextureTarget ToGlTextureTarget(
        MapRenderTextureTarget target) => target switch
    {
        MapRenderTextureTarget.Texture2D => TextureTarget.Texture2D,
        MapRenderTextureTarget.TextureCube => TextureTarget.TextureCubeMap,
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    private uint LinkProgram(string vertexSource, string fragmentSource)
    {
        uint vertexShader = CompileShader(
            ShaderType.VertexShader,
            vertexSource);
        try
        {
            uint fragmentShader = CompileShader(
                ShaderType.FragmentShader,
                fragmentSource);
            try
            {
                uint program = _gl.CreateProgram();
                _gl.AttachShader(program, vertexShader);
                _gl.AttachShader(program, fragmentShader);
                _gl.LinkProgram(program);
                _gl.GetProgram(
                    program,
                    ProgramPropertyARB.LinkStatus,
                    out int status);
                if (status != 0)
                    return program;

                string info = _gl.GetProgramInfoLog(program);
                _gl.DeleteProgram(program);
                throw new InvalidOperationException(
                    $"XModel viewer OpenGL program link failed: {info}");
            }
            finally
            {
                _gl.DeleteShader(fragmentShader);
            }
        }
        finally
        {
            _gl.DeleteShader(vertexShader);
        }
    }

    private MapRenderOpenGlLinkedProgramHandleResolution
        ResolveLinkedProgram(
            string vertexSource,
            string fragmentSource)
    {
        MapRenderOpenGlProgramKey key =
            MapRenderOpenGlProgramKey.Create(
                vertexSource,
                fragmentSource,
                MapRenderOpenGlSharedProgramCache
                    .EditorPreviewLinkProfileIdentity);
        if (_viewerProgramResolutions.TryGetValue(
                key,
                out MapRenderOpenGlLinkedProgramHandleResolution
                    viewerResolution))
        {
            return viewerResolution with { IsReuse = true };
        }

        MapRenderOpenGlLinkedProgramHandleResolution resolution =
            _sharedProgramUsage.GetOrLink(
                vertexSource,
                fragmentSource,
                () => LinkProgram(vertexSource, fragmentSource));
        if (!resolution.IsCacheResident)
        {
            _viewerProgramResolutions.Add(key, resolution);
            if (resolution.IsReady)
                _viewerOwnedProgramHandles.Add(resolution.Handle);
        }
        return resolution;
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(
            shader,
            ShaderParameterName.CompileStatus,
            out int status);
        if (status != 0)
            return shader;

        string info = _gl.GetShaderInfoLog(shader);
        _gl.DeleteShader(shader);
        throw new InvalidOperationException(
            $"XModel viewer OpenGL {type} compile failed: {info}");
    }

    private int RequireUniform(uint program, string name)
    {
        int location = _gl.GetUniformLocation(program, name);
        if (location >= 0)
            return location;
        throw new InvalidOperationException(
            $"XModel viewer OpenGL program omitted required uniform '{name}'.");
    }

    private void DeleteUploadedResources()
    {
        foreach (AuthoredDrawGroup group in _drawGroups)
        {
            foreach (AuthoredDraw draw in group.Draws)
                DeleteAuthoredDraw(draw);
        }
        _drawGroups.Clear();
        foreach (uint texture in _textureHandles.Values.Distinct())
            _gl.DeleteTexture(texture);
        _textureHandles.Clear();
        if (_neutralModelLightingAtlas != 0)
            _gl.DeleteTexture(_neutralModelLightingAtlas);
        _neutralModelLightingAtlas = 0;
        if (_neutralReflectionCube != 0)
            _gl.DeleteTexture(_neutralReflectionCube);
        _neutralReflectionCube = 0;
        DeleteWireframe(_wireframe);
        _wireframe = default;
        _state.InvalidateAll();
    }

    private void DeleteViewerOwnedPrograms()
    {
        foreach (uint program in _viewerOwnedProgramHandles)
            _gl.DeleteProgram(program);
        _viewerOwnedProgramHandles.Clear();
        _viewerProgramResolutions.Clear();
    }

    private void DeleteAuthoredDraw(AuthoredDraw draw)
    {
        if (draw.IndexBuffer != 0)
            _gl.DeleteBuffer(draw.IndexBuffer);
        if (draw.VertexBuffer != 0)
            _gl.DeleteBuffer(draw.VertexBuffer);
        if (draw.VertexArray != 0)
            _gl.DeleteVertexArray(draw.VertexArray);
    }

    private void DeleteWireframe(WireframeGeometry geometry)
    {
        if (geometry.IndexBuffer != 0)
            _gl.DeleteBuffer(geometry.IndexBuffer);
        if (geometry.VertexBuffer != 0)
            _gl.DeleteBuffer(geometry.VertexBuffer);
        if (geometry.VertexArray != 0)
            _gl.DeleteVertexArray(geometry.VertexArray);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private static int MaxMipLevel(int width, int height)
    {
        int size = Math.Max(width, height);
        int level = 0;
        while (size > 1)
        {
            size >>= 1;
            level++;
        }
        return level;
    }

    private sealed record PreparedPass(
        XModelRenderAuthoredPass Packet,
        MapRenderOpenGlPackedRsxVertexLayout Layout,
        GlRsxProgram Program,
        GlRsxConstantBinding[] ConstantBindings,
        MapRenderMaterialSamplerBinding[] MaterialSamplers,
        MapRenderShaderRuntimeSamplerRequirement[]
            RuntimeSamplerRequirements);

    private sealed record AuthoredDrawGroup(
        int GroupId,
        AuthoredDraw[] Draws);

    private readonly record struct AuthoredDraw(
        uint VertexArray,
        uint VertexBuffer,
        uint IndexBuffer,
        uint IndexCount,
        MapRenderState State,
        GlRsxProgram Program,
        GlRsxConstantBinding[] ConstantBindings,
        GlRsxSamplerBinding[] MaterialSamplerBindings,
        MapRenderShaderRuntimeSamplerRequirement[]
            RuntimeSamplerRequirements);

    private readonly record struct WireframeGeometry(
        uint VertexArray,
        uint VertexBuffer,
        uint IndexBuffer,
        uint IndexCount);

    private const string WireframeVertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        uniform mat4 uViewProjection;
        void main()
        {
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
        }
        """;

    private const string WireframeFragmentShaderSource = """
        #version 330 core
        out vec4 FragColor;
        void main()
        {
            FragColor = vec4(0.55, 0.88, 0.22, 1.0);
        }
        """;
}
