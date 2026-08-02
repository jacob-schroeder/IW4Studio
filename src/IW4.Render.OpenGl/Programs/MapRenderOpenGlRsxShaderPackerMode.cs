namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Operational lowering for the authored RSX shader-packer state, including
/// FP32 suppression and the pre-blend transfer order.
/// </summary>
public enum MapRenderOpenGlRsxShaderPackerMode
{
    DisabledByState = 0,
    LinearToSrgbProgramEpilogue,
    PremultipliedLinearToSrgbProgramEpilogue,
    SuppressedForFp32Exports,
    SuppressedForDiagnosticOutput
}
