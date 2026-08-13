using IW4.Render.Shaders;

namespace IW4.Render.Execution;

/// <summary>
/// Host capabilities required before EditorPreview may execute a translated
/// authored fragment program. Unsupported inputs are routed to the generic
/// editor material instead of being silently initialized to zero.
/// </summary>
public static class TranslatedProgramCapability
{
    public const string ColorTargetZeroBlocker =
        "fragmentColorTarget0=EDITOR_VISIBLE_EXPORT_MISSING";
    public const string AdditionalMrtBlocker =
        "fragmentMrtTargets=EDITOR_ADDITIONAL_TARGETS_UNAVAILABLE";
    public const string UnknownTargetTopologyBlocker =
        "fragmentColorTargets=EDITOR_SURFACE_TARGET_TOPOLOGY_UNKNOWN";

    /// <summary>
    /// EditorPreview renders camera-color programs into one host color buffer.
    /// Its authored-program path intentionally models the normal-camera
    /// surface-A topology, where only fixed fragment output H0 is active.
    /// </summary>
    public static FragmentTargetOutputAvailability
        CreateSurfaceAOutputAvailability() => new(
            rawPs3SurfaceColorTarget: 0x01,
            hostDrawBufferCount: 1);

    public static IReadOnlyList<string> FindBlockers(
        IReadOnlyList<ShaderFragmentExport> fragmentColorExports,
        FragmentTargetOutputAvailability targetOutputs)
    {
        ArgumentNullException.ThrowIfNull(fragmentColorExports);
        ArgumentNullException.ThrowIfNull(targetOutputs);

        var blockers = new List<string>();
        if (!targetOutputs.HasKnownNativeOutputCount)
        {
            blockers.Add(UnknownTargetTopologyBlocker);
        }
        else if (targetOutputs.NativeOutputCount!.Value >
                 targetOutputs.HostDrawBufferCount)
            blockers.Add(AdditionalMrtBlocker);

        bool writesVisibleTarget = fragmentColorExports.Any(export =>
            export.ColorTarget == 0 &&
            export.WrittenComponentMask != 0 &&
            targetOutputs.IsNativeOutputActive(export.ColorTarget) &&
            targetOutputs.IsHostDrawBufferAvailable(export.ColorTarget));
        if (!writesVisibleTarget)
            blockers.Add(ColorTargetZeroBlocker);

        if (fragmentColorExports.Any(export =>
                export.WrittenComponentMask != 0 &&
                targetOutputs.IsNativeOutputActive(export.ColorTarget) &&
                !targetOutputs.IsHostDrawBufferAvailable(export.ColorTarget)) &&
            !blockers.Contains(AdditionalMrtBlocker, StringComparer.Ordinal))
        {
            blockers.Add(AdditionalMrtBlocker);
        }

        return blockers.AsReadOnly();
    }
}
