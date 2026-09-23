namespace IW4.Render.Shaders;

/// <summary>Immutable float4 snapshot retained by a selected-pass constant plan.</summary>
public readonly record struct ShaderConstantValue(
    float X,
    float Y,
    float Z,
    float W);
