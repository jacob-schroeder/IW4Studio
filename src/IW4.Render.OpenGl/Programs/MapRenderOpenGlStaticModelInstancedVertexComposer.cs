using System.Text;

using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Specializes one translated RSX vertex program for the native Event22
/// static-model placement path. Locations 13..15 carry the compacted host
/// placement rows; the generated bridge reconstructs the exact native
/// World0/WorldView0/WorldViewProjection0 rows that the PS3 backend would
/// otherwise upload once per isolated placement.
/// </summary>
internal static class MapRenderOpenGlStaticModelInstancedVertexComposer
{
    public const uint LightingPayloadAttribute = 12;
    public const uint FirstPlacementAttribute = 13;
    public const int PlacementAttributeCount = 3;

    public const string ViewRowsUniform = "rsxStaticViewRows";
    public const string ViewProjectionRowsUniform =
        "rsxStaticViewProjectionRows";
    public const string EyeOffsetUniform = "rsxStaticEyeOffset";
    public const string CompositionWindEnabledUniform =
        "rsxStaticCompositionWindEnabled";
    public const string CompositionTimeUniform =
        "rsxStaticCompositionTime";
    public const string CompositionAmplitudeUniform =
        "rsxStaticCompositionAmplitude";
    public const string CompositionAngularFrequencyUniform =
        "rsxStaticCompositionAngularFrequency";
    public const string CompositionSpatialFrequencyUniform =
        "rsxStaticCompositionSpatialFrequency";
    public const string CompositionLocalMinimumHeightUniform =
        "rsxStaticCompositionLocalMinimumHeight";
    public const string CompositionLocalHeightRangeUniform =
        "rsxStaticCompositionLocalHeightRange";

    private const string MainMarker = "void main() {";

    public static bool TryCompose(
        string source,
        IReadOnlyList<ShaderVertexInputBinding> vertexInputs,
        TranslatedProgramVertexConstantBindingPlan plan,
        out string composed,
        out string cacheIdentity,
        out string blocker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(vertexInputs);
        ArgumentNullException.ThrowIfNull(plan);

        composed = string.Empty;
        cacheIdentity = string.Empty;
        blocker = string.Empty;

        if (!TryResolveLightingPayload(plan, out _, out blocker))
            return false;

        for (int index = 0; index < vertexInputs.Count; index++)
        {
            byte destination = vertexInputs[index].Destination;
            if (destination == LightingPayloadAttribute)
            {
                blocker =
                    "STATIC_INSTANCE_ATTRIBUTE_DEST12_IS_AUTHORED_MODEL_LIGHTING_COLLISION";
                return false;
            }
            if (destination >= FirstPlacementAttribute &&
                destination < FirstPlacementAttribute +
                    PlacementAttributeCount)
            {
                blocker =
                    $"STATIC_INSTANCE_ATTRIBUTE_DEST{destination}_IS_AUTHORED";
                return false;
            }
        }

        TranslatedProgramVertexConstantBinding[] bindings =
            plan.Bindings
                .Where(binding => IsPlacementDependent(
                    binding.CodeMatrixSemantic))
                .OrderBy(binding => binding.Destination)
                .ToArray();
        if (bindings.Length == 0)
        {
            blocker = "STATIC_INSTANCE_WORLD_MATRIX_BINDING_MISSING";
            return false;
        }

        string rewritten = source;
        TranslatedProgramVertexConstantBinding[]
            lightingPayloadBindings = plan.Bindings
                .Where(binding => IsLightingPayloadBinding(binding.Kind))
                .ToArray();
        foreach (TranslatedProgramVertexConstantBinding
                 binding in lightingPayloadBindings)
        {
            string token = $"rsxVertexConst[{binding.Destination}]";
            if (!rewritten.Contains(token, StringComparison.Ordinal))
            {
                blocker =
                    $"STATIC_INSTANCE_VERTEX_CONST_C{binding.Destination}_" +
                    (binding.Kind ==
                        TranslatedProgramVertexConstantBindingKind
                            .PerInstanceStaticModelBaseLightingCoords
                        ? "ROW39_READ_MISSING"
                        : "ROW3A_READ_MISSING");
                return false;
            }
            rewritten = rewritten.Replace(
                token,
                "aRsxInput12",
                StringComparison.Ordinal);
        }
        foreach (TranslatedProgramVertexConstantBinding
                 binding in bindings)
        {
            string token = $"rsxVertexConst[{binding.Destination}]";
            if (!rewritten.Contains(token, StringComparison.Ordinal))
            {
                blocker =
                    $"STATIC_INSTANCE_VERTEX_CONST_C{binding.Destination}_READ_MISSING";
                return false;
            }

            string expression =
                $"rsxStaticDerivedConst({(int)binding.CodeMatrixSemantic!.Value}, " +
                $"{(int)binding.CodeMatrixTransform}, {binding.CodeMatrixRow})";
            rewritten = rewritten.Replace(
                token,
                expression,
                StringComparison.Ordinal);
        }

        int mainOffset = rewritten.IndexOf(
            MainMarker,
            StringComparison.Ordinal);
        if (mainOffset < 0)
        {
            blocker = "STATIC_INSTANCE_VERTEX_MAIN_MISSING";
            return false;
        }

        var builder = new StringBuilder(
            rewritten.Length + StaticPlacementBridge.Length + 256);
        builder.Append(rewritten, 0, mainOffset);
        builder.Append(StaticPlacementBridge);
        builder.Append(rewritten, mainOffset, rewritten.Length - mainOffset);
        composed = builder.ToString();
        cacheIdentity = string.Join(
            ',',
            bindings.Select(binding =>
                $"c{binding.Destination}=" +
                $"{binding.CodeMatrixSemantic}:" +
                $"{binding.CodeMatrixTransform}:" +
                $"r{binding.CodeMatrixRow}")
                .Concat(lightingPayloadBindings.Select(binding =>
                    $"c{binding.Destination}=" +
                    (binding.Kind ==
                        TranslatedProgramVertexConstantBindingKind
                            .PerInstanceStaticModelBaseLightingCoords
                        ? "instanceRow39"
                        : "instanceRow3A"))));
        return true;
    }

    public static bool TryResolveLightingPayload(
        TranslatedProgramVertexConstantBindingPlan plan,
        out MapRenderStaticInstanceLightingPayload lightingPayload,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(plan);
        bool usesBaseLightingCoords = plan.Bindings.Any(binding =>
            binding.Kind ==
            TranslatedProgramVertexConstantBindingKind
                .PerInstanceStaticModelBaseLightingCoords);
        bool usesLightProbeAmbient = plan.Bindings.Any(binding =>
            binding.Kind ==
            TranslatedProgramVertexConstantBindingKind
                .PerInstanceStaticModelLightProbeAmbient);
        if (usesBaseLightingCoords && usesLightProbeAmbient)
        {
            lightingPayload = MapRenderStaticInstanceLightingPayload.None;
            blocker =
                "STATIC_INSTANCE_LIGHTING_PAYLOAD_CONFLICT_ROW39_AND_ROW3A";
            return false;
        }

        lightingPayload = usesBaseLightingCoords
            ? MapRenderStaticInstanceLightingPayload.BaseLightingCoords
            : usesLightProbeAmbient
                ? MapRenderStaticInstanceLightingPayload.LightProbeAmbient
                : MapRenderStaticInstanceLightingPayload.None;
        blocker = string.Empty;
        return true;
    }

    public static IReadOnlySet<int> ResolveExternallyBoundVertexConstantDestinations(
        TranslatedProgramVertexConstantBindingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.Bindings
            .Where(binding =>
                IsPlacementDependent(binding.CodeMatrixSemantic) ||
                IsLightingPayloadBinding(binding.Kind))
            .Select(binding => (int)binding.Destination)
            .ToHashSet();
    }

    private static bool IsLightingPayloadBinding(
        TranslatedProgramVertexConstantBindingKind kind) =>
        kind is
            TranslatedProgramVertexConstantBindingKind
                .PerInstanceStaticModelBaseLightingCoords or
            TranslatedProgramVertexConstantBindingKind
                .PerInstanceStaticModelLightProbeAmbient;

    private static bool IsPlacementDependent(
        CodeMatrixSemantic? semantic) => semantic is
        CodeMatrixSemantic.World0 or
        CodeMatrixSemantic.WorldView0 or
        CodeMatrixSemantic.WorldViewProjection0;

    private const string StaticPlacementBridge = """
        uniform vec4 rsxStaticViewRows[4];
        uniform vec4 rsxStaticViewProjectionRows[4];
        uniform vec3 rsxStaticEyeOffset;
        uniform int rsxStaticCompositionWindEnabled;
        uniform float rsxStaticCompositionTime;
        uniform float rsxStaticCompositionAmplitude;
        uniform float rsxStaticCompositionAngularFrequency;
        uniform float rsxStaticCompositionSpatialFrequency;
        uniform float rsxStaticCompositionLocalMinimumHeight;
        uniform float rsxStaticCompositionLocalHeightRange;

        // Bounded EditorPreview deformation shared with the generic static
        // model shader. No authored RSX wind constant is claimed here.
        float rsxStaticCompositionSway()
        {
          if (rsxStaticCompositionWindEnabled == 0 ||
              rsxStaticCompositionLocalHeightRange <= 0.0001)
          {
            return 0.0;
          }
          vec4 localPosition = vec4(aRsxInput0.xyz, 1.0);
          float heightWeight = clamp(
              (aRsxInput0.z - rsxStaticCompositionLocalMinimumHeight) /
                  rsxStaticCompositionLocalHeightRange,
              0.0,
              1.0);
          heightWeight *= heightWeight;
          float renderX = dot(aRsxInput13, localPosition);
          float renderZ = dot(aRsxInput15, localPosition);
          float phase =
              rsxStaticCompositionTime *
                  rsxStaticCompositionAngularFrequency +
              renderX * rsxStaticCompositionSpatialFrequency +
              renderZ * rsxStaticCompositionSpatialFrequency * 1.37;
          float wave = (
              sin(phase) +
              0.35 * sin(phase * 0.61 + 1.7)) / 1.35;
          return rsxStaticCompositionAmplitude * heightWeight * wave;
        }

        vec4 rsxStaticWorldRow(int row)
        {
          vec4 host0 = aRsxInput13;
          vec4 host1 = aRsxInput14;
          vec4 host2 = aRsxInput15;
          if (row == 0) return vec4(host0.x, -host2.x, host1.x, 0.0);
          if (row == 1) return vec4(host0.y, -host2.y, host1.y, 0.0);
          if (row == 2) return vec4(host0.z, -host2.z, host1.z, 0.0);
          float vegetationSway = rsxStaticCompositionSway();
          return vec4(
              host0.w - rsxStaticEyeOffset.x + vegetationSway,
              -host2.w - rsxStaticEyeOffset.y - vegetationSway * 0.35,
              host1.w - rsxStaticEyeOffset.z,
              1.0);
        }

        vec4 rsxStaticMultiplyRow(vec4 lhs, bool viewProjection)
        {
          if (viewProjection)
          {
            return lhs.x * rsxStaticViewProjectionRows[0] +
                   lhs.y * rsxStaticViewProjectionRows[1] +
                   lhs.z * rsxStaticViewProjectionRows[2] +
                   lhs.w * rsxStaticViewProjectionRows[3];
          }
          return lhs.x * rsxStaticViewRows[0] +
                 lhs.y * rsxStaticViewRows[1] +
                 lhs.z * rsxStaticViewRows[2] +
                 lhs.w * rsxStaticViewRows[3];
        }

        vec4 rsxStaticBaseRow(int semantic, int row)
        {
          vec4 world = rsxStaticWorldRow(row);
          if (semantic == 5) return world;
          if (semantic == 6) return rsxStaticMultiplyRow(world, false);
          return rsxStaticMultiplyRow(world, true);
        }

        mat4 rsxStaticBaseMatrix(int semantic)
        {
          return transpose(mat4(
              rsxStaticBaseRow(semantic, 0),
              rsxStaticBaseRow(semantic, 1),
              rsxStaticBaseRow(semantic, 2),
              rsxStaticBaseRow(semantic, 3)));
        }

        vec4 rsxStaticMatrixRow(mat4 value, int row)
        {
          return vec4(
              value[0][row], value[1][row],
              value[2][row], value[3][row]);
        }

        vec4 rsxStaticMatrixColumn(mat4 value, int column)
        {
          return value[column];
        }

        vec4 rsxStaticDerivedConst(int semantic, int transform, int row)
        {
          if (transform == 0) return rsxStaticBaseRow(semantic, row);
          mat4 value = rsxStaticBaseMatrix(semantic);
          if (transform == 2) return rsxStaticMatrixColumn(value, row);
          value = inverse(value);
          if (transform == 1) return rsxStaticMatrixRow(value, row);
          return rsxStaticMatrixColumn(value, row);
        }

        """;
}
