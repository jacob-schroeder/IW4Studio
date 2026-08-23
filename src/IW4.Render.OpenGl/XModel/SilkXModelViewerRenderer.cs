using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Techniques;
using System.Numerics;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Lighting;
using IW4.Render.Materials;
using IW4.Render.OpenGl.Programs;
using IW4.Render.Shaders;
using IW4.Render.Textures;
using Texture = IW4.Render.Textures.Texture;
using TextureTarget = Silk.NET.OpenGL.TextureTarget;
using RenderTextureTarget = IW4.Render.Textures.TextureTarget;
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
    private const int ViewerReflectionEnvironmentSize = 32;
    private const int ViewerReflectionEnvironmentMaxMipLevel = 5;
    private const int NeutralDynamicLightingEntry =
        ModelLightingAtlasLayout.StaticEntryCapacity;

    private readonly GL _gl;
    private readonly SilkOpenGlStateShadow _state;
    private readonly SilkOpenGlTextureParameters _textureParameters;
    private readonly OpenGlSharedProgramCache _sharedPrograms;
    private readonly OpenGlSharedProgramCache.UsageLease
        _sharedProgramUsage;
    private readonly HashSet<uint> _viewerOwnedProgramHandles = [];
    private readonly Dictionary<
        OpenGlProgramKey,
        OpenGlLinkedProgramHandleResolution>
        _viewerProgramResolutions = [];
    private readonly SilkOpenGlAuthoredMaterialExecutor _authoredMaterials;
    private readonly Dictionary<Texture, uint> _textureHandles =
        new(ReferenceEqualityComparer.Instance);
    private readonly uint _checkerboardProgram;
    private readonly uint _checkerboardVertexArray;
    private readonly uint _wireframeProgram;
    private readonly int _wireframeViewProjectionLocation;
    private readonly int _wireframeColorLocation;
    private readonly List<MapRenderEditorDrawGroup<AuthoredDraw>>
        _drawGroups = [];
    private WireframeGeometry _wireframe;
    private WireframeGeometry _collisionWireframe;
    private uint _neutralModelLightingAtlas;
    private uint _viewerReflectionEnvironment;
    private bool? _viewerReflectionEnvironmentStudioEnabled;
    private bool _disposed;

    public SilkXModelViewerRenderer(
        GL gl,
        string? programBinaryCacheDirectory = null)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _state = new SilkOpenGlStateShadow(gl);
        _textureParameters = new SilkOpenGlTextureParameters(gl);
        _sharedPrograms = new OpenGlSharedProgramCache(
            gl,
            programBinaryCacheDirectory:
                programBinaryCacheDirectory);
        _sharedProgramUsage = _sharedPrograms.AcquireUsageLease(gl);
        try
        {
            _authoredMaterials = new SilkOpenGlAuthoredMaterialExecutor(
                gl,
                _state,
                ResolveLinkedProgram);
            _checkerboardProgram = LinkProgram(
                CheckerboardVertexShaderSource,
                CheckerboardFragmentShaderSource);
            _checkerboardVertexArray = _gl.GenVertexArray();
            if (_checkerboardVertexArray == 0)
            {
                throw new InvalidOperationException(
                    "OpenGL did not allocate the XModel viewer checkerboard vertex array.");
            }
            _wireframeProgram = LinkProgram(
                WireframeVertexShaderSource,
                WireframeFragmentShaderSource);
            _wireframeViewProjectionLocation =
                RequireUniform(_wireframeProgram, "uViewProjection");
            _wireframeColorLocation = RequireUniform(_wireframeProgram, "uColor");
        }
        catch
        {
            if (_wireframeProgram != 0)
                _gl.DeleteProgram(_wireframeProgram);
            if (_checkerboardVertexArray != 0)
                _gl.DeleteVertexArray(_checkerboardVertexArray);
            if (_checkerboardProgram != 0)
                _gl.DeleteProgram(_checkerboardProgram);
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
            _collisionWireframe = CreateWireframeGeometry(lod.Surfaces, collisionOnly: true);
            for (int surfaceOrdinal = 0;
                 surfaceOrdinal < lod.Surfaces.Count;
                 surfaceOrdinal++)
            {
                XModelRenderSurface surface =
                    lod.Surfaces[surfaceOrdinal];
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
                    MapRenderEditorDrawBucketClassification
                        classification =
                            MapRenderEditorDrawBucketClassifier.Classify(
                                prepared
                                    .Select(pass => pass.Packet.State)
                                    .ToArray());
                    MapRenderEditorDrawGroup<AuthoredDraw> drawGroup =
                        surface.Bounds.IsValid
                            ? MapRenderEditorDrawGroup<AuthoredDraw>
                                .FromBounds(
                                    surfaceOrdinal,
                                    classification,
                                    draws,
                                    surface.Bounds)
                            : MapRenderEditorDrawGroup<AuthoredDraw>
                                .FromExplicitDepth(
                                    surfaceOrdinal,
                                    classification,
                                    draws,
                                    0f);
                    _drawGroups.Add(drawGroup);
                }
                catch
                {
                    foreach (AuthoredDraw draw in draws)
                        DeleteAuthoredDraw(draw);
                    throw;
                }
                string[] viewerInputs = prepared
                    .SelectMany(pass => pass.MaterialSamplers)
                    .Where(binding => string.Equals(
                        binding.ExternalResourceIdentity,
                        XModelRenderAuthoredPass
                            .ViewerReflectionProbeResourceIdentity,
                        StringComparison.Ordinal))
                    .Any()
                        ? ["viewerReflectionEnvironment"]
                        : [];
                if (prepared.Any(pass =>
                        pass.RuntimeSamplerRequirements.Length != 0))
                {
                    viewerInputs = viewerInputs
                        .Append("nativeViewerModelLightingEntry7168")
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
        RenderCamera camera,
        float aspectRatio) =>
        OpenGlRsxClipSpaceLowering
            .CreateDirectEditorPreviewHostViewProjection(
                OpenGlDerivedMatrixPolicy.CreatePreviewFromCamera(
                    camera,
                    aspectRatio));

    public void Render(
        int framebuffer,
        int width,
        int height,
        RenderCamera camera,
        float materialTimeSeconds,
        bool studioEnvironmentEnabled,
        bool showWireframe,
        bool showCollision = false)
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
        DerivedMatrixState matrices =
            OpenGlDerivedMatrixPolicy.CreatePreviewFromCamera(
                camera,
                aspect);
        ApplyViewerReflectionEnvironment(studioEnvironmentEnabled);
        EstablishFrameState(framebuffer, width, height);
        DrawCheckerboard();
        if (showWireframe)
        {
            DrawWireframe(_wireframe, CreateHostViewProjection(camera, aspect), 0.55f, 0.88f, 0.22f);
            return;
        }
        if (showCollision)
        {
            DrawWireframe(_collisionWireframe, CreateHostViewProjection(camera, aspect), 1f, 0.45f, 0.05f);
            return;
        }

        IReadOnlyList<MapRenderEditorDrawGroup<AuthoredDraw>>
            sortedDrawGroups = MapRenderEditorDrawQueueSorter.Sort(
                _drawGroups,
                camera.Position,
                camera.Forward);
        foreach (MapRenderEditorDrawGroup<AuthoredDraw> group in
                 sortedDrawGroups)
        {
            int groupId = group.AuthoredPasses[0].GroupId;
            foreach (AuthoredDraw draw in group.AuthoredPasses)
            {
                if (!_authoredMaterials.TryApplyRenderState(
                        draw.State,
                        stencilTargetContract: null,
                        out string? stateBlocker))
                {
                    throw new InvalidOperationException(
                        $"Preflighted XModel authored group {groupId} lost its render-state contract: {stateBlocker}");
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
                        $"Preflighted XModel authored group {groupId} lost its constant contract: {constantBlocker}");
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
        _gl.DeleteVertexArray(_checkerboardVertexArray);
        _gl.DeleteProgram(_checkerboardProgram);
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
        ShaderExecutionContract execution = packet.ShaderExecution;
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
            OpenGlPackedRsxVertexLayout.SourceFloatStride);
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
            ShaderExecutionPurpose.CameraColor)
        {
            blocker = $"EXECUTION_PURPOSE_{execution.Purpose}_UNSUPPORTED";
            return false;
        }
        if (execution.FragmentDepthExportEnabled)
        {
            blocker = "FRAGMENT_DEPTH_EXPORT_REQUIRES_OWNED_DEPTH_EXPORT_PATH";
            return false;
        }
        if (packet.State.Stencil.Enabled)
        {
            blocker = "STENCIL_REQUIRES_OWNED_D24S8_TARGET";
            return false;
        }
        int[] programSamplerDestinations = execution
            .ProgramSamplerDestinations
            .Distinct()
            .Order()
            .ToArray();
        HashSet<int> programSamplerDestinationSet =
            programSamplerDestinations.ToHashSet();
        ShaderRuntimeSamplerRequirement[] runtimeSamplerRequirements =
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
                out MaterialSamplerBinding[] materialSamplers,
                out blocker))
        {
            return false;
        }

        TranslatedProgramDirectCodeConstantPlanBuildResult
            directResult =
                TranslatedProgramDirectCodeConstantPlanner
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
        TranslatedProgramDirectCodeConstantPlan directPlan =
            directResult.Plan!;
        ushort[] unsupportedDynamicRows = directPlan.DynamicSourceRows
            .Where(row => row is not
                (FrameDirectCodeConstants.GameTimeRowIndex or
                 FrameDirectCodeConstants
                     .StaticModelBaseLightingCoordsRowIndex or
                 FrameDirectCodeConstants
                     .StaticModelLightProbeAmbientRowIndex))
            .ToArray();
        if (unsupportedDynamicRows.Length != 0)
        {
            blocker = "RUNTIME_DIRECT_ROWS_UNAVAILABLE:" +
                string.Join(',', unsupportedDynamicRows.Select(row =>
                    $"0x{row:X2}"));
            return false;
        }

        TranslatedProgramVertexConstantBindingPlanBuildResult
            vertexResult =
                TranslatedProgramVertexConstantBindingPlanner
                    .TryPlan(
                        execution.ProgramVertexConstantDestinations,
                        execution.ConstantDestinations,
                        execution.EmbeddedVertexConstants,
                        directPlan);
        if (!vertexResult.IsReady ||
            vertexResult.Plan is not { } vertexPlan)
        {
            blocker = "VERTEX_CONSTANTS:" +
                string.Join('|', vertexResult.Blockers);
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
            out string? programBlocker);
        if (program.Handle == 0)
        {
            blocker = "OPENGL_PROGRAM:" +
                (programBlocker ?? "LINK_OR_LOWERING_FAILED");
            return false;
        }
        Vector4 neutralLightProbeAmbient =
            MapRenderStaticModelLightingAtlasBuilder
                .DecodeLightProbeAmbientRow(
                    GfxColor.FromRgba(128, 128, 128, 0));
        if (!_authoredMaterials.TryCreateConstantBindings(
                execution,
                program,
                directPlan,
                vertexPlan,
                out GlRsxConstantBinding[] constantBindings,
                out string? constantBlocker,
                vertexConstantOverrides: vertexPlan.Bindings
                    .Where(binding => binding.Kind is
                        TranslatedProgramVertexConstantBindingKind
                            .PerInstanceStaticModelBaseLightingCoords or
                        TranslatedProgramVertexConstantBindingKind
                            .PerInstanceStaticModelLightProbeAmbient)
                    .ToDictionary(
                        binding => (int)binding.Destination,
                        binding => binding.Kind ==
                            TranslatedProgramVertexConstantBindingKind
                                .PerInstanceStaticModelBaseLightingCoords
                            ? ModelLightingAtlasLayout.EntryCoordinates(
                                NeutralDynamicLightingEntry)
                            : neutralLightProbeAmbient)))
        {
            blocker = "OPENGL_CONSTANT_BINDINGS:" + constantBlocker;
            return false;
        }

        OpenGlPackedRsxVertexLayout layout;
        try
        {
            layout = new OpenGlPackedRsxVertexLayout(
                OpenGlPackedRsxVertexLayout.ResolveAttributeMask(
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
        IReadOnlyList<ShaderRuntimeSamplerRequirement> requirements,
        out string? blocker)
    {
        blocker = null;
        foreach (ShaderRuntimeSamplerRequirement requirement in
                 requirements)
        {
            if (requirement.Destination >= TextureUnitCount)
            {
                blocker =
                    $"RUNTIME_SAMPLER_DEST_{requirement.Destination}_OUT_OF_RANGE";
                return false;
            }
            if (requirement.ResourceKind !=
                    ShaderRuntimeSamplerResourceKind
                        .ModelLightingAtlas ||
                requirement.Status !=
                    ShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneAtlasRequired ||
                requirement.CodeSamplerArgument !=
                    MaterialTextureSource.ModelLighting ||
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
        ShaderExecutionContract execution,
        IReadOnlyList<MaterialSamplerBinding> bindings,
        IReadOnlyList<int> programSamplerDestinations,
        IReadOnlyList<ShaderRuntimeSamplerRequirement>
            runtimeSamplerRequirements,
        out MaterialSamplerBinding[] selected,
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
        var result = new List<MaterialSamplerBinding>(
            destinations.Length);
        foreach (ushort destination in destinations)
        {
            if (destination >= TextureUnitCount)
            {
                blocker =
                    $"MATERIAL_SAMPLER_DEST_{destination}_OUT_OF_RANGE";
                return false;
            }
            MaterialSamplerBinding[] candidates = bindings
                .Where(binding =>
                    binding.Identity.SamplerDest == destination)
                .ToArray();
            if (candidates.Length != 1)
            {
                blocker =
                    $"MATERIAL_SAMPLER_DEST_{destination}_OWNER_COUNT_{candidates.Length}";
                return false;
            }
            MaterialSamplerBinding candidate = candidates[0];
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
                        XModelRenderAuthoredPass
                            .ViewerReflectionProbeResourceIdentity,
                        StringComparison.Ordinal) &&
                    candidate.Identity.SamplerArgIndex < 0 &&
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
                RenderTextureTarget.Texture2D => "Texture2D",
                RenderTextureTarget.TextureCube => "TextureCube",
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
                    binding.Identity.SamplerDest == destination) +
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
                    binding.Identity.SamplerDest,
                    binding.Texture is { } texture
                        ? GetOrCreateTexture(texture)
                        : GetOrCreateViewerReflectionEnvironment(),
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
                prepared.Packet.GroupId,
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
        IReadOnlyList<XModelRenderSurface> surfaces,
        bool collisionOnly = false)
    {
        int vertexCount = checked(surfaces.Sum(surface =>
            surface.Positions.Count));
        int triangleIndexCount = checked(surfaces.Sum(surface =>
            (collisionOnly ? surface.CollisionIndices : surface.Indices).Count));
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
            IReadOnlyList<uint> sourceIndices = collisionOnly ? surface.CollisionIndices : surface.Indices;
            for (int triangle = 0;
                 triangle < sourceIndices.Count;
                 triangle += 3)
            {
                uint a = checked((uint)vertexOffset +
                    sourceIndices[triangle]);
                uint b = checked((uint)vertexOffset +
                    sourceIndices[triangle + 1]);
                uint c = checked((uint)vertexOffset +
                    sourceIndices[triangle + 2]);
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
        OpenGlPackedRsxVertexLayout layout)
    {
        uint stride = checked((uint)layout.FloatStride * sizeof(float));
        uint packedAttribute = 0;
        for (uint attribute = 0;
             attribute < OpenGlPackedRsxVertexLayout
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
                    OpenGlPackedRsxVertexLayout
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
        _state.SetEnabled(EnableCap.TextureCubeMapSeamless, true);
        _state.ColorMask(true, true, true, true);
        _state.DepthMask(true);
        _state.BindVertexArray(0);
        _state.BindArrayBuffer(0);
        _state.UseProgram(0);
        _gl.DepthRange(
            OpenGlRsxClipSpaceLowering.SceneDepthRange.Minimum,
            OpenGlRsxClipSpaceLowering.SceneDepthRange.Maximum);
        _gl.ClearColor(0.52f, 0.52f, 0.52f, 1f);
        _gl.ClearDepth(1d);
        _gl.Clear(
            ClearBufferMask.ColorBufferBit |
            ClearBufferMask.DepthBufferBit);
    }

    private void DrawCheckerboard()
    {
        _state.SetEnabled(EnableCap.FramebufferSrgb, false);
        _state.SetEnabled(EnableCap.ScissorTest, false);
        _state.SetEnabled(EnableCap.DepthTest, false);
        _state.DepthMask(false);
        _state.SetEnabled(EnableCap.Blend, false);
        _state.SetEnabled(EnableCap.CullFace, false);
        _state.SetEnabled(EnableCap.StencilTest, false);
        _state.SetEnabled(EnableCap.PolygonOffsetFill, false);
        _state.SetEnabled(EnableCap.PolygonOffsetLine, false);
        _state.SetEnabled(EnableCap.PolygonOffsetPoint, false);
        _state.PolygonMode(PolygonMode.Fill);
        _state.ColorMask(true, true, true, true);
        _state.UseProgram(_checkerboardProgram);
        _state.BindVertexArray(_checkerboardVertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _state.BindVertexArray(0);
        _state.UseProgram(0);
    }

    private void DrawWireframe(WireframeGeometry geometry, Matrix4x4 viewProjection, float red, float green, float blue)
    {
        if (geometry.IndexCount == 0)
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
        _state.Uniform3(_wireframeColorLocation, red, green, blue);
        _state.BindVertexArray(geometry.VertexArray);
        _gl.DrawElements(
            PrimitiveType.Lines,
            geometry.IndexCount,
            DrawElementsType.UnsignedInt,
            null);
        _state.BindVertexArray(0);
        _state.UseProgram(0);
    }

    private void BindRuntimeSamplers(
        IReadOnlyList<ShaderRuntimeSamplerRequirement> requirements)
    {
        foreach (ShaderRuntimeSamplerRequirement requirement in
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

    private uint GetOrCreateTexture(Texture texture)
    {
        if (_textureHandles.TryGetValue(texture, out uint handle))
            return handle;

        handle = CreateTexture(texture);
        _textureHandles.Add(texture, handle);
        return handle;
    }

    private uint CreateTexture(Texture texture)
    {
        uint handle = _gl.GenTexture();
        TextureTarget target = ToGlTextureTarget(texture.Target);
        try
        {
            _gl.BindTexture(target, handle);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            if (texture.Target == RenderTextureTarget.Texture2D)
            {
                UploadTextureLevel(
                    TextureTarget.Texture2D,
                    0,
                    texture.Width,
                    texture.Height,
                    texture.PixelBytes,
                    texture.PixelFormat,
                    texture.DecodedSamplerState.UsesSrgbReads);
                for (int mipIndex = 0;
                     mipIndex < texture.MipLevels.Count;
                     mipIndex++)
                {
                    TextureMip mip = texture.MipLevels[mipIndex];
                    UploadTextureLevel(
                        TextureTarget.Texture2D,
                        checked(mipIndex + 1),
                        mip.Width,
                        mip.Height,
                        mip.PixelBytes,
                        texture.PixelFormat,
                        texture.DecodedSamplerState.UsesSrgbReads);
                }
            }
            else
            {
                for (int faceIndex = 0;
                     faceIndex < texture.CubeFaces!.Count;
                     faceIndex++)
                {
                    TextureCubeFace face =
                        texture.CubeFaces[faceIndex];
                    TextureTarget faceTarget = (TextureTarget)(
                        (int)TextureTarget.TextureCubeMapPositiveX +
                        faceIndex);
                    UploadTextureLevel(
                        faceTarget,
                        0,
                        texture.Width,
                        texture.Height,
                        face.RgbaBytes,
                        texture.PixelFormat,
                        texture.DecodedSamplerState.UsesSrgbReads);
                    for (int mipIndex = 0;
                         mipIndex < face.MipLevels.Count;
                         mipIndex++)
                    {
                        TextureMip mip = face.MipLevels[mipIndex];
                        UploadTextureLevel(
                            faceTarget,
                            checked(mipIndex + 1),
                            mip.Width,
                            mip.Height,
                            mip.PixelBytes,
                            texture.PixelFormat,
                            texture.DecodedSamplerState.UsesSrgbReads);
                    }
                }
            }
            int maximumMip = texture.MipLevels.Count;
            if (maximumMip == 0 &&
                texture.DecodedSamplerState.MipFilter !=
                    TextureFilter.None)
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
            ModelLightingAtlasLayout.Width *
            ModelLightingAtlasLayout.Height *
            ModelLightingAtlasLayout.Depth * 4];
        int baseX =
            (NeutralDynamicLightingEntry &
             (ModelLightingAtlasLayout.EntriesPerRow - 1)) *
            ModelLightingAtlasLayout.TileWidth;
        int baseY =
            (NeutralDynamicLightingEntry /
             ModelLightingAtlasLayout.EntriesPerRow) *
            ModelLightingAtlasLayout.TileHeight;
        for (int z = 0;
             z < ModelLightingAtlasLayout.TileDepth;
             z++)
        {
            for (int y = 0;
                 y < ModelLightingAtlasLayout.TileHeight;
                 y++)
            {
                for (int x = 0;
                     x < ModelLightingAtlasLayout.TileWidth;
                     x++)
                {
                    int offset = checked(
                        (((z * ModelLightingAtlasLayout.Height +
                           baseY + y) *
                          ModelLightingAtlasLayout.Width +
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
                    ModelLightingAtlasLayout.Width,
                    ModelLightingAtlasLayout.Height,
                    ModelLightingAtlasLayout.Depth,
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

    private uint GetOrCreateViewerReflectionEnvironment()
    {
        if (_viewerReflectionEnvironment != 0)
            return _viewerReflectionEnvironment;

        byte[] neutral = CreateBlackReflectionEnvironmentFace();
        uint handle = _gl.GenTexture();
        try
        {
            _gl.BindTexture(TextureTarget.TextureCubeMap, handle);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            fixed (byte* pixels = neutral)
            {
                for (int face = 0; face < 6; face++)
                {
                    _gl.TexImage2D(
                        (TextureTarget)(
                            (int)TextureTarget.TextureCubeMapPositiveX +
                            face),
                        0,
                        InternalFormat.Rgba8,
                        ViewerReflectionEnvironmentSize,
                        ViewerReflectionEnvironmentSize,
                        0,
                        PixelFormat.Rgba,
                        PixelType.UnsignedByte,
                        pixels);
                }
            }
            _gl.GenerateMipmap(TextureTarget.TextureCubeMap);
            ApplyViewerReflectionEnvironmentSampler();
            _viewerReflectionEnvironment = handle;
            _viewerReflectionEnvironmentStudioEnabled = false;
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

    private void ApplyViewerReflectionEnvironment(bool studioEnabled)
    {
        if (_viewerReflectionEnvironment == 0 ||
            _viewerReflectionEnvironmentStudioEnabled == studioEnabled)
        {
            return;
        }

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(
            TextureTarget.TextureCubeMap,
            _viewerReflectionEnvironment);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        try
        {
            for (int face = 0; face < 6; face++)
            {
                byte[] rgba = studioEnabled
                    ? CreateStudioReflectionEnvironmentFace(face)
                    : CreateBlackReflectionEnvironmentFace();
                fixed (byte* pixels = rgba)
                {
                    _gl.TexSubImage2D(
                        (TextureTarget)(
                            (int)TextureTarget.TextureCubeMapPositiveX +
                            face),
                        0,
                        0,
                        0,
                        ViewerReflectionEnvironmentSize,
                        ViewerReflectionEnvironmentSize,
                        PixelFormat.Rgba,
                        PixelType.UnsignedByte,
                        pixels);
                }
            }

            _gl.GenerateMipmap(TextureTarget.TextureCubeMap);
            ApplyViewerReflectionEnvironmentSampler();
            _viewerReflectionEnvironmentStudioEnabled = studioEnabled;
        }
        finally
        {
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            _gl.BindTexture(TextureTarget.TextureCubeMap, 0);
        }
    }

    private void ApplyViewerReflectionEnvironmentSampler() =>
        _textureParameters.ApplySampler(
            RsxSamplerDecoder.Decode(
                RsxImplicitSamplerStateEncoding.ReflectionProbe),
            ViewerReflectionEnvironmentMaxMipLevel,
            TextureTarget.TextureCubeMap);

    private static byte[] CreateBlackReflectionEnvironmentFace()
    {
        var rgba = new byte[
            ViewerReflectionEnvironmentSize *
            ViewerReflectionEnvironmentSize * 4];
        for (int offset = 3; offset < rgba.Length; offset += 4)
            rgba[offset] = byte.MaxValue;
        return rgba;
    }

    private static byte[] CreateStudioReflectionEnvironmentFace(int face)
    {
        if (face is < 0 or >= 6)
            throw new ArgumentOutOfRangeException(nameof(face));

        var rgba = new byte[
            ViewerReflectionEnvironmentSize *
            ViewerReflectionEnvironmentSize * 4];
        Vector3 softboxDirection = Vector3.Normalize(
            new Vector3(-0.38f, 0.22f, 0.90f));
        for (int y = 0; y < ViewerReflectionEnvironmentSize; y++)
        {
            float t = 2f * (y + 0.5f) /
                ViewerReflectionEnvironmentSize - 1f;
            for (int x = 0; x < ViewerReflectionEnvironmentSize; x++)
            {
                float s = 2f * (x + 0.5f) /
                    ViewerReflectionEnvironmentSize - 1f;
                Vector3 direction = CubeFaceDirection(face, s, t);
                float overhead = SmoothStep(0.1f, 0.95f, direction.Z);
                float horizon = 1f - MathF.Abs(direction.Z);
                float softbox = MathF.Pow(
                    MathF.Max(Vector3.Dot(direction, softboxDirection), 0f),
                    18f);
                float sideVariation = 0.5f + 0.5f * direction.X;
                float intensity = Math.Clamp(
                    0.14f +
                    0.18f * horizon +
                    0.32f * overhead +
                    0.28f * softbox +
                    0.06f * sideVariation,
                    0.10f,
                    0.95f);
                byte value = checked((byte)MathF.Round(intensity * 255f));
                int offset = (y * ViewerReflectionEnvironmentSize + x) * 4;
                rgba[offset] = value;
                rgba[offset + 1] = value;
                rgba[offset + 2] = value;
                rgba[offset + 3] = byte.MaxValue;
            }
        }
        return rgba;
    }

    private static Vector3 CubeFaceDirection(int face, float s, float t) =>
        Vector3.Normalize(face switch
        {
            0 => new Vector3(1f, -t, -s),
            1 => new Vector3(-1f, -t, s),
            2 => new Vector3(s, 1f, t),
            3 => new Vector3(s, -1f, -t),
            4 => new Vector3(s, -t, 1f),
            5 => new Vector3(-s, -t, -1f),
            _ => throw new ArgumentOutOfRangeException(nameof(face))
        });

    private static float SmoothStep(float minimum, float maximum, float value)
    {
        float normalized = Math.Clamp(
            (value - minimum) / (maximum - minimum),
            0f,
            1f);
        return normalized * normalized * (3f - 2f * normalized);
    }

    private void UploadTextureLevel(
        TextureTarget target,
        int level,
        int width,
        int height,
        byte[] pixelBytes,
        DecodedTexturePixelFormat pixelFormat,
        bool useSrgbReads)
    {
        (InternalFormat internalFormat,
         PixelFormat uploadFormat,
         PixelType uploadType) = pixelFormat switch
        {
            DecodedTexturePixelFormat.Rgba8Unorm =>
                (useSrgbReads
                    ? InternalFormat.Srgb8Alpha8
                    : InternalFormat.Rgba8,
                 PixelFormat.Rgba,
                 PixelType.UnsignedByte),
            DecodedTexturePixelFormat.Rg16Float =>
                (InternalFormat.RG16f,
                 PixelFormat.RG,
                 PixelType.HalfFloat),
            _ => throw new ArgumentOutOfRangeException(
                nameof(pixelFormat),
                pixelFormat,
                null)
        };
        fixed (byte* pixels = pixelBytes)
        {
            _gl.TexImage2D(
                target,
                level,
                internalFormat,
                checked((uint)width),
                checked((uint)height),
                0,
                uploadFormat,
                uploadType,
                pixels);
        }
    }

    private static bool CanUploadTexture(
        Texture texture,
        out string blocker)
    {
        if (texture.Target is not
            (RenderTextureTarget.Texture2D or
             RenderTextureTarget.TextureCube))
        {
            blocker = $"TARGET_{texture.Target}_UNSUPPORTED";
            return false;
        }
        if (!texture.HasCompleteDecodedPayload)
        {
            blocker = "DECODED_RGBA_CHAIN_INCOMPLETE";
            return false;
        }
        blocker = string.Empty;
        return true;
    }

    private static TextureTarget ToGlTextureTarget(
        RenderTextureTarget target) => target switch
    {
        RenderTextureTarget.Texture2D => TextureTarget.Texture2D,
        RenderTextureTarget.TextureCube => TextureTarget.TextureCubeMap,
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    private uint LinkProgram(
        string vertexSource,
        string fragmentSource,
        bool requestRetrievableBinary = false)
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
                if (requestRetrievableBinary)
                {
                    _gl.ProgramParameter(
                        program,
                        ProgramParameterPName.BinaryRetrievableHint,
                        1);
                }
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

    private OpenGlLinkedProgramHandleResolution
        ResolveLinkedProgram(
            string vertexSource,
            string fragmentSource)
    {
        OpenGlProgramKey key =
            OpenGlProgramKey.Create(
                vertexSource,
                fragmentSource,
                OpenGlSharedProgramCache
                    .LinkProfileIdentity);
        if (_viewerProgramResolutions.TryGetValue(
                key,
                out OpenGlLinkedProgramHandleResolution
                    viewerResolution))
        {
            return viewerResolution with { IsReuse = true };
        }

        OpenGlLinkedProgramHandleResolution resolution =
            _sharedProgramUsage.GetOrLink(
                vertexSource,
                fragmentSource,
                () => LinkProgram(
                    vertexSource,
                    fragmentSource,
                    _sharedProgramUsage
                        .ProgramBinaryPersistenceEnabled));
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
        foreach (MapRenderEditorDrawGroup<AuthoredDraw> group in
                 _drawGroups)
        {
            foreach (AuthoredDraw draw in group.AuthoredPasses)
                DeleteAuthoredDraw(draw);
        }
        _drawGroups.Clear();
        foreach (uint texture in _textureHandles.Values.Distinct())
            _gl.DeleteTexture(texture);
        _textureHandles.Clear();
        if (_neutralModelLightingAtlas != 0)
            _gl.DeleteTexture(_neutralModelLightingAtlas);
        _neutralModelLightingAtlas = 0;
        if (_viewerReflectionEnvironment != 0)
            _gl.DeleteTexture(_viewerReflectionEnvironment);
        _viewerReflectionEnvironment = 0;
        _viewerReflectionEnvironmentStudioEnabled = null;
        DeleteWireframe(_wireframe);
        _wireframe = default;
        DeleteWireframe(_collisionWireframe);
        _collisionWireframe = default;
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
        OpenGlPackedRsxVertexLayout Layout,
        GlRsxProgram Program,
        GlRsxConstantBinding[] ConstantBindings,
        MaterialSamplerBinding[] MaterialSamplers,
        ShaderRuntimeSamplerRequirement[]
            RuntimeSamplerRequirements);

    private readonly record struct AuthoredDraw(
        int GroupId,
        uint VertexArray,
        uint VertexBuffer,
        uint IndexBuffer,
        uint IndexCount,
        RenderState State,
        GlRsxProgram Program,
        GlRsxConstantBinding[] ConstantBindings,
        GlRsxSamplerBinding[] MaterialSamplerBindings,
        ShaderRuntimeSamplerRequirement[]
            RuntimeSamplerRequirements);

    private readonly record struct WireframeGeometry(
        uint VertexArray,
        uint VertexBuffer,
        uint IndexBuffer,
        uint IndexCount);

    private const string CheckerboardVertexShaderSource = """
        #version 330 core
        const vec2 positions[3] = vec2[](
            vec2(-1.0, -1.0),
            vec2( 3.0, -1.0),
            vec2(-1.0,  3.0));
        void main()
        {
            gl_Position = vec4(positions[gl_VertexID], 0.0, 1.0);
        }
        """;

    private const string CheckerboardFragmentShaderSource = """
        #version 330 core
        layout (location = 0) out vec4 FragColor;
        void main()
        {
            vec2 cell = floor(gl_FragCoord.xy / 20.0);
            float alternate = mod(cell.x + cell.y, 2.0);
            vec3 shade = mix(vec3(0.48), vec3(0.58), alternate);
            FragColor = vec4(shade, 1.0);
        }
        """;

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
        uniform vec3 uColor;
        void main()
        {
            FragColor = vec4(uColor, 1.0);
        }
        """;
}
