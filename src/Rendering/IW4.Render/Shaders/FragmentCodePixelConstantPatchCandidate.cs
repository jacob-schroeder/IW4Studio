namespace IW4.Render.Shaders;

internal sealed record FragmentCodePixelConstantPatchCandidate(
    int ArgumentOrdinal,
    ushort Destination,
    int ArgumentRaw,
    ushort CodeIndex,
    CodePixelConstantPatchStatus? DeferredStatus,
    IReadOnlyList<ushort> RelativePatchOffsets,
    int UploadOffset,
    string? Detail);
