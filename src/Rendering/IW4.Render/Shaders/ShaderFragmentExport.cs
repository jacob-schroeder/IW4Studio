
namespace IW4.Render.Shaders;

public sealed record ShaderFragmentExport(
    int ColorTarget,
    string Register,
    RsxFragmentWriteMask WrittenComponentMask,
    string WrittenComponents);
