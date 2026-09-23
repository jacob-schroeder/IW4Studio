namespace IW4.Render.Shaders;

internal sealed record RsxShaderTranslationResult(
    RsxVertexProgramIr VertexProgramIr,
    RsxFragmentProgramIr FragmentProgramIr,
    bool ProgramIrReady,
    IReadOnlyList<int> ReadVertexInputDestinations,
    IReadOnlyList<int> ReadVertexConstantDestinations,
    IReadOnlyList<EmbeddedVertexConstant> EmbeddedVertexConstants,
    IReadOnlyList<int> ReadFragmentSamplerDestinations,
    uint FragmentProgramControl,
    string FragmentExportPrecision,
    bool FragmentDepthExportEnabled,
    IReadOnlyList<RsxFragmentColorExport> FragmentColorExports,
    IReadOnlyList<StaticFragmentConstantPatch> StaticFragmentConstantPatches,
    IReadOnlyList<CodePixelConstantPatchPlan> CodePixelConstantPatchPlans,
    IReadOnlyList<string> Blockers);
