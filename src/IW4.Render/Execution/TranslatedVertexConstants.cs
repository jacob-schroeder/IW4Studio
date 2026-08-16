using IW4.Render.Execution;
using IW4.Render.Shaders;

namespace IW4.Render.Execution;

/// <summary>
/// One exact draw-time binding for a vertex constant read by the selected RSX
/// microcode. Static values include compiler-owned defaults and resolved
/// direct-table rows; derived matrix rows retain their dynamic matrix source.
/// </summary>
public sealed record TranslatedProgramVertexConstantBinding(
    ushort Destination,
    TranslatedProgramVertexConstantBindingOwner Owner,
    TranslatedProgramVertexConstantBindingKind Kind,
    ShaderConstantValue? StaticValue,
    CodeMatrixSemantic? CodeMatrixSemantic,
    CodeMatrixTransform CodeMatrixTransform,
    int CodeMatrixRow,
    string OwnerIdentity,
    ushort? DynamicCodeConstantSourceRow = null);

public enum TranslatedProgramVertexConstantBindingOwner
{
    PassArgument,
    EmbeddedProgramDefault
}

public enum TranslatedProgramVertexConstantBindingKind
{
    StaticValue,
    DerivedMatrixRow,
    DynamicGameTime,
    DynamicSceneLightPosition,
    DynamicSunShadowProjection,
    DynamicClipSpaceLookup,
    DynamicZNear,
    PerInstanceStaticModelBaseLightingCoords,
    PerInstanceStaticModelLightProbeAmbient,
    DynamicSceneLightShadow
}

/// <summary>
/// Immutable, destination-keyed vertex-constant bindings for one translated
/// EditorPreview program. The plan contains exactly one binding for every
/// constant destination read by the selected microcode.
/// </summary>
public sealed class
    TranslatedProgramVertexConstantBindingPlan
{
    internal TranslatedProgramVertexConstantBindingPlan(
        IReadOnlyList<
            TranslatedProgramVertexConstantBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        TranslatedProgramVertexConstantBinding[] copied =
            bindings
                .Select(binding => binding is null
                    ? throw new ArgumentException(
                        "Vertex-constant binding plans cannot contain null bindings.",
                        nameof(bindings))
                    : binding with { })
                .ToArray();
        var destinations = new HashSet<ushort>();
        foreach (TranslatedProgramVertexConstantBinding binding
                 in copied)
        {
            Validate(binding, nameof(bindings));
            if (!destinations.Add(binding.Destination))
            {
                throw new ArgumentException(
                    $"Vertex constant c{binding.Destination} is duplicated.",
                    nameof(bindings));
            }
        }

        Bindings = Array.AsReadOnly(copied);
    }

    public IReadOnlyList<
        TranslatedProgramVertexConstantBinding> Bindings
        { get; }

    private static void Validate(
        TranslatedProgramVertexConstantBinding binding,
        string parameterName)
    {
        if (binding.Destination >
            RsxVertexConstantLayout.MaximumDestination)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                binding.Destination,
                "Vertex-constant binding destination is outside c0 through c467.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(
            binding.OwnerIdentity,
            parameterName);

        switch (binding.Kind)
        {
            case TranslatedProgramVertexConstantBindingKind
                .StaticValue:
                if (binding.StaticValue is not { } value ||
                    !IsFinite(value) ||
                    binding.DynamicCodeConstantSourceRow.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    CodeMatrixTransform.None ||
                    binding.CodeMatrixRow != -1)
                {
                    throw new ArgumentException(
                        $"Static vertex constant c{binding.Destination} has an invalid value/source shape.",
                        parameterName);
                }
                break;

            case TranslatedProgramVertexConstantBindingKind
                .DerivedMatrixRow:
                if (binding.StaticValue.HasValue ||
                    binding.DynamicCodeConstantSourceRow.HasValue ||
                    binding.CodeMatrixSemantic is not { } semantic ||
                    !DerivedMatrixResolver.Supports(semantic) ||
                    !Enum.IsDefined(binding.CodeMatrixTransform) ||
                    binding.CodeMatrixRow is < 0 or > 3)
                {
                    throw new ArgumentException(
                        $"Matrix vertex constant c{binding.Destination} has an invalid value/source shape.",
                        parameterName);
                }
                break;

            case TranslatedProgramVertexConstantBindingKind
                .DynamicGameTime:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    CodeMatrixTransform.None ||
                    binding.CodeMatrixRow != -1 ||
                    binding.DynamicCodeConstantSourceRow !=
                    FrameDirectCodeConstants
                        .GameTimeRowIndex)
                {
                    throw new ArgumentException(
                        $"Dynamic game-time vertex constant c{binding.Destination} has an invalid source shape.",
                        parameterName);
                }
                break;

            case TranslatedProgramVertexConstantBindingKind
                .DynamicSceneLightPosition:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    CodeMatrixTransform.None ||
                    binding.CodeMatrixRow != -1 ||
                    binding.DynamicCodeConstantSourceRow !=
                    FrameDirectCodeConstants
                        .DirectionalLightDirectionRowIndex)
                {
                    throw new ArgumentException(
                        $"Dynamic scene-light position vertex constant c{binding.Destination} has an invalid source shape.",
                        parameterName);
                }
                break;

            case TranslatedProgramVertexConstantBindingKind
                .DynamicSceneLightShadow:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    CodeMatrixTransform.None ||
                    binding.CodeMatrixRow != -1 ||
                    binding.DynamicCodeConstantSourceRow is not
                        { } sceneLightRow ||
                    sceneLightRow is not
                        FrameDirectCodeConstants.LightSpotFactorsRowIndex and
                        not FrameDirectCodeConstants
                            .LightFalloffPlacementRowIndex)
                {
                    throw new ArgumentException(
                        $"Dynamic scene-light shadow constant c{binding.Destination} has an invalid source shape.",
                        parameterName);
                }
                break;

            case TranslatedProgramVertexConstantBindingKind
                .DynamicSunShadowProjection:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    CodeMatrixTransform.None ||
                    binding.CodeMatrixRow != -1 ||
                    binding.DynamicCodeConstantSourceRow is not { } sourceRow ||
                    !TranslatedProgramDirectCodeConstantRows
                        .IsSunShadowProjectionSourceRow(sourceRow))
                {
                    throw new ArgumentException(
                        $"Dynamic sun-shadow projection vertex constant c{binding.Destination} has an invalid source shape.",
                        parameterName);
                }
                break;

            case TranslatedProgramVertexConstantBindingKind
                .DynamicClipSpaceLookup:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    CodeMatrixTransform.None ||
                    binding.CodeMatrixRow != -1 ||
                    binding.DynamicCodeConstantSourceRow is not
                        { } clipSpaceRow ||
                    !TranslatedProgramDirectCodeConstantRows
                        .IsClipSpaceLookupSourceRow(clipSpaceRow))
                {
                    throw new ArgumentException(
                        $"Dynamic clip-space lookup vertex constant c{binding.Destination} has an invalid source shape.",
                        parameterName);
                }
                break;

            case TranslatedProgramVertexConstantBindingKind
                .DynamicZNear:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    CodeMatrixTransform.None ||
                    binding.CodeMatrixRow != -1 ||
                    binding.DynamicCodeConstantSourceRow is not
                        { } zNearRow ||
                    !TranslatedProgramDirectCodeConstantRows
                        .IsZNearSourceRow(zNearRow))
                {
                    throw new ArgumentException(
                        $"Dynamic Z-near vertex constant c{binding.Destination} has an invalid source shape.",
                        parameterName);
                }
                break;

            case TranslatedProgramVertexConstantBindingKind
                .PerInstanceStaticModelBaseLightingCoords:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    CodeMatrixTransform.None ||
                    binding.CodeMatrixRow != -1 ||
                    binding.DynamicCodeConstantSourceRow !=
                    FrameDirectCodeConstants
                        .StaticModelBaseLightingCoordsRowIndex)
                {
                    throw new ArgumentException(
                        $"Per-instance model-lighting vertex constant c{binding.Destination} has an invalid source shape.",
                        parameterName);
                }
                break;

            case TranslatedProgramVertexConstantBindingKind
                .PerInstanceStaticModelLightProbeAmbient:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    CodeMatrixTransform.None ||
                    binding.CodeMatrixRow != -1 ||
                    binding.DynamicCodeConstantSourceRow !=
                    FrameDirectCodeConstants
                        .StaticModelLightProbeAmbientRowIndex)
                {
                    throw new ArgumentException(
                        $"Per-instance light-probe ambient vertex constant c{binding.Destination} has an invalid source shape.",
                        parameterName);
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    binding.Kind,
                    "Unknown vertex-constant binding kind.");
        }
    }

    private static bool IsFinite(ShaderConstantValue value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}

public sealed record
    TranslatedProgramVertexConstantBindingPlanBuildResult(
        TranslatedProgramVertexConstantBindingPlan? Plan,
        IReadOnlyList<string> Blockers)
{
    public bool IsReady => Plan is not null && Blockers.Count == 0;
}

/// <summary>
/// Pure draw-binding reconciliation for translated EditorPreview vertex
/// programs. Every microcode-read constant must have exactly one pass or
/// compiler-default owner, and that owner must resolve to an exact static value
/// or supported dynamic matrix row.
/// </summary>
public static class
    TranslatedProgramVertexConstantBindingPlanner
{
    public static
        TranslatedProgramVertexConstantBindingPlanBuildResult
        TryPlan(
            IReadOnlyList<int> readDestinations,
            IReadOnlyList<ShaderConstantDestination> passConstants,
            IReadOnlyList<EmbeddedVertexConstant> embeddedConstants,
            TranslatedProgramDirectCodeConstantPlan
                directCodePlan)
    {
        ArgumentNullException.ThrowIfNull(readDestinations);
        ArgumentNullException.ThrowIfNull(passConstants);
        ArgumentNullException.ThrowIfNull(embeddedConstants);
        ArgumentNullException.ThrowIfNull(directCodePlan);

        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        blockers.UnionWith(
            SelectedProgramVertexConstantOwnership.FindBlockers(
                readDestinations,
                passConstants,
                embeddedConstants));

        int[] reads = readDestinations
            .Distinct()
            .Order()
            .ToArray();
        var bindings = new List<
            TranslatedProgramVertexConstantBinding>(
                reads.Length);
        foreach (int destination in reads)
        {
            if (destination is < 0 or >=
                RsxVertexConstantLayout.Count)
            {
                continue;
            }

            ShaderConstantDestination[] passOwners = passConstants
                .Where(constant =>
                    constant.Destination == destination &&
                    constant.ArgumentType.EndsWith(
                        "VertexConst",
                        StringComparison.Ordinal))
                .ToArray();
            EmbeddedVertexConstant[] embeddedOwners =
                embeddedConstants
                    .Where(constant => constant.Destination == destination)
                    .ToArray();
            if (passOwners.Length + embeddedOwners.Length != 1)
            {
                continue;
            }

            if (embeddedOwners.Length == 1)
            {
                EmbeddedVertexConstant embedded = embeddedOwners[0];
                if (!TryCreateEmbeddedBinding(
                        embedded,
                        out TranslatedProgramVertexConstantBinding?
                            binding,
                        out string? blocker))
                {
                    blockers.Add(
                        $"vertexConstantDest{destination}={blocker}");
                    continue;
                }

                bindings.Add(binding!);
                continue;
            }

            if (!TryCreatePassBinding(
                    passOwners[0],
                    directCodePlan,
                    out TranslatedProgramVertexConstantBinding?
                        passBinding,
                    out string? passBlocker))
            {
                blockers.Add(
                    $"vertexConstantDest{destination}={passBlocker}");
                continue;
            }

            bindings.Add(passBinding!);
        }

        if (blockers.Count != 0 || bindings.Count != reads.Length)
        {
            return new(
                null,
                Array.AsReadOnly(blockers.ToArray()));
        }

        return new(
            new TranslatedProgramVertexConstantBindingPlan(
                bindings),
            []);
    }

    private static bool TryCreateEmbeddedBinding(
        EmbeddedVertexConstant embedded,
        out TranslatedProgramVertexConstantBinding? binding,
        out string? blocker)
    {
        binding = null;
        blocker = null;
        if (embedded.RawResourceIndex != embedded.Destination)
        {
            blocker = "EMBEDDED_RESOURCE_INDEX_DESTINATION_MISMATCH";
            return false;
        }
        if (!embedded.IsOperationallyResolved)
        {
            blocker = "EMBEDDED_OWNER_UNRESOLVED";
            return false;
        }
        if (!IsFinite(embedded.Value))
        {
            blocker = "EMBEDDED_VALUE_NONFINITE";
            return false;
        }

        binding = new(
            embedded.Destination,
            TranslatedProgramVertexConstantBindingOwner
                .EmbeddedProgramDefault,
            TranslatedProgramVertexConstantBindingKind
                .StaticValue,
            embedded.Value,
            null,
            CodeMatrixTransform.None,
            -1,
            $"embeddedParam{embedded.ParameterOrdinal}:{embedded.ParameterName}");
        return true;
    }

    private static bool TryCreatePassBinding(
        ShaderConstantDestination pass,
        TranslatedProgramDirectCodeConstantPlan directCodePlan,
        out TranslatedProgramVertexConstantBinding? binding,
        out string? blocker)
    {
        binding = null;
        blocker = null;

        bool hasAnyStaticValue =
            pass.Value.HasValue;
        bool hasCompleteStaticValue =
            pass.Value.HasValue;
        bool hasAnyMatrixSource =
            pass.CodeMatrix is not null;
        bool hasDirectSource = pass.CodeConstantSourceRow.HasValue;
        int sourceCount =
            (hasAnyStaticValue ? 1 : 0) +
            (hasAnyMatrixSource ? 1 : 0) +
            (hasDirectSource ? 1 : 0);
        if (sourceCount == 0)
        {
            blocker = "PASS_OWNER_VALUE_SOURCE_UNRESOLVED";
            return false;
        }
        if (sourceCount > 1)
        {
            blocker = "PASS_OWNER_VALUE_SOURCE_AMBIGUOUS";
            return false;
        }

        if (hasAnyStaticValue)
        {
            if (!hasCompleteStaticValue)
            {
                blocker = "PASS_STATIC_VALUE_INCOMPLETE";
                return false;
            }
            ShaderConstantValue value = pass.Value!.Value;
            if (!IsFinite(value))
            {
                blocker = "PASS_STATIC_VALUE_NONFINITE";
                return false;
            }
            if (!pass.IsOperationallyResolved)
            {
                blocker = "PASS_STATIC_VALUE_UNRESOLVED";
                return false;
            }

            binding = StaticPassBinding(pass, value, "static");
            return true;
        }

        if (hasAnyMatrixSource)
        {
            if (pass.CodeMatrix is not { } matrix ||
                !DerivedMatrixResolver.Supports(matrix.Semantic) ||
                !Enum.IsDefined(matrix.Transform) ||
                matrix.Row is < 0 or > 3)
            {
                blocker = "PASS_MATRIX_SOURCE_UNSUPPORTED";
                return false;
            }
            if (!pass.IsOperationallyResolved)
            {
                blocker = "PASS_MATRIX_SOURCE_UNRESOLVED";
                return false;
            }

            binding = new(
                pass.Destination,
                TranslatedProgramVertexConstantBindingOwner
                    .PassArgument,
                TranslatedProgramVertexConstantBindingKind
                    .DerivedMatrixRow,
                null,
                matrix.Semantic,
                matrix.Transform,
                matrix.Row,
                $"passArg{pass.ArgumentIndex}:{pass.ResourceIdentity}");
            return true;
        }

        ushort sourceRow = pass.CodeConstantSourceRow!.Value;
        if (!TranslatedProgramDirectCodeConstantRows
                .IsSupportedSourceRow(sourceRow))
        {
            blocker =
                $"PASS_DIRECT_CODE_ROW_0x{sourceRow:X2}_UNSUPPORTED";
            return false;
        }
        if (directCodePlan.IsDynamicSourceRow(sourceRow))
        {
            TranslatedProgramVertexConstantBindingKind kind;
            if (sourceRow ==
                FrameDirectCodeConstants.GameTimeRowIndex)
            {
                kind = TranslatedProgramVertexConstantBindingKind
                    .DynamicGameTime;
            }
            else if (sourceRow ==
                     FrameDirectCodeConstants
                         .DirectionalLightDirectionRowIndex &&
                     directCodePlan.SceneLightIndex.HasValue)
            {
                kind = TranslatedProgramVertexConstantBindingKind
                    .DynamicSceneLightPosition;
            }
            else if (sourceRow is
                     FrameDirectCodeConstants.LightSpotFactorsRowIndex or
                     FrameDirectCodeConstants
                         .LightFalloffPlacementRowIndex &&
                     directCodePlan.SceneLightIndex.HasValue)
            {
                kind = TranslatedProgramVertexConstantBindingKind
                    .DynamicSceneLightShadow;
            }
            else if (TranslatedProgramDirectCodeConstantRows
                         .IsSunShadowProjectionSourceRow(sourceRow))
            {
                kind = TranslatedProgramVertexConstantBindingKind
                    .DynamicSunShadowProjection;
            }
            else if (TranslatedProgramDirectCodeConstantRows
                         .IsClipSpaceLookupSourceRow(sourceRow))
            {
                kind = TranslatedProgramVertexConstantBindingKind
                    .DynamicClipSpaceLookup;
            }
            else if (TranslatedProgramDirectCodeConstantRows
                         .IsZNearSourceRow(sourceRow))
            {
                kind = TranslatedProgramVertexConstantBindingKind
                    .DynamicZNear;
            }
            else if (TranslatedProgramDirectCodeConstantRows
                         .IsStaticModelBaseLightingCoordsSourceRow(sourceRow))
            {
                kind = TranslatedProgramVertexConstantBindingKind
                    .PerInstanceStaticModelBaseLightingCoords;
            }
            else if (TranslatedProgramDirectCodeConstantRows
                         .IsStaticModelLightProbeAmbientSourceRow(sourceRow))
            {
                kind = TranslatedProgramVertexConstantBindingKind
                    .PerInstanceStaticModelLightProbeAmbient;
            }
            else
            {
                blocker =
                    $"PASS_DYNAMIC_CODE_ROW_0x{sourceRow:X2}_UNSUPPORTED";
                return false;
            }

            binding = new(
                pass.Destination,
                TranslatedProgramVertexConstantBindingOwner
                    .PassArgument,
                kind,
                null,
                null,
                CodeMatrixTransform.None,
                -1,
                $"passArg{pass.ArgumentIndex}:dynamicRow0x{sourceRow:X2}:{pass.ResourceIdentity}",
                sourceRow);
            return true;
        }

        if (!directCodePlan.TryGetRow(
                sourceRow,
                out DirectCodeConstantRow?
                    directRow))
        {
            blocker =
                $"PASS_DIRECT_CODE_ROW_0x{sourceRow:X2}_UNAVAILABLE";
            return false;
        }

        binding = StaticPassBinding(
            pass,
            directRow!.Value,
            $"directRow0x{sourceRow:X2}");
        return true;
    }

    private static
        TranslatedProgramVertexConstantBinding StaticPassBinding(
            ShaderConstantDestination pass,
            ShaderConstantValue value,
            string sourceIdentity) =>
        new(
            pass.Destination,
            TranslatedProgramVertexConstantBindingOwner
                .PassArgument,
            TranslatedProgramVertexConstantBindingKind
                .StaticValue,
            value,
            null,
            CodeMatrixTransform.None,
            -1,
            $"passArg{pass.ArgumentIndex}:{sourceIdentity}:{pass.ResourceIdentity}");

    private static bool IsFinite(ShaderConstantValue value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}
