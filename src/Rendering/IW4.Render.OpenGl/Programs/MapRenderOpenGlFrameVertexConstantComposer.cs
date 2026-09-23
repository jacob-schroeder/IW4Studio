using IW4.Render.Execution;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Replaces only normal-camera vertex constants whose values are invariant for
/// the complete map frame with rows from the map frame uniform block. The
/// source rewrite is deliberately destination-specific: every remaining RSX
/// constant keeps the established per-draw uniform path.
/// </summary>
internal static class MapRenderOpenGlFrameVertexConstantComposer
{
    public const string BlockName = "RsxMapFrameVertexConstants";
    public const uint BindingPoint = 0;

    private const string MainMarker = "void main() {";

    public static bool TryCompose(
        string source,
        TranslatedProgramVertexConstantBindingPlan plan,
        bool includesStaticModelBridge,
        out string composed,
        out string cacheIdentity,
        out string blocker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(plan);

        composed = string.Empty;
        cacheIdentity = string.Empty;
        blocker = string.Empty;

        TranslatedProgramVertexConstantBinding[] bindings = plan.Bindings
            .Where(binding => IsFrameGlobal(binding, includesStaticModelBridge))
            .OrderBy(binding => binding.Destination)
            .ToArray();
        if (bindings.Length == 0 && !includesStaticModelBridge)
        {
            composed = source;
            cacheIdentity = "none";
            return true;
        }

        string rewritten = source;
        foreach (TranslatedProgramVertexConstantBinding binding in bindings)
        {
            string token = $"rsxVertexConst[{binding.Destination}]";
            if (!rewritten.Contains(token, StringComparison.Ordinal))
            {
                blocker =
                    $"MAP_FRAME_VERTEX_CONST_C{binding.Destination}_READ_MISSING";
                return false;
            }

            rewritten = rewritten.Replace(
                token,
                ResolveExpression(binding),
                StringComparison.Ordinal);
        }

        int mainOffset = rewritten.IndexOf(MainMarker, StringComparison.Ordinal);
        if (mainOffset < 0)
        {
            blocker = "MAP_FRAME_VERTEX_MAIN_MISSING";
            return false;
        }

        composed = string.Concat(
            rewritten.AsSpan(0, mainOffset),
            UniformBlock,
            rewritten.AsSpan(mainOffset));
        cacheIdentity = string.Join(
            ',',
            bindings.Select(binding =>
                $"c{binding.Destination}={Describe(binding)}")
            .Append(includesStaticModelBridge ? "staticBridge" : "map"));
        return true;
    }

    public static IReadOnlySet<int> ResolveExternallyBoundDestinations(
        TranslatedProgramVertexConstantBindingPlan plan,
        bool includesStaticModelBridge)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.Bindings
            .Where(binding => IsFrameGlobal(binding, includesStaticModelBridge))
            .Select(binding => (int)binding.Destination)
            .ToHashSet();
    }

    public static int MatrixRowIndex(
        CodeMatrixSemantic semantic,
        CodeMatrixTransform transform,
        int row)
    {
        if (row is < 0 or > 3 ||
            !Enum.IsDefined(transform))
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        int semanticIndex = semantic switch
        {
            CodeMatrixSemantic.View => 0,
            CodeMatrixSemantic.Projection => 1,
            CodeMatrixSemantic.ViewProjection => 2,
            CodeMatrixSemantic.World0 => 3,
            CodeMatrixSemantic.WorldView0 => 4,
            CodeMatrixSemantic.WorldViewProjection0 => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(semantic))
        };
        return ((semanticIndex * 4 + (int)transform) * 4) + row;
    }

    public static bool IsFrameGlobal(
        TranslatedProgramVertexConstantBinding binding,
        bool includesStaticModelBridge)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.Kind ==
            TranslatedProgramVertexConstantBindingKind.DerivedMatrixRow &&
            binding.CodeMatrixSemantic is { } semantic)
        {
            return (semantic is CodeMatrixSemantic.View or
                CodeMatrixSemantic.Projection or
                CodeMatrixSemantic.ViewProjection or
                CodeMatrixSemantic.World0 or
                CodeMatrixSemantic.WorldView0 or
                CodeMatrixSemantic.WorldViewProjection0) &&
                (!includesStaticModelBridge || semantic is not
                    CodeMatrixSemantic.World0 and not
                    CodeMatrixSemantic.WorldView0 and not
                    CodeMatrixSemantic.WorldViewProjection0);
        }

        return binding.Kind is
            TranslatedProgramVertexConstantBindingKind.DynamicGameTime or
            TranslatedProgramVertexConstantBindingKind.DynamicClipSpaceLookup or
            TranslatedProgramVertexConstantBindingKind.DynamicZNear;
    }

    private static string ResolveExpression(
        TranslatedProgramVertexConstantBinding binding)
    {
        if (binding.Kind ==
            TranslatedProgramVertexConstantBindingKind.DerivedMatrixRow &&
            binding.CodeMatrixSemantic is { } semantic)
        {
            return $"rsxMapFrameMatrixRows[{MatrixRowIndex(semantic, binding.CodeMatrixTransform, binding.CodeMatrixRow)}]";
        }

        return binding.Kind switch
        {
            TranslatedProgramVertexConstantBindingKind.DynamicGameTime =>
                "rsxMapFrameGameTime",
            TranslatedProgramVertexConstantBindingKind.DynamicClipSpaceLookup
                when binding.DynamicCodeConstantSourceRow ==
                    FrameDirectCodeConstants.ClipSpaceLookupScaleRowIndex =>
                "rsxMapFrameClipSpaceLookupScale",
            TranslatedProgramVertexConstantBindingKind.DynamicClipSpaceLookup
                when binding.DynamicCodeConstantSourceRow ==
                    FrameDirectCodeConstants.ClipSpaceLookupOffsetRowIndex =>
                "rsxMapFrameClipSpaceLookupOffset",
            TranslatedProgramVertexConstantBindingKind.DynamicZNear =>
                "rsxMapFrameZNear",
            _ => throw new ArgumentOutOfRangeException(nameof(binding))
        };
    }

    private static string Describe(
        TranslatedProgramVertexConstantBinding binding) =>
        binding.Kind ==
        TranslatedProgramVertexConstantBindingKind.DerivedMatrixRow
            ? $"{binding.CodeMatrixSemantic}:{binding.CodeMatrixTransform}:r{binding.CodeMatrixRow}"
            : binding.Kind.ToString();

    private const string UniformBlock = """
        layout(std140) uniform RsxMapFrameVertexConstants
        {
          vec4 rsxMapFrameMatrixRows[96];
          vec4 rsxMapFrameGameTime;
          vec4 rsxMapFrameClipSpaceLookupScale;
          vec4 rsxMapFrameClipSpaceLookupOffset;
          vec4 rsxMapFrameZNear;
          vec4 rsxMapFrameEyeOffset;
          vec4 rsxMapFrameVegetationTime;
        };

        """;
}
