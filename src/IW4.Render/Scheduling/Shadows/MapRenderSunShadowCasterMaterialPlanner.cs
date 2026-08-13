using IW4.Render.Techniques;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Assets;
using IW4.Render.Materials;
using IW4.Render.Shaders;
using IW4.Render.Textures;

namespace IW4.Render.Scheduling.Shadows;

public enum MapRenderSunShadowCasterMaterialKind : byte
{
    Opaque = 0,
    Cutout = 1
}

public enum MapRenderSunShadowCasterMaterialFailureKind
{
    TechniqueSetUnavailable,
    Slot2Unavailable,
    Slot2Ambiguous,
    Slot2TechniqueUnavailable,
    TechniquePassShapeMismatch,
    TechniqueNameUnsupported,
    ShaderProgramContractMismatch,
    ShaderArgumentTableIncomplete,
    ShaderArgumentContractMismatch,
    VertexDeclarationUnavailable,
    StateUnavailable,
    StateContractMismatch,
    CutoutRouteUnavailable,
    CutoutTextureUnavailable,
    CutoutTextureAmbiguous,
    CutoutImageUnavailable
}

public sealed record MapRenderSunShadowCasterMaterialFailure(
    MapRenderSunShadowCasterMaterialFailureKind Kind,
    string Detail);

public sealed class MapRenderSunShadowCasterMaterialPlanResult
{
    private MapRenderSunShadowCasterMaterialPlanResult(
        MapRenderSunShadowCasterMaterialPlan? plan,
        MapRenderSunShadowCasterMaterialFailure? failure)
    {
        if ((plan is null) == (failure is null))
        {
            throw new ArgumentException(
                "A caster-material result requires exactly one plan or typed failure.");
        }

        Plan = plan;
        Failure = failure;
    }

    public MapRenderSunShadowCasterMaterialPlan? Plan { get; }

    public MapRenderSunShadowCasterMaterialFailure? Failure { get; }

    public bool IsSuccess => Plan is not null;

    internal static MapRenderSunShadowCasterMaterialPlanResult Succeeded(
        MapRenderSunShadowCasterMaterialPlan plan) => new(plan, null);

    internal static MapRenderSunShadowCasterMaterialPlanResult Failed(
        MapRenderSunShadowCasterMaterialFailureKind kind,
        string detail) => new(
            null,
            new MapRenderSunShadowCasterMaterialFailure(kind, detail));
}

/// <summary>
/// One exact slot-2 material execution plan for the sun-shadow caster pass.
/// The selected source snapshot and decoded authored state are retained so a
/// renderer does not need to repeat or reinterpret asset selection.
/// </summary>
public abstract class MapRenderSunShadowCasterMaterialPlan
{
    protected MapRenderSunShadowCasterMaterialPlan(
        MapRenderSunShadowCasterMaterialKind kind,
        MaterialAsset material,
        MaterialTechniqueSetAsset techniqueSet,
        MaterialTechniqueAsset technique,
        MaterialPassAsset pass,
        SelectedPassProgramSources sources,
        RenderState state)
    {
        Kind = kind;
        Material = material;
        TechniqueSet = techniqueSet;
        Technique = technique;
        Pass = pass;
        Sources = sources;
        State = state;
    }

    public MapRenderSunShadowCasterMaterialKind Kind { get; }

    public MaterialAsset Material { get; }

    public MaterialTechniqueSetAsset TechniqueSet { get; }

    public MaterialTechniqueAsset Technique { get; }

    public MaterialPassAsset Pass { get; }

    public SelectedPassProgramSources Sources { get; }

    public RenderState State { get; }

    public int TechniqueSlot =>
        MapRenderSunShadowCasterMaterialPlanner.SunShadowTechniqueSlot;

    public int PassIndex =>
        MapRenderSunShadowCasterMaterialPlanner.SunShadowPassIndex;
}

public sealed class MapRenderSunShadowOpaqueCasterMaterialPlan :
    MapRenderSunShadowCasterMaterialPlan
{
    internal MapRenderSunShadowOpaqueCasterMaterialPlan(
        MaterialAsset material,
        MaterialTechniqueSetAsset techniqueSet,
        MaterialTechniqueAsset technique,
        MaterialPassAsset pass,
        SelectedPassProgramSources sources,
        RenderState state)
        : base(
            MapRenderSunShadowCasterMaterialKind.Opaque,
            material,
            techniqueSet,
            technique,
            pass,
            sources,
            state)
    {
    }
}

public sealed record MapRenderSunShadowCutoutSamplerBinding(
    int ArgumentIndex,
    ushort Destination,
    uint NameHash,
    TextureSemantic TextureSemantic,
    MaterialStreamSource EngineRouteSource,
    int TextureTableOrdinal,
    MaterialTextureDef Texture,
    GfxImageAsset Image,
    MaterialSamplerState RawSamplerState,
    RsxSamplerState DecodedSamplerState);

public sealed class MapRenderSunShadowCutoutCasterMaterialPlan :
    MapRenderSunShadowCasterMaterialPlan
{
    internal MapRenderSunShadowCutoutCasterMaterialPlan(
        MaterialAsset material,
        MaterialTechniqueSetAsset techniqueSet,
        MaterialTechniqueAsset technique,
        MaterialPassAsset pass,
        SelectedPassProgramSources sources,
        RenderState state,
        MapRenderSunShadowCutoutSamplerBinding sampler,
        bool usesVertexColor)
        : base(
            MapRenderSunShadowCasterMaterialKind.Cutout,
            material,
            techniqueSet,
            technique,
            pass,
            sources,
            state)
    {
        Sampler = sampler;
        UsesVertexColor = usesVertexColor;
    }

    public MapRenderSunShadowCutoutSamplerBinding Sampler { get; }

    /// <summary>
    /// True for the exact declaration route Source1 -&gt; Dest3 consumed by
    /// vertcol_simple_atest.hlsl. Its fragment program multiplies sampled
    /// color by the routed vertex color before fixed-function alpha test.
    /// </summary>
    public bool UsesVertexColor { get; }

    public float NormalizedAlphaReference => State.AlphaRef / 255.0f;
}

/// <summary>
/// Resolves only native technique slot 2. It never scans for another depth or
/// alpha-tested technique. Accepted pass shapes are explicit contracts; the
/// fixed static-model path is not generalized to
/// similarly named techniques.
/// </summary>
public static class MapRenderSunShadowCasterMaterialPlanner
{
    public const int SunShadowTechniqueSlot =
        (int)MaterialTechniqueType.BuildShadowmapDepth;
    public const int SunShadowPassIndex = 0;
    public const string OpaqueTechniqueName = "build_shadowmap_depth";
    public const string WorldOpaqueNoColorTechniqueName =
        "build_shadowmap_depth_nc";
    public const string StaticModelOpaqueTechniqueName =
        "build_shadowmap_model_nc";
    public const string CutoutTechniqueName = "build_shadowmap_depth_test_nc";
    public const string WorldCutoutColorTechniqueName =
        "build_shadowmap_depth_test";
    public const string StaticModelCutoutATestGcTechniqueName =
        "build_shadowmap_model_atest_gc";
    public const string StaticModelCutoutATestNcTechniqueName =
        "build_shadowmap_model_atest_nc";
    public const string StaticModelCutoutTestTechniqueName =
        "build_shadowmap_model_test";
    public const string StaticModelCutoutTestGcTechniqueName =
        "build_shadowmap_model_test_gc";
    public const string StaticModelCutoutTestNcTechniqueName =
        "build_shadowmap_model_test_nc";
    public const string StaticModelCasterMaterialName = "m/shadowcaster";
    public const string StaticModelVertexShaderName = "transform_only.hlsl";
    public const string StaticModelPixelShaderName = "null.hlsl";
    public const string StaticModelCutoutColorShaderName =
        "vertcol_simple_atest.hlsl";
    public const string StaticModelCutoutNoColorShaderName =
        "vertcol_simple_atest_nc.hlsl";
    public static readonly uint StaticModelWorldMatrixCodeConstant =
        new MaterialCodeConstantArgument(
            MaterialConstantSource.WorldMatrix0,
            FirstRow: 0,
            RowCount: 4).PackedValue;
    public static readonly uint StaticModelViewProjectionCodeConstant =
        new MaterialCodeConstantArgument(
            MaterialConstantSource.ViewProjectionMatrix,
            FirstRow: 0,
            RowCount: 4).PackedValue;
    public const uint CutoutColorSamplerHash = 0xA0AB1041u;
    public const ushort CutoutSamplerDestination = 0;
    public const TextureSemantic ColorTextureSemantic =
        TextureSemantic.ColorMap;
    public const MaterialStreamSource CutoutEngineRouteSource =
        MaterialStreamSource.TexCoord0;
    public const RsxCompareFunction CutoutAlphaFunction =
        RsxCompareFunction.GreaterThanOrEqual;
    public const byte CutoutAlphaReference = 0x80;

    /// <summary>
    /// Identifies the native selector's deliberate no-draw case. A literal
    /// null slot-2 cell clears active material state and bypasses draw setup.
    /// A nonzero unresolved pointer is never classified as this case.
    /// </summary>
    public static bool IsNativeNullSlot2Rejection(
        MaterialTechniqueSetAsset techniqueSet)
    {
        ArgumentNullException.ThrowIfNull(techniqueSet);
        MaterialTechniqueSlot[] slots = techniqueSet.TechniqueSlots
            .Where(slot => slot.Index == SunShadowTechniqueSlot)
            .ToArray();
        return slots.Length == 1 &&
            slots[0].Pointer.Raw == 0 &&
            slots[0].Technique is null;
    }

    public static MapRenderSunShadowCasterMaterialPlanResult Plan(
        MaterialAsset material,
        RenderAssetLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(lookup);

        if (string.Equals(
                NormalizeName(material.Info.Name),
                StaticModelCasterMaterialName,
                StringComparison.Ordinal))
        {
            return PlanFixedStaticModelCaster(material, lookup);
        }

        MaterialTechniqueSetAsset? techniqueSet =
            material.TechniqueSet ??
            lookup.ResolveTechniqueSet(material.TechniqueSetPointer);
        return techniqueSet is null
            ? MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .TechniqueSetUnavailable,
                "The material's canonical technique set is unavailable.")
            : Plan(material, techniqueSet, lookup);
    }

    public static MapRenderSunShadowCasterMaterialPlanResult Plan(
        MaterialAsset material,
        MaterialTechniqueSetAsset techniqueSet,
        RenderAssetLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(techniqueSet);
        ArgumentNullException.ThrowIfNull(lookup);

        if (string.Equals(
                NormalizeName(material.Info.Name),
                StaticModelCasterMaterialName,
                StringComparison.Ordinal))
        {
            return PlanFixedStaticModelCaster(
                material,
                techniqueSet,
                lookup);
        }

        MaterialTechniqueSlot[] exactSlots = lookup
            .ResolveTechniqueSlots(techniqueSet)
            .Where(candidate => candidate.Index == SunShadowTechniqueSlot)
            .ToArray();
        if (exactSlots.Length == 0)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind.Slot2Unavailable,
                "Technique slot 2 is unavailable; no alternative technique was scanned.");
        }
        if (exactSlots.Length != 1)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind.Slot2Ambiguous,
                $"Technique slot 2 has {exactSlots.Length} materialized candidates.");
        }

        MaterialTechniqueSlot exactSlot = exactSlots[0];
        if (exactSlot.Technique is not { } technique)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .Slot2TechniqueUnavailable,
                "Technique slot 2 has no materialized technique.");
        }
        if (technique.PassCount != 1 || technique.Passes.Count != 1)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .TechniquePassShapeMismatch,
                $"Technique slot 2 declares {technique.PassCount} pass(es) and retains {technique.Passes.Count}; exactly one is required.");
        }

        MaterialPassAsset pass = technique.Passes[SunShadowPassIndex];
        SelectedPassProgramSources sources = lookup.ResolveSources(
            techniqueSet,
            technique,
            SunShadowPassIndex,
            pass);
        if (!RenderStateDecoder.TryDecode(
                material,
                SunShadowTechniqueSlot,
                SunShadowPassIndex,
                lookup,
                out RenderState state))
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind.StateUnavailable,
                "The authored slot-2 pass state could not be decoded.");
        }

        return PlanResolved(
            material,
            techniqueSet,
            exactSlot,
            sources,
            state,
            texture => texture.Image ?? lookup.ResolveImage(texture.DataPointer));
    }

    public static MapRenderSunShadowCasterMaterialPlanResult
        PlanFixedStaticModelCaster(RenderAssetLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        MaterialAsset? material = lookup.ResolveUniqueMaterialByName(
            StaticModelCasterMaterialName);
        return material is null
            ? MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .TechniqueSetUnavailable,
                $"The unique renderer-global {StaticModelCasterMaterialName} material is unavailable.")
            : PlanFixedStaticModelCaster(material, lookup);
    }

    private static MapRenderSunShadowCasterMaterialPlanResult
        PlanFixedStaticModelCaster(
            MaterialAsset material,
            RenderAssetLookup lookup)
    {
        MaterialTechniqueSetAsset? techniqueSet =
            material.TechniqueSet ??
            lookup.ResolveTechniqueSet(material.TechniqueSetPointer);
        return techniqueSet is null
            ? MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .TechniqueSetUnavailable,
                $"{StaticModelCasterMaterialName} has no canonical technique set.")
            : PlanFixedStaticModelCaster(material, techniqueSet, lookup);
    }

    private static MapRenderSunShadowCasterMaterialPlanResult
        PlanFixedStaticModelCaster(
            MaterialAsset material,
            MaterialTechniqueSetAsset techniqueSet,
            RenderAssetLookup lookup)
    {
        MaterialTechniqueSlot[] exactSlots = techniqueSet.TechniqueSlots
            .Where(candidate => candidate.Index == SunShadowTechniqueSlot)
            .ToArray();
        if (exactSlots.Length != 1)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                exactSlots.Length == 0
                    ? MapRenderSunShadowCasterMaterialFailureKind
                        .Slot2Unavailable
                    : MapRenderSunShadowCasterMaterialFailureKind
                        .Slot2Ambiguous,
                $"{StaticModelCasterMaterialName} retained {exactSlots.Length} direct slot-2 rows.");
        }

        MaterialTechniqueSlot exactSlot = exactSlots[0];
        if (exactSlot.Technique is not { } technique)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .Slot2TechniqueUnavailable,
                $"{StaticModelCasterMaterialName} slot 2 is not materialized.");
        }
        if (technique.PassCount != 1 ||
            technique.Passes.Count != 1)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .TechniquePassShapeMismatch,
                $"{StaticModelCasterMaterialName} slot 2 requires exactly one pass.");
        }

        MaterialPassAsset pass = technique.Passes[0];
        if (!RenderStateDecoder.TryDecode(
                material,
                SunShadowTechniqueSlot,
                SunShadowPassIndex,
                lookup,
                out RenderState state))
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind.StateUnavailable,
                $"{StaticModelCasterMaterialName} slot-2 state is unavailable.");
        }

        return PlanResolved(
            material,
            techniqueSet,
            exactSlot,
            CreateFixedStaticModelSources(pass),
            state,
            texture => texture.Image ??
                lookup.ResolveImage(texture.DataPointer));
    }

    internal static MapRenderSunShadowCasterMaterialPlanResult PlanResolved(
        MaterialAsset material,
        MaterialTechniqueSetAsset techniqueSet,
        MaterialTechniqueSlot exactSlot,
        SelectedPassProgramSources sources,
        RenderState state,
        Func<MaterialTextureDef, GfxImageAsset?> resolveImage)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(techniqueSet);
        ArgumentNullException.ThrowIfNull(exactSlot);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(resolveImage);

        if (exactSlot.Index != SunShadowTechniqueSlot)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind.Slot2Unavailable,
                $"Resolved technique slot {exactSlot.Index} is not native sun-shadow slot 2.");
        }
        if (exactSlot.Technique is not { } technique)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .Slot2TechniqueUnavailable,
                "Technique slot 2 has no materialized technique.");
        }
        if (technique.PassCount != 1 || technique.Passes.Count != 1)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .TechniquePassShapeMismatch,
                $"Technique slot 2 declares {technique.PassCount} pass(es) and retains {technique.Passes.Count}; exactly one is required.");
        }
        if (!sources.HasCompleteArguments)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .ShaderArgumentTableIncomplete,
                $"Slot-2 pass expects {sources.ExpectedArgumentCount} argument(s) but {sources.LoadedArgumentCount} were materialized.");
        }
        if (!state.HasState)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind.StateUnavailable,
                "The authored slot-2 pass state is unavailable.");
        }

        string techniqueName = NormalizeName(technique.Name);
        string materialName = NormalizeName(material.Info.Name);
        MaterialPassAsset pass = technique.Passes[0];
        if (string.Equals(
                techniqueName,
                StaticModelOpaqueTechniqueName,
                StringComparison.Ordinal))
        {
            if (!string.Equals(
                    materialName,
                    StaticModelCasterMaterialName,
                    StringComparison.Ordinal))
            {
                return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                    MapRenderSunShadowCasterMaterialFailureKind
                        .TechniqueNameUnsupported,
                    $"{StaticModelOpaqueTechniqueName} is valid only on the renderer-global {StaticModelCasterMaterialName} material.");
            }
            if (sources.VertexDeclaration is not { } declaration ||
                declaration.StreamCount != 1 ||
                declaration.HasOptionalSource ||
                declaration.Routing.Count < 1 ||
                declaration.Routing[0] !=
                    new MaterialVertexStreamRouting(
                        MaterialStreamSource.Position,
                        MaterialStreamDestination.Position))
            {
                return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                    MapRenderSunShadowCasterMaterialFailureKind
                        .VertexDeclarationUnavailable,
                    "build_shadowmap_model_nc requires its authored one-stream Source0-to-Dest0 declaration; Event22 row 2 independently maps that source to the physical vertex stream.");
            }
            if (technique.Flags != MaterialTechniqueFlags.None ||
                pass.PerPrimArgCount != 1 ||
                pass.PerObjArgCount != 1 ||
                pass.StableArgCount != 0 ||
                pass.CustomSamplerFlags != MaterialCustomSamplerFlags.None ||
                pass.PrecompiledVertexShader !=
                    MaterialPrecompiledVertexShader.ModelUnlit)
            {
                return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                    MapRenderSunShadowCasterMaterialFailureKind
                        .ShaderArgumentContractMismatch,
                    "build_shadowmap_model_nc requires flags 0, argument counts 1/1/0, sampler flags 0, and the ModelUnlit precompiled vertex-shader selector.");
            }
            if (!string.Equals(
                    NormalizeName(sources.VertexProgram.Name),
                    StaticModelVertexShaderName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    NormalizeName(sources.PixelProgram.Name),
                    StaticModelPixelShaderName,
                    StringComparison.Ordinal))
            {
                return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                    MapRenderSunShadowCasterMaterialFailureKind
                        .ShaderProgramContractMismatch,
                    $"build_shadowmap_model_nc requires transform_only.hlsl and null.hlsl; resolved '{sources.VertexProgram.Name}'/'{sources.PixelProgram.Name}'.");
            }
            if (sources.Arguments.Count != 2 ||
                !MatchesCodeConstant(
                    sources.Arguments[0],
                    destination: 4,
                    StaticModelWorldMatrixCodeConstant) ||
                !MatchesCodeConstant(
                    sources.Arguments[1],
                    destination: 0,
                    StaticModelViewProjectionCodeConstant))
            {
                return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                    MapRenderSunShadowCasterMaterialFailureKind
                        .ShaderArgumentContractMismatch,
                    "build_shadowmap_model_nc requires World0 at dest 4 followed by ViewProjection at dest 0.");
            }
            if (state.LoadBits0 != 0x00128812u ||
                state.LoadBits1 != 0x0000003Du ||
                state.CommandWordCount != 0 ||
                state.ColorMask != RsxColorMask.None ||
                state.AlphaTestEnabled ||
                !state.CullEnabled ||
                state.CullFace != RsxCullFace.Front ||
                state.BlendEnabled ||
                !state.DepthTestEnabled ||
                !state.DepthWriteEnabled ||
                state.DepthFunc != RsxCompareFunction.LessThanOrEqual ||
                state.PolygonOffsetMode == RenderPolygonOffsetMode.Explicit ||
                state.StencilEnabled)
            {
                return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                    MapRenderSunShadowCasterMaterialFailureKind
                        .StateContractMismatch,
                    "build_shadowmap_model_nc does not retain the required 0x00128812/0x0000003D model-caster state.");
            }

            return MapRenderSunShadowCasterMaterialPlanResult.Succeeded(
                new MapRenderSunShadowOpaqueCasterMaterialPlan(
                    material,
                    techniqueSet,
                    technique,
                    pass,
                    sources,
                    state));
        }

        if (sources.VertexDeclaration is null)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .VertexDeclarationUnavailable,
                "The exact slot-2 vertex declaration is unavailable.");
        }
        if (string.Equals(
                techniqueName,
                OpaqueTechniqueName,
                StringComparison.Ordinal) ||
            string.Equals(
                techniqueName,
                WorldOpaqueNoColorTechniqueName,
                StringComparison.Ordinal))
        {
            if (technique.Flags != MaterialTechniqueFlags.None ||
                pass.PerPrimArgCount != 1 ||
                pass.PerObjArgCount != 1 ||
                pass.StableArgCount != 0 ||
                pass.CustomSamplerFlags != MaterialCustomSamplerFlags.None ||
                pass.PrecompiledVertexShader !=
                    MaterialPrecompiledVertexShader.ModelUnlit ||
                sources.Arguments.Count != 2 ||
                !MatchesCodeConstant(
                    sources.Arguments[0],
                    destination: 4,
                    raw: StaticModelWorldMatrixCodeConstant) ||
                !MatchesCodeConstant(
                    sources.Arguments[1],
                    destination: 0,
                    raw: StaticModelViewProjectionCodeConstant))
            {
                return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                    MapRenderSunShadowCasterMaterialFailureKind
                        .ShaderArgumentContractMismatch,
                    $"{techniqueName} requires World0 and ViewProjection with counts 1/1/0, flags 0, sampler flags 0, and the ModelUnlit precompiled vertex-shader selector.");
            }
            if (!MatchesOpaqueDeclaration(sources.VertexDeclaration) ||
                !string.Equals(
                    NormalizeName(sources.VertexProgram.Name),
                    StaticModelVertexShaderName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    NormalizeName(sources.PixelProgram.Name),
                    StaticModelPixelShaderName,
                    StringComparison.Ordinal))
            {
                return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                    MapRenderSunShadowCasterMaterialFailureKind
                        .ShaderProgramContractMismatch,
                    $"{techniqueName} requires Source0-to-Dest0 with transform_only.hlsl/null.hlsl.");
            }
            if (state.LoadBits0 != 0x00124812u ||
                state.LoadBits1 != 0x0000003Du ||
                state.CommandWordCount != 0 ||
                state.ColorMask != RsxColorMask.None ||
                state.AlphaTestEnabled ||
                state.CullEnabled ||
                state.BlendEnabled ||
                !state.DepthTestEnabled ||
                !state.DepthWriteEnabled ||
                state.DepthFunc != RsxCompareFunction.LessThanOrEqual ||
                state.PolygonOffsetMode == RenderPolygonOffsetMode.Explicit ||
                state.StencilEnabled)
            {
                return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                    MapRenderSunShadowCasterMaterialFailureKind
                        .StateContractMismatch,
                    $"{techniqueName} does not retain the required 0x00124812/0x0000003D world-caster state.");
            }

            return MapRenderSunShadowCasterMaterialPlanResult.Succeeded(
                new MapRenderSunShadowOpaqueCasterMaterialPlan(
                    material,
                    techniqueSet,
                    technique,
                    pass,
                    sources,
                    state));
        }

        bool worldCutout = TryGetWorldCutoutContract(
            techniqueName,
            out bool worldUsesColorRoute);
        bool staticModelCutout = TryGetStaticModelCutoutContract(
            techniqueName,
            out bool usesColorRoute,
            out uint[] acceptedLoadBits0);
        if (worldCutout)
        {
            usesColorRoute = worldUsesColorRoute;
            acceptedLoadBits0 = [0x00127012u];
        }
        if (!worldCutout && !staticModelCutout)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .TechniqueNameUnsupported,
                $"Technique slot 2 name '{techniqueName}' is not a supported sun-shadow caster technique.");
        }

        int samplerArgumentIndex;
        MaterialShaderArgumentAsset samplerArgument;
        {
            if (technique.Flags !=
                    MaterialTechniqueFlags.DeclarationHasOptionalSource ||
                pass.PerPrimArgCount != 1 ||
                pass.PerObjArgCount != 1 ||
                pass.StableArgCount != 1 ||
                pass.CustomSamplerFlags != MaterialCustomSamplerFlags.None ||
                pass.PrecompiledVertexShader !=
                    MaterialPrecompiledVertexShader.ModelUnlit)
            {
                return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                    MapRenderSunShadowCasterMaterialFailureKind
                        .ShaderArgumentContractMismatch,
                    $"{techniqueName} requires flags 0x0008, argument counts 1/1/1, sampler flags 0, and the ModelUnlit precompiled vertex-shader selector.");
            }

            string expectedShader = usesColorRoute
                ? StaticModelCutoutColorShaderName
                : StaticModelCutoutNoColorShaderName;
            if (!string.Equals(
                    NormalizeName(sources.VertexProgram.Name),
                    expectedShader,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    NormalizeName(sources.PixelProgram.Name),
                    expectedShader,
                    StringComparison.Ordinal))
            {
                return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                    MapRenderSunShadowCasterMaterialFailureKind
                        .ShaderProgramContractMismatch,
                    $"{techniqueName} requires the exact {expectedShader} vertex/pixel pair.");
            }

            if (!MatchesStaticModelCutoutDeclaration(
                    sources.VertexDeclaration,
                    usesColorRoute))
            {
                return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                    MapRenderSunShadowCasterMaterialFailureKind
                        .CutoutRouteUnavailable,
                    $"{techniqueName} does not retain its exact position/{(usesColorRoute ? "color/" : string.Empty)}UV declaration contract.");
            }
            if (sources.Arguments.Count != 3 ||
                !MatchesCodeConstant(
                    sources.Arguments[0],
                    destination: 4,
                    raw: StaticModelWorldMatrixCodeConstant) ||
                !MatchesCodeConstant(
                    sources.Arguments[1],
                    destination: 0,
                    raw: StaticModelViewProjectionCodeConstant))
            {
                return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                    MapRenderSunShadowCasterMaterialFailureKind
                        .ShaderArgumentContractMismatch,
                    $"{techniqueName} requires World0, ViewProjection, then the color sampler.");
            }
            if (!acceptedLoadBits0.Contains(state.LoadBits0) ||
                state.LoadBits1 != 0x0000003Du ||
                state.CommandWordCount != 0 ||
                state.ColorMask != RsxColorMask.None ||
                state.BlendEnabled ||
                !state.DepthTestEnabled ||
                !state.DepthWriteEnabled ||
                state.DepthFunc != RsxCompareFunction.LessThanOrEqual ||
                state.PolygonOffsetMode == RenderPolygonOffsetMode.Explicit ||
                state.StencilEnabled)
            {
                return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                    MapRenderSunShadowCasterMaterialFailureKind
                        .StateContractMismatch,
                    $"{techniqueName} does not retain a supported caster state signature.");
            }

            samplerArgumentIndex = 2;
            samplerArgument = sources.Arguments[samplerArgumentIndex];
        }

        if (
            samplerArgument.Type != MaterialShaderArgumentType.MaterialPixelSampler ||
            samplerArgument.Dest != CutoutSamplerDestination ||
            samplerArgument.MaterialNameHash !=
                CutoutColorSamplerHash)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .ShaderArgumentContractMismatch,
                $"{techniqueName} requires material pixel sampler dest 0/hash 0xA0AB1041.");
        }
        if (!state.AlphaTestEnabled ||
            state.AlphaFunc != CutoutAlphaFunction ||
            state.AlphaRef != CutoutAlphaReference)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .StateContractMismatch,
                $"{techniqueName} requires authored GEQUAL (0x0206), reference 0x80 alpha state.");
        }
        if (!sources.VertexDeclaration.Routing
                .Take(sources.VertexDeclaration.StreamCount)
                .Any(route => route.Source == CutoutEngineRouteSource))
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .CutoutRouteUnavailable,
                "The exact cutout vertex declaration does not expose engine route source 0x02.");
        }

        List<(int Ordinal, MaterialTextureDef Texture)> matchingTextures = [];
        for (int ordinal = 0; ordinal < material.Textures.Count; ordinal++)
        {
            MaterialTextureDef texture = material.Textures[ordinal];
            if (texture.NameHash == CutoutColorSamplerHash &&
                texture.Semantic == ColorTextureSemantic)
            {
                matchingTextures.Add((ordinal, texture));
            }
        }
        if (matchingTextures.Count == 0)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .CutoutTextureUnavailable,
                "The exact sampler hash/semantic 0xA0AB1041/0x02 texture row is unavailable.");
        }
        if (matchingTextures.Count != 1)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .CutoutTextureAmbiguous,
                $"The exact cutout sampler identity matches {matchingTextures.Count} material texture rows.");
        }

        (int textureOrdinal, MaterialTextureDef textureRow) = matchingTextures[0];
        GfxImageAsset? image = resolveImage(textureRow);
        if (image is null)
        {
            return MapRenderSunShadowCasterMaterialPlanResult.Failed(
                MapRenderSunShadowCasterMaterialFailureKind
                    .CutoutImageUnavailable,
                "The exact cutout texture row has no resolved GfxImage identity.");
        }

        RsxSamplerState decodedSampler = RsxSamplerDecoder.Decode(
            textureRow.SamplerState,
            image.MinLodControl,
            image.UseSrgbReads);
        var sampler = new MapRenderSunShadowCutoutSamplerBinding(
            ArgumentIndex: samplerArgumentIndex,
            Destination: samplerArgument.Dest,
            NameHash: samplerArgument.MaterialNameHash,
            TextureSemantic: textureRow.Semantic,
            EngineRouteSource: CutoutEngineRouteSource,
            TextureTableOrdinal: textureOrdinal,
            Texture: textureRow,
            Image: image,
            RawSamplerState: textureRow.SamplerState,
            DecodedSamplerState: decodedSampler);
        return MapRenderSunShadowCasterMaterialPlanResult.Succeeded(
            new MapRenderSunShadowCutoutCasterMaterialPlan(
                material,
                techniqueSet,
                technique,
                pass,
                sources,
                state,
                sampler,
                usesColorRoute));
    }

    private static string NormalizeName(string? name)
    {
        ReadOnlySpan<char> normalized = name.AsSpan().Trim();
        if (!normalized.IsEmpty && normalized[0] == ',')
            normalized = normalized[1..];
        return normalized.ToString();
    }

    private static bool MatchesCodeConstant(
        MaterialShaderArgumentAsset argument,
        ushort destination,
        uint raw) =>
        argument.Type == MaterialShaderArgumentType.CodeVertexConst &&
        argument.Dest == destination &&
        argument.CodeConstant.PackedValue == raw;

    private static bool MatchesOpaqueDeclaration(
        MaterialVertexDeclarationAsset declaration) =>
        declaration.StreamCount == 1 &&
        !declaration.HasOptionalSource &&
        declaration.Routing.Count >= 1 &&
        declaration.Routing[0] ==
            new MaterialVertexStreamRouting(
                MaterialStreamSource.Position,
                MaterialStreamDestination.Position);

    private static bool TryGetWorldCutoutContract(
        string techniqueName,
        out bool usesColorRoute)
    {
        usesColorRoute = false;
        if (string.Equals(
                techniqueName,
                CutoutTechniqueName,
                StringComparison.Ordinal))
        {
            return true;
        }
        if (string.Equals(
                techniqueName,
                WorldCutoutColorTechniqueName,
                StringComparison.Ordinal))
        {
            usesColorRoute = true;
            return true;
        }

        return false;
    }

    private static bool TryGetStaticModelCutoutContract(
        string techniqueName,
        out bool usesColorRoute,
        out uint[] acceptedLoadBits0)
    {
        usesColorRoute = false;
        acceptedLoadBits0 = [];
        switch (techniqueName)
        {
            case StaticModelCutoutATestGcTechniqueName:
            case StaticModelCutoutTestTechniqueName:
                usesColorRoute = true;
                acceptedLoadBits0 = [0x00127012u];
                return true;
            case StaticModelCutoutTestGcTechniqueName:
                usesColorRoute = true;
                acceptedLoadBits0 = [0x0012B012u];
                return true;
            case StaticModelCutoutATestNcTechniqueName:
                acceptedLoadBits0 = [0x00127012u];
                return true;
            case StaticModelCutoutTestNcTechniqueName:
                acceptedLoadBits0 = [0x00127012u, 0x0012B012u];
                return true;
            default:
                return false;
        }
    }

    private static bool MatchesStaticModelCutoutDeclaration(
        MaterialVertexDeclarationAsset declaration,
        bool usesColorRoute)
    {
        int expectedCount = usesColorRoute ? 3 : 2;
        if (declaration.StreamCount != expectedCount ||
            !declaration.HasOptionalSource ||
            declaration.Routing.Count < expectedCount)
        {
            return false;
        }

        if (declaration.Routing[0] !=
            new MaterialVertexStreamRouting(
                MaterialStreamSource.Position,
                MaterialStreamDestination.Position))
        {
            return false;
        }
        if (usesColorRoute &&
            declaration.Routing[1] !=
                new MaterialVertexStreamRouting(
                    MaterialStreamSource.Color,
                    MaterialStreamDestination.Color0))
        {
            return false;
        }

        int uvIndex = usesColorRoute ? 2 : 1;
        return declaration.Routing[uvIndex] ==
            new MaterialVertexStreamRouting(
                MaterialStreamSource.TexCoord0,
                MaterialStreamDestination.TexCoord0);
    }

    private static SelectedPassProgramSources
        CreateFixedStaticModelSources(MaterialPassAsset pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        var vertex = new ShaderProgramResolution(
            pass.VertexShaderPointer.Untyped,
            MaterialShaderKind.Vertex,
            pass.VertexShader,
            ShaderProgramResolutionKind
                .HydratedObjectWithoutActiveProvider,
            providerIdentity: null);
        var pixel = new ShaderProgramResolution(
            pass.PixelShaderPointer.Untyped,
            MaterialShaderKind.Pixel,
            pass.PixelShader,
            ShaderProgramResolutionKind
                .HydratedObjectWithoutActiveProvider,
            providerIdentity: null);
        int expected = pass.PerPrimArgCount + pass.PerObjArgCount +
            pass.StableArgCount;
        return new SelectedPassProgramSources(
            pass.VertexDeclaration,
            vertex,
            pixel,
            Array.AsReadOnly(pass.Args.ToArray()),
            expected);
    }

}

/// <summary>
/// Selects the material-sorted-index source used by the static-model sun-shadow
/// list builder.
/// </summary>
public enum MapRenderSunShadowStaticMaterialExecutionPath : byte
{
    Ineligible = 0,

    /// <summary>
    /// Uses the builder's fixed m/shadowcaster key; baseTechType 2 then selects
    /// build_shadowmap_model_nc.
    /// </summary>
    SharedCasterUsingModelShadowCasterKey = 1,

    /// <summary>
    /// Extracts the source material's sorted index and defers it for sorting
    /// before grouped emission.
    /// </summary>
    AuthoredCasterUsingSourceMaterialSortKey = 2
}

/// <summary>
/// Retains the two native route bits. Static sun-shadow material eligibility is
/// exactly the nonzero result of MaterialInfo.gameFlags &amp; 0xC0.
/// </summary>
public readonly record struct MapRenderSunShadowStaticMaterialEligibility(
    MaterialGameFlags GameFlags,
    MaterialGameFlags RouteBits)
{
    public bool IsEligible => RouteBits != 0;

    /// <summary>
    /// Bit 0x40 has priority when both route bits are set.
    /// </summary>
    public MapRenderSunShadowStaticMaterialExecutionPath ExecutionPath =>
        (RouteBits & MaterialGameFlags.CastsShadow) != 0
            ? MapRenderSunShadowStaticMaterialExecutionPath
                .SharedCasterUsingModelShadowCasterKey
            : (RouteBits & MaterialGameFlags.MaterialSpecificShadowCaster) != 0
                ? MapRenderSunShadowStaticMaterialExecutionPath
                    .AuthoredCasterUsingSourceMaterialSortKey
                : MapRenderSunShadowStaticMaterialExecutionPath.Ineligible;
}

public static class MapRenderSunShadowStaticMaterialEligibilityClassifier
{
    public const MaterialGameFlags RouteMask =
        MaterialGameFlags.ShadowCasterRouteMask;

    public static MapRenderSunShadowStaticMaterialEligibility Classify(
        MaterialAsset material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return Classify(material.Info);
    }

    public static MapRenderSunShadowStaticMaterialEligibility Classify(
        MaterialInfo materialInfo)
    {
        ArgumentNullException.ThrowIfNull(materialInfo);
        MaterialGameFlags routeBits = materialInfo.GameFlags & RouteMask;
        return new MapRenderSunShadowStaticMaterialEligibility(
            materialInfo.GameFlags,
            routeBits);
    }

    public static bool IsEligible(MaterialAsset material) =>
        Classify(material).IsEligible;
}
