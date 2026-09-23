namespace IW4.Render.Shaders;

internal sealed record FragmentStaticPixelConstantPatchCandidate(
    StaticFragmentConstantPatch Patch,
    IReadOnlyList<ushort> RelativePatchOffsets,
    int UploadOffset);
