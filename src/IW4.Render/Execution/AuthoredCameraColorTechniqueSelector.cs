using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Techniques;

namespace IW4.Render.Execution;

/// <summary>
/// Resolves one deterministic EditorPreview camera-color technique group.
/// The selected slot and authored pass order are shared by map static models
/// and the standalone XModel viewer.
/// </summary>
internal static class AuthoredCameraColorTechniqueSelector
{
    internal static AuthoredCameraColorTechniqueSelection Select(
        MaterialAsset material,
        MaterialTechniqueSetAsset? techniqueSet,
        IMaterialExecutionLookup lookup,
        int? exactTechniqueSlot = null)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(lookup);
        if (techniqueSet is null)
        {
            return AuthoredCameraColorTechniqueSelection.Blocked(
                "techniqueSet=unresolved");
        }

        IReadOnlyList<MaterialTechniqueSlot> slots =
            lookup.ResolveTechniqueSlots(techniqueSet);
        IReadOnlyList<int> orderedSlots = exactTechniqueSlot is { } exact
            ? [exact]
            : EditorPreviewTechniquePolicy.OrderCandidateSlots(slots);
        string lastBlocker = "cameraColorTechnique=notFound";
        foreach (int slotIndex in orderedSlots)
        {
            MaterialTechniqueSlot? slot = slots.FirstOrDefault(candidate =>
                candidate.Index == slotIndex);
            if (slot?.Technique is not { } technique)
            {
                lastBlocker = $"techniqueSlot{slotIndex}=unresolved";
                if (exactTechniqueSlot.HasValue)
                {
                    return new AuthoredCameraColorTechniqueSelection(
                        slotIndex,
                        string.Empty,
                        [],
                        lastBlocker);
                }
                continue;
            }
            if (technique.PassCount != technique.Passes.Count)
            {
                lastBlocker =
                    $"techniqueSlot{slotIndex}=passCountMismatch(" +
                    $"declared={technique.PassCount},loaded={technique.Passes.Count})";
                return new AuthoredCameraColorTechniqueSelection(
                    slotIndex,
                    technique.Name ?? string.Empty,
                    [],
                    lastBlocker);
            }

            var passes = new List<AuthoredCameraColorPassSelection>(
                technique.Passes.Count);
            string? groupBlocker = null;
            for (int passIndex = 0;
                 passIndex < technique.Passes.Count;
                 passIndex++)
            {
                MaterialPassAsset sourcePass = technique.Passes[passIndex];
                IReadOnlyList<MaterialShaderArgumentAsset> arguments =
                    lookup.ResolveShaderArgs(sourcePass);
                int unresolvedCodeSamplerCount = arguments.Count(argument =>
                    argument.Type ==
                        MaterialShaderArgumentType.CodePixelSampler &&
                    !CodePixelSamplerAbi.HasRuntimeRequirement(
                        unchecked((uint)argument.ArgumentRaw)));
                bool stateReady = RenderStateDecoder.TryDecode(
                    material,
                    slotIndex,
                    passIndex,
                    lookup,
                    out RenderState state);
                if (!stateReady)
                    state = RenderState.Default;
                string passClass = MaterialPassClassifier.Classify(
                    technique.Name ?? string.Empty,
                    state,
                    unresolvedCodeSamplerCount);
                if (!MaterialPassClassifier.CanSubmitToCameraColor(passClass))
                {
                    groupBlocker =
                        $"techniqueSlot{slotIndex}.pass{passIndex}=" +
                        $"nonCameraColor({passClass})";
                    break;
                }

                passes.Add(new AuthoredCameraColorPassSelection(
                    sourcePass,
                    arguments,
                    passIndex,
                    passClass,
                    state,
                    unresolvedCodeSamplerCount,
                    stateReady));
            }

            if (groupBlocker is null &&
                passes.Count == technique.Passes.Count &&
                passes.Count > 0)
            {
                return new AuthoredCameraColorTechniqueSelection(
                    slotIndex,
                    technique.Name ?? string.Empty,
                    passes,
                    string.Empty);
            }

            lastBlocker = groupBlocker ??
                $"techniqueSlot{slotIndex}=noCameraColorPass";
            // EditorPreview owns one explicit normal-camera selector policy:
            // the first populated candidate slot is authoritative. A blocked
            // selected group must remain visible as blocked instead of
            // silently changing the material technique.
            return new AuthoredCameraColorTechniqueSelection(
                slotIndex,
                technique.Name ?? string.Empty,
                [],
                lastBlocker);
        }

        return AuthoredCameraColorTechniqueSelection.Blocked(lastBlocker);
    }
}

internal sealed record AuthoredCameraColorTechniqueSelection(
    int TechniqueSlot,
    string TechniqueName,
    IReadOnlyList<AuthoredCameraColorPassSelection> Passes,
    string Blocker)
{
    internal static AuthoredCameraColorTechniqueSelection Blocked(
        string blocker) => new(-1, string.Empty, [], blocker);
}

internal sealed record AuthoredCameraColorPassSelection(
    MaterialPassAsset SourcePass,
    IReadOnlyList<MaterialShaderArgumentAsset> Arguments,
    int PassIndex,
    string PassClass,
    RenderState State,
    int UnresolvedCodeSamplerCount,
    bool StateReady);
