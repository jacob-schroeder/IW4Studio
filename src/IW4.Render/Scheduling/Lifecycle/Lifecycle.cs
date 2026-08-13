using System.Collections.ObjectModel;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Scheduling.Lifecycle;

/// <summary>
/// Compact operational recipe for the EditorPreview normal-camera targets and
/// its exact fullscreen presentation materials.
/// </summary>
public sealed class MapRenderEditorPreviewNormalCameraRecipe
{
    public const uint FullscreenCopyStateBits0 = 0x1812_8812;
    public const uint FullscreenCopyStateBits1 = 0xE00E_0002;
    public const uint GlowSetupStateBits0 = 0x5812_8812;
    public const uint GlowApplyStateBits0 = 0x192A_892A;

    private readonly MapRenderNormalCameraTargetPlan[] _targets;

    private MapRenderEditorPreviewNormalCameraRecipe(
        MapRenderSceneTargetClearPlan sceneTargetClear,
        IReadOnlyList<MapRenderNormalCameraTargetPlan> targets,
        MapRenderNormalCameraMaterialAssetContract feedbackReplace,
        MapRenderNormalCameraMaterialAssetContract postFx,
        MapRenderNormalCameraMaterialAssetContract postFxColor2,
        MapRenderNormalCameraMaterialAssetContract glowConsistentSetup,
        MapRenderNormalCameraMaterialAssetContract glowConsistentSetupColor2,
        MapRenderNormalCameraMaterialAssetContract glowApplyBloom,
        IReadOnlyList<MapRenderNormalCameraMaterialAssetContract>
            glowSymmetricFilters)
    {
        ArgumentNullException.ThrowIfNull(sceneTargetClear);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(feedbackReplace);
        ArgumentNullException.ThrowIfNull(postFx);
        ArgumentNullException.ThrowIfNull(postFxColor2);
        ArgumentNullException.ThrowIfNull(glowConsistentSetup);
        ArgumentNullException.ThrowIfNull(glowConsistentSetupColor2);
        ArgumentNullException.ThrowIfNull(glowApplyBloom);
        ArgumentNullException.ThrowIfNull(glowSymmetricFilters);

        _targets = targets.ToArray();
        MapRenderNormalCameraTargetKind[] expectedTargets =
        [
            MapRenderNormalCameraTargetKind.Scene,
            MapRenderNormalCameraTargetKind.ResolvedPostSun,
            MapRenderNormalCameraTargetKind.ResolvedScene,
            MapRenderNormalCameraTargetKind.HalfParticles
        ];
        if (!_targets.Select(target => target.Kind)
                .SequenceEqual(expectedTargets) ||
            _targets[1].InitialAliasOf !=
                MapRenderNormalCameraTargetKind.ResolvedScene ||
            _targets.Where(target => target.Kind !=
                    MapRenderNormalCameraTargetKind.ResolvedPostSun)
                .Any(target => target.InitialAliasOf is not null))
        {
            throw new ArgumentException(
                "Live Preview requires target order 2, 3, 4, 6 with target 3 initially aliasing target 4.",
                nameof(targets));
        }
        if (sceneTargetClear.TargetId !=
                (int)MapRenderNormalCameraTargetKind.Scene ||
            sceneTargetClear.SurfaceMask !=
                (MapRenderSceneClearSurfaceMask.Rgba |
                 MapRenderSceneClearSurfaceMask.Depth |
                 MapRenderSceneClearSurfaceMask.Stencil) ||
            sceneTargetClear.Depth != 1f ||
            sceneTargetClear.Stencil != 0)
        {
            throw new ArgumentException(
                "Live Preview requires the target-2 color/depth/stencil clear.",
                nameof(sceneTargetClear));
        }
        if (feedbackReplace.MaterialName != "feedbackreplace" ||
            postFx.MaterialName != "postfx" ||
            feedbackReplace.StateBits0 != FullscreenCopyStateBits0 ||
            feedbackReplace.StateBits1 != FullscreenCopyStateBits1 ||
            postFx.StateBits0 != FullscreenCopyStateBits0 ||
            postFx.StateBits1 != FullscreenCopyStateBits1 ||
            postFxColor2.MaterialName != "postfx_color2" ||
            postFxColor2.StateBits0 != FullscreenCopyStateBits0 ||
            postFxColor2.StateBits1 != FullscreenCopyStateBits1)
        {
            throw new ArgumentException(
                "Live Preview fullscreen materials no longer match their exact identities and state words.");
        }
        if (glowConsistentSetup.MaterialName !=
                "glow_consistent_setup" ||
            glowConsistentSetupColor2.MaterialName !=
                "glow_consistent_setup_color2" ||
            glowConsistentSetup.StateBits0 != GlowSetupStateBits0 ||
            glowConsistentSetupColor2.StateBits0 != GlowSetupStateBits0 ||
            glowConsistentSetup.StateBits1 != FullscreenCopyStateBits1 ||
            glowConsistentSetupColor2.StateBits1 !=
                FullscreenCopyStateBits1 ||
            glowApplyBloom.MaterialName != "glow_apply_bloom" ||
            glowApplyBloom.StateBits0 != GlowApplyStateBits0 ||
            glowApplyBloom.StateBits1 != FullscreenCopyStateBits1 ||
            glowSymmetricFilters.Count != 8 ||
            glowSymmetricFilters.Where((contract, index) =>
                    contract.MaterialName != $"filter_symmetric_{index + 1}" ||
                    contract.StateBits0 != FullscreenCopyStateBits0 ||
                    contract.StateBits1 != FullscreenCopyStateBits1)
                .Any())
        {
            throw new ArgumentException(
                "Live Preview glow materials no longer match their exact PS3 identities and state words.");
        }

        SceneTargetClear = sceneTargetClear;
        Targets = Array.AsReadOnly(_targets);
        FeedbackReplace = feedbackReplace;
        PostFx = postFx;
        PostFxColor2 = postFxColor2;
        GlowConsistentSetup = glowConsistentSetup;
        GlowConsistentSetupColor2 = glowConsistentSetupColor2;
        GlowApplyBloom = glowApplyBloom;
        GlowSymmetricFilters = Array.AsReadOnly(
            glowSymmetricFilters.ToArray());
    }

    public static MapRenderEditorPreviewNormalCameraRecipe Current { get; } =
        Create();

    public MapRenderSceneTargetClearPlan SceneTargetClear { get; }

    public ReadOnlyCollection<MapRenderNormalCameraTargetPlan> Targets
        { get; }

    public MapRenderNormalCameraMaterialAssetContract FeedbackReplace
        { get; }

    public MapRenderNormalCameraMaterialAssetContract PostFx { get; }

    public MapRenderNormalCameraMaterialAssetContract PostFxColor2 { get; }

    public MapRenderNormalCameraMaterialAssetContract GlowConsistentSetup
        { get; }

    public MapRenderNormalCameraMaterialAssetContract
        GlowConsistentSetupColor2 { get; }

    public MapRenderNormalCameraMaterialAssetContract GlowApplyBloom { get; }

    public ReadOnlyCollection<MapRenderNormalCameraMaterialAssetContract>
        GlowSymmetricFilters { get; }

    public MapRenderNormalCameraTargetPlan GetTarget(
        MapRenderNormalCameraTargetKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        return _targets.Single(target => target.Kind == kind);
    }

    private static MapRenderEditorPreviewNormalCameraRecipe Create()
    {
        MapRenderNormalCameraMaterialAssetContract feedbackReplace = Material(
            "feedbackreplace",
            "passthru_alpha",
            "passthru_alpha",
            MaterialTechniqueFlags.DeclarationHasOptionalSource,
            "textured_simple.hlsl",
            "textured_simple.hlsl",
            [
                CodeArgument(
                    MaterialShaderArgumentType.CodeVertexConst,
                    0,
                    MaterialConstantSource.WorldViewProjectionMatrix0,
                    rowCount: 4),
                TextureArgument(
                    0,
                    MaterialTextureSource.Feedback)
            ]);
        MapRenderNormalCameraMaterialAssetContract postFx = Material(
            "postfx",
            "postfx",
            "postfx",
            MaterialTechniqueFlags.NeedsResolvedScene |
                MaterialTechniqueFlags.DeclarationHasOptionalSource,
            "textured_simple.hlsl",
            "postfx.hlsl",
            [
                CodeArgument(
                    MaterialShaderArgumentType.CodeVertexConst,
                    0,
                    MaterialConstantSource.WorldViewProjectionMatrix0,
                    rowCount: 4),
                TextureArgument(0, MaterialTextureSource.ResolvedScene)
            ]);
        MapRenderNormalCameraMaterialAssetContract postFxColor2 = Material(
            "postfx_color2",
            "postfx_color2",
            "postfx_color2",
            MaterialTechniqueFlags.NeedsResolvedScene |
                MaterialTechniqueFlags.DeclarationHasOptionalSource,
            "textured_simple.hlsl",
            "postfx_color2.hlsl",
            [
                CodeArgument(
                    MaterialShaderArgumentType.CodeVertexConst,
                    0,
                    MaterialConstantSource.WorldViewProjectionMatrix0,
                    rowCount: 4),
                TextureArgument(0, MaterialTextureSource.ResolvedScene),
                CodeArgument(MaterialShaderArgumentType.CodePixelConst, 1,
                    MaterialConstantSource.ColorTintBase),
                CodeArgument(MaterialShaderArgumentType.CodePixelConst, 2,
                    MaterialConstantSource.ColorTintDelta),
                CodeArgument(MaterialShaderArgumentType.CodePixelConst, 3,
                    MaterialConstantSource.ColorTintQuadraticDelta),
                CodeArgument(MaterialShaderArgumentType.CodePixelConst, 4,
                    MaterialConstantSource.ColorBias)
            ]);
        MapRenderNormalCameraMaterialAssetContract glowConsistentSetup =
            Material(
                "glow_consistent_setup",
                "glow_consistent_setup",
                "glow_consistent_setup",
                MaterialTechniqueFlags.NeedsResolvedScene |
                    MaterialTechniqueFlags.DeclarationHasOptionalSource,
                "glow_consistent_setup.hlsl",
                "glow_consistent_setup.hlsl",
                [
                    CodeArgument(
                        MaterialShaderArgumentType.CodeVertexConst,
                        0,
                        MaterialConstantSource.WorldViewProjectionMatrix0,
                        rowCount: 4),
                    CodeArgument(
                        MaterialShaderArgumentType.CodeVertexConst,
                        16,
                        MaterialConstantSource.RenderTargetSize),
                    TextureArgument(
                        0,
                        MaterialTextureSource.ResolvedScene),
                    CodeArgument(
                        MaterialShaderArgumentType.CodePixelConst,
                        4,
                        MaterialConstantSource.GlowSetup),
                    CodeArgument(
                        MaterialShaderArgumentType.CodePixelConst,
                        5,
                        MaterialConstantSource.ColorTintBase),
                    CodeArgument(
                        MaterialShaderArgumentType.CodePixelConst,
                        6,
                        MaterialConstantSource.ColorTintDelta),
                    CodeArgument(
                        MaterialShaderArgumentType.CodePixelConst,
                        7,
                        MaterialConstantSource.ColorBias)
                ],
                GlowSetupStateBits0);
        MapRenderNormalCameraMaterialAssetContract glowConsistentSetupColor2 =
            Material(
                "glow_consistent_setup_color2",
                "glow_consistent_setup_color2",
                "glow_consistent_setup_color2",
                MaterialTechniqueFlags.NeedsResolvedScene |
                    MaterialTechniqueFlags.DeclarationHasOptionalSource,
                "glow_consistent_setup.hlsl",
                "glow_consistent_setup_color2.hlsl",
                [
                    CodeArgument(
                        MaterialShaderArgumentType.CodeVertexConst,
                        0,
                        MaterialConstantSource.WorldViewProjectionMatrix0,
                        rowCount: 4),
                    CodeArgument(
                        MaterialShaderArgumentType.CodeVertexConst,
                        16,
                        MaterialConstantSource.RenderTargetSize),
                    TextureArgument(
                        0,
                        MaterialTextureSource.ResolvedScene),
                    CodeArgument(
                        MaterialShaderArgumentType.CodePixelConst,
                        4,
                        MaterialConstantSource.GlowSetup),
                    CodeArgument(
                        MaterialShaderArgumentType.CodePixelConst,
                        5,
                        MaterialConstantSource.ColorTintBase),
                    CodeArgument(
                        MaterialShaderArgumentType.CodePixelConst,
                        6,
                        MaterialConstantSource.ColorTintDelta),
                    CodeArgument(
                        MaterialShaderArgumentType.CodePixelConst,
                        7,
                        MaterialConstantSource.ColorTintQuadraticDelta),
                    CodeArgument(
                        MaterialShaderArgumentType.CodePixelConst,
                        8,
                        MaterialConstantSource.ColorBias)
                ],
                GlowSetupStateBits0);
        MapRenderNormalCameraMaterialAssetContract glowApplyBloom = Material(
            "glow_apply_bloom",
            "glow_apply_bloom",
            "glow_apply_bloom",
            MaterialTechniqueFlags.DeclarationHasOptionalSource,
            "glow_apply_bloom.hlsl",
            "glow_apply_bloom.hlsl",
            [
                CodeArgument(
                    MaterialShaderArgumentType.CodeVertexConst,
                    0,
                    MaterialConstantSource.WorldViewProjectionMatrix0,
                    rowCount: 4),
                TextureArgument(
                    0,
                    MaterialTextureSource.Feedback),
                CodeArgument(
                    MaterialShaderArgumentType.CodePixelConst,
                    1,
                    MaterialConstantSource.GlowApply)
            ],
            GlowApplyStateBits0);
        MapRenderNormalCameraMaterialAssetContract[] glowSymmetricFilters =
            Enumerable.Range(1, 8)
                .Select(CreateGlowSymmetricFilter)
                .ToArray();

        return new MapRenderEditorPreviewNormalCameraRecipe(
            new MapRenderSceneTargetClearPlan(
                (int)MapRenderNormalCameraTargetKind.Scene,
                MapRenderSceneClearSurfaceMask.Rgba |
                MapRenderSceneClearSurfaceMask.Depth |
                MapRenderSceneClearSurfaceMask.Stencil,
                1f,
                0),
            [
                new MapRenderNormalCameraTargetPlan(
                    MapRenderNormalCameraTargetKind.Scene,
                    "R_RENDERTARGET_SCENE",
                    12,
                    MapRenderNormalCameraTargetDimensions
                        .DoubleWidthBackingDisplayLogical),
                new MapRenderNormalCameraTargetPlan(
                    MapRenderNormalCameraTargetKind.ResolvedPostSun,
                    "R_RENDERTARGET_RESOLVED_POST_SUN",
                    11,
                    MapRenderNormalCameraTargetDimensions.FullDisplay,
                    MapRenderNormalCameraTargetKind.ResolvedScene),
                new MapRenderNormalCameraTargetPlan(
                    MapRenderNormalCameraTargetKind.ResolvedScene,
                    "R_RENDERTARGET_RESOLVED_SCENE",
                    11,
                    MapRenderNormalCameraTargetDimensions.FullDisplay),
                new MapRenderNormalCameraTargetPlan(
                    MapRenderNormalCameraTargetKind.HalfParticles,
                    "R_RENDERTARGET_HALF_PARTICLES",
                    3,
                    MapRenderNormalCameraTargetDimensions
                        .HalfDisplayShiftClamp)
            ],
            feedbackReplace,
            postFx,
            postFxColor2,
            glowConsistentSetup,
            glowConsistentSetupColor2,
            glowApplyBloom,
            glowSymmetricFilters);
    }

    private static MapRenderNormalCameraMaterialAssetContract
        CreateGlowSymmetricFilter(int tapHalfCount)
    {
        int firstPixelDestination = tapHalfCount <= 4
            ? tapHalfCount * 2
            : tapHalfCount;
        var arguments = new List<MapRenderNormalCameraMaterialArgumentContract>(
            2 + tapHalfCount * 2)
        {
            CodeArgument(
                MaterialShaderArgumentType.CodeVertexConst,
                0,
                MaterialConstantSource.WorldViewProjectionMatrix0,
                rowCount: 4),
            TextureArgument(
                0,
                MaterialTextureSource.Feedback)
        };
        for (int index = 0; index < tapHalfCount; index++)
        {
            arguments.Add(CodeArgument(
                MaterialShaderArgumentType.CodeVertexConst,
                checked((ushort)(12 + index)),
                (MaterialConstantSource)(
                    (ushort)MaterialConstantSource.FilterTap0 + index)));
        }
        for (int index = 0; index < tapHalfCount; index++)
        {
            arguments.Add(CodeArgument(
                MaterialShaderArgumentType.CodePixelConst,
                checked((ushort)(firstPixelDestination + index)),
                (MaterialConstantSource)(
                    (ushort)MaterialConstantSource.FilterTap0 + index)));
        }

        string name = $"filter_symmetric_{tapHalfCount}";
        return Material(
            name,
            name,
            name,
            MaterialTechniqueFlags.DeclarationHasOptionalSource,
            $"{name}.hlsl",
            $"{name}.hlsl",
            arguments);
    }

    private static MapRenderNormalCameraMaterialAssetContract Material(
        string materialName,
        string techniqueSetName,
        string techniqueName,
        MaterialTechniqueFlags techniqueFlags,
        string vertexShaderName,
        string pixelShaderName,
        IReadOnlyList<MapRenderNormalCameraMaterialArgumentContract> arguments,
        uint stateBits0 = FullscreenCopyStateBits0)
        => new(
            materialName,
            techniqueSetName,
            techniqueName,
            techniqueSlot: (int)MaterialTechniqueType.Unlit,
            techniqueFlags,
            vertexShaderName,
            pixelShaderName,
            stateBits0,
            FullscreenCopyStateBits1,
            arguments);

    private static MapRenderNormalCameraMaterialArgumentContract CodeArgument(
        MaterialShaderArgumentType type,
        ushort destination,
        MaterialConstantSource source,
        byte firstRow = 0,
        byte rowCount = 1) => new(
            type,
            destination,
            new MaterialCodeConstantArgument(
                source,
                firstRow,
                rowCount).PackedValue);

    private static MapRenderNormalCameraMaterialArgumentContract
        TextureArgument(
            ushort destination,
            MaterialTextureSource source) => new(
                MaterialShaderArgumentType.CodePixelSampler,
                destination,
                (uint)source);
}
