using IW4.Render.Execution;
using IW4.Render.Shaders;

namespace IW4.Render.EditorPreview;

/// <summary>
/// One exact draw-time binding for a vertex constant read by the selected RSX
/// microcode. Static values include compiler-owned defaults and resolved
/// direct-table rows; derived matrix rows retain their dynamic matrix source.
/// </summary>
public sealed record MapRenderEditorTranslatedProgramVertexConstantBinding(
    ushort Destination,
    MapRenderEditorTranslatedProgramVertexConstantBindingOwner Owner,
    MapRenderEditorTranslatedProgramVertexConstantBindingKind Kind,
    MapRenderShaderConstantValue? StaticValue,
    MapRenderCodeMatrixSemantic? CodeMatrixSemantic,
    MapRenderCodeMatrixTransform CodeMatrixTransform,
    int CodeMatrixRow,
    string OwnerIdentity,
    ushort? DynamicCodeConstantSourceRow = null);

public enum MapRenderEditorTranslatedProgramVertexConstantBindingOwner
{
    PassArgument,
    EmbeddedProgramDefault
}

public enum MapRenderEditorTranslatedProgramVertexConstantBindingKind
{
    StaticValue,
    DerivedMatrixRow,
    DynamicGameTime,
    DynamicSceneLightPosition,
    DynamicSunShadowProjection,
    DynamicClipSpaceLookup,
    DynamicZNear,
    PerInstanceStaticModelBaseLightingCoords,
    PerInstanceStaticModelLightProbeAmbient
}

/// <summary>
/// Immutable, destination-keyed vertex-constant bindings for one translated
/// EditorPreview program. The plan contains exactly one binding for every
/// constant destination read by the selected microcode.
/// </summary>
public sealed class
    MapRenderEditorTranslatedProgramVertexConstantBindingPlan
{
    internal MapRenderEditorTranslatedProgramVertexConstantBindingPlan(
        IReadOnlyList<
            MapRenderEditorTranslatedProgramVertexConstantBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        MapRenderEditorTranslatedProgramVertexConstantBinding[] copied =
            bindings
                .Select(binding => binding is null
                    ? throw new ArgumentException(
                        "Vertex-constant binding plans cannot contain null bindings.",
                        nameof(bindings))
                    : binding with { })
                .ToArray();
        var destinations = new HashSet<ushort>();
        foreach (MapRenderEditorTranslatedProgramVertexConstantBinding binding
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
        MapRenderEditorTranslatedProgramVertexConstantBinding> Bindings
        { get; }

    private static void Validate(
        MapRenderEditorTranslatedProgramVertexConstantBinding binding,
        string parameterName)
    {
        if (binding.Destination >
            MapRenderRsxVertexConstantLayout.MaximumDestination)
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
            case MapRenderEditorTranslatedProgramVertexConstantBindingKind
                .StaticValue:
                if (binding.StaticValue is not { } value ||
                    !IsFinite(value) ||
                    binding.DynamicCodeConstantSourceRow.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    MapRenderCodeMatrixTransform.None ||
                    binding.CodeMatrixRow != -1)
                {
                    throw new ArgumentException(
                        $"Static vertex constant c{binding.Destination} has an invalid value/source shape.",
                        parameterName);
                }
                break;

            case MapRenderEditorTranslatedProgramVertexConstantBindingKind
                .DerivedMatrixRow:
                if (binding.StaticValue.HasValue ||
                    binding.DynamicCodeConstantSourceRow.HasValue ||
                    binding.CodeMatrixSemantic is not { } semantic ||
                    !MapRenderDerivedMatrixResolver.Supports(semantic) ||
                    !Enum.IsDefined(binding.CodeMatrixTransform) ||
                    binding.CodeMatrixRow is < 0 or > 3)
                {
                    throw new ArgumentException(
                        $"Matrix vertex constant c{binding.Destination} has an invalid value/source shape.",
                        parameterName);
                }
                break;

            case MapRenderEditorTranslatedProgramVertexConstantBindingKind
                .DynamicGameTime:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    MapRenderCodeMatrixTransform.None ||
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

            case MapRenderEditorTranslatedProgramVertexConstantBindingKind
                .DynamicSceneLightPosition:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    MapRenderCodeMatrixTransform.None ||
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

            case MapRenderEditorTranslatedProgramVertexConstantBindingKind
                .DynamicSunShadowProjection:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    MapRenderCodeMatrixTransform.None ||
                    binding.CodeMatrixRow != -1 ||
                    binding.DynamicCodeConstantSourceRow is not { } sourceRow ||
                    !MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
                        .IsSunShadowProjectionSourceRow(sourceRow))
                {
                    throw new ArgumentException(
                        $"Dynamic sun-shadow projection vertex constant c{binding.Destination} has an invalid source shape.",
                        parameterName);
                }
                break;

            case MapRenderEditorTranslatedProgramVertexConstantBindingKind
                .DynamicClipSpaceLookup:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    MapRenderCodeMatrixTransform.None ||
                    binding.CodeMatrixRow != -1 ||
                    binding.DynamicCodeConstantSourceRow is not
                        { } clipSpaceRow ||
                    !MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
                        .IsClipSpaceLookupSourceRow(clipSpaceRow))
                {
                    throw new ArgumentException(
                        $"Dynamic clip-space lookup vertex constant c{binding.Destination} has an invalid source shape.",
                        parameterName);
                }
                break;

            case MapRenderEditorTranslatedProgramVertexConstantBindingKind
                .DynamicZNear:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    MapRenderCodeMatrixTransform.None ||
                    binding.CodeMatrixRow != -1 ||
                    binding.DynamicCodeConstantSourceRow is not
                        { } zNearRow ||
                    !MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
                        .IsZNearSourceRow(zNearRow))
                {
                    throw new ArgumentException(
                        $"Dynamic Z-near vertex constant c{binding.Destination} has an invalid source shape.",
                        parameterName);
                }
                break;

            case MapRenderEditorTranslatedProgramVertexConstantBindingKind
                .PerInstanceStaticModelBaseLightingCoords:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    MapRenderCodeMatrixTransform.None ||
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

            case MapRenderEditorTranslatedProgramVertexConstantBindingKind
                .PerInstanceStaticModelLightProbeAmbient:
                if (binding.StaticValue.HasValue ||
                    binding.CodeMatrixSemantic.HasValue ||
                    binding.CodeMatrixTransform !=
                    MapRenderCodeMatrixTransform.None ||
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

    private static bool IsFinite(MapRenderShaderConstantValue value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}

public sealed record
    MapRenderEditorTranslatedProgramVertexConstantBindingPlanBuildResult(
        MapRenderEditorTranslatedProgramVertexConstantBindingPlan? Plan,
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
    MapRenderEditorTranslatedProgramVertexConstantBindingPlanner
{
    public static
        MapRenderEditorTranslatedProgramVertexConstantBindingPlanBuildResult
        TryPlan(
            IReadOnlyList<int> readDestinations,
            IReadOnlyList<MapRenderShaderSamplerDestination> passConstants,
            IReadOnlyList<MapRenderEmbeddedVertexConstant> embeddedConstants,
            MapRenderEditorTranslatedProgramDirectCodeConstantPlan
                directCodePlan)
    {
        ArgumentNullException.ThrowIfNull(readDestinations);
        ArgumentNullException.ThrowIfNull(passConstants);
        ArgumentNullException.ThrowIfNull(embeddedConstants);
        ArgumentNullException.ThrowIfNull(directCodePlan);

        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        blockers.UnionWith(
            MapRenderSelectedProgramVertexConstantOwnership.FindBlockers(
                readDestinations,
                passConstants,
                embeddedConstants));

        int[] reads = readDestinations
            .Distinct()
            .Order()
            .ToArray();
        var bindings = new List<
            MapRenderEditorTranslatedProgramVertexConstantBinding>(
                reads.Length);
        foreach (int destination in reads)
        {
            if (destination is < 0 or >=
                MapRenderRsxVertexConstantLayout.Count)
            {
                continue;
            }

            MapRenderShaderSamplerDestination[] passOwners = passConstants
                .Where(constant =>
                    constant.Destination == destination &&
                    constant.ArgumentType.EndsWith(
                        "VertexConst",
                        StringComparison.Ordinal))
                .ToArray();
            MapRenderEmbeddedVertexConstant[] embeddedOwners =
                embeddedConstants
                    .Where(constant => constant.Destination == destination)
                    .ToArray();
            if (passOwners.Length + embeddedOwners.Length != 1)
            {
                continue;
            }

            if (embeddedOwners.Length == 1)
            {
                MapRenderEmbeddedVertexConstant embedded = embeddedOwners[0];
                if (!TryCreateEmbeddedBinding(
                        embedded,
                        out MapRenderEditorTranslatedProgramVertexConstantBinding?
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
                    out MapRenderEditorTranslatedProgramVertexConstantBinding?
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
            new MapRenderEditorTranslatedProgramVertexConstantBindingPlan(
                bindings),
            []);
    }

    private static bool TryCreateEmbeddedBinding(
        MapRenderEmbeddedVertexConstant embedded,
        out MapRenderEditorTranslatedProgramVertexConstantBinding? binding,
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
            MapRenderEditorTranslatedProgramVertexConstantBindingOwner
                .EmbeddedProgramDefault,
            MapRenderEditorTranslatedProgramVertexConstantBindingKind
                .StaticValue,
            embedded.Value,
            null,
            MapRenderCodeMatrixTransform.None,
            -1,
            $"embeddedParam{embedded.ParameterOrdinal}:{embedded.ParameterName}");
        return true;
    }

    private static bool TryCreatePassBinding(
        MapRenderShaderSamplerDestination pass,
        MapRenderEditorTranslatedProgramDirectCodeConstantPlan directCodePlan,
        out MapRenderEditorTranslatedProgramVertexConstantBinding? binding,
        out string? blocker)
    {
        binding = null;
        blocker = null;

        bool hasAnyStaticValue =
            pass.X.HasValue ||
            pass.Y.HasValue ||
            pass.Z.HasValue ||
            pass.W.HasValue;
        bool hasCompleteStaticValue =
            pass.X.HasValue &&
            pass.Y.HasValue &&
            pass.Z.HasValue &&
            pass.W.HasValue;
        bool hasAnyMatrixSource =
            pass.CodeMatrixSemantic.HasValue ||
            pass.CodeMatrixTransform != MapRenderCodeMatrixTransform.None ||
            pass.CodeMatrixRow != -1;
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
            var value = new MapRenderShaderConstantValue(
                pass.X!.Value,
                pass.Y!.Value,
                pass.Z!.Value,
                pass.W!.Value);
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
            if (pass.CodeMatrixSemantic is not { } semantic ||
                !MapRenderDerivedMatrixResolver.Supports(semantic) ||
                !Enum.IsDefined(pass.CodeMatrixTransform) ||
                pass.CodeMatrixRow is < 0 or > 3)
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
                MapRenderEditorTranslatedProgramVertexConstantBindingOwner
                    .PassArgument,
                MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .DerivedMatrixRow,
                null,
                semantic,
                pass.CodeMatrixTransform,
                pass.CodeMatrixRow,
                $"passArg{pass.ArgumentIndex}:{pass.ResourceIdentity}");
            return true;
        }

        ushort sourceRow = pass.CodeConstantSourceRow!.Value;
        if (!MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
                .IsSupportedSourceRow(sourceRow))
        {
            blocker =
                $"PASS_DIRECT_CODE_ROW_0x{sourceRow:X2}_UNSUPPORTED";
            return false;
        }
        if (directCodePlan.IsDynamicSourceRow(sourceRow))
        {
            MapRenderEditorTranslatedProgramVertexConstantBindingKind kind;
            if (sourceRow ==
                FrameDirectCodeConstants.GameTimeRowIndex)
            {
                kind = MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .DynamicGameTime;
            }
            else if (sourceRow ==
                     FrameDirectCodeConstants
                         .DirectionalLightDirectionRowIndex &&
                     directCodePlan.SceneLightIndex.HasValue)
            {
                kind = MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .DynamicSceneLightPosition;
            }
            else if (MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
                         .IsSunShadowProjectionSourceRow(sourceRow))
            {
                kind = MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .DynamicSunShadowProjection;
            }
            else if (MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
                         .IsClipSpaceLookupSourceRow(sourceRow))
            {
                kind = MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .DynamicClipSpaceLookup;
            }
            else if (MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
                         .IsZNearSourceRow(sourceRow))
            {
                kind = MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .DynamicZNear;
            }
            else if (MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
                         .IsStaticModelBaseLightingCoordsSourceRow(sourceRow))
            {
                kind = MapRenderEditorTranslatedProgramVertexConstantBindingKind
                    .PerInstanceStaticModelBaseLightingCoords;
            }
            else if (MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
                         .IsStaticModelLightProbeAmbientSourceRow(sourceRow))
            {
                kind = MapRenderEditorTranslatedProgramVertexConstantBindingKind
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
                MapRenderEditorTranslatedProgramVertexConstantBindingOwner
                    .PassArgument,
                kind,
                null,
                null,
                MapRenderCodeMatrixTransform.None,
                -1,
                $"passArg{pass.ArgumentIndex}:dynamicRow0x{sourceRow:X2}:{pass.ResourceIdentity}",
                sourceRow);
            return true;
        }

        if (!directCodePlan.TryGetRow(
                sourceRow,
                out MapRenderDirectCodeConstantRow?
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
        MapRenderEditorTranslatedProgramVertexConstantBinding StaticPassBinding(
            MapRenderShaderSamplerDestination pass,
            MapRenderShaderConstantValue value,
            string sourceIdentity) =>
        new(
            pass.Destination,
            MapRenderEditorTranslatedProgramVertexConstantBindingOwner
                .PassArgument,
            MapRenderEditorTranslatedProgramVertexConstantBindingKind
                .StaticValue,
            value,
            null,
            MapRenderCodeMatrixTransform.None,
            -1,
            $"passArg{pass.ArgumentIndex}:{sourceIdentity}:{pass.ResourceIdentity}");

    private static bool IsFinite(MapRenderShaderConstantValue value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}
