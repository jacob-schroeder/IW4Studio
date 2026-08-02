namespace IW4.Render.Shaders;

/// <summary>
/// Potential color-target write metadata for the selected RSX export bank.
/// Predicates and control flow remain represented in the instruction stream;
/// this is not a claim that a write is guaranteed at runtime.
/// </summary>
public readonly record struct RsxFragmentColorExport(
    int ColorTarget,
    bool Fp16,
    int RegisterIndex,
    byte WrittenComponentMask,
    string WrittenComponents)
{
    public string Register => $"{(Fp16 ? 'H' : 'R')}{RegisterIndex}";
}
