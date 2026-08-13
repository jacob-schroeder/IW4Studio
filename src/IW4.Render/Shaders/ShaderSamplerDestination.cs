namespace IW4.Render.Shaders;

public sealed record ShaderSamplerDestination(
    int ArgumentIndex,
    string ArgumentType,
    ushort Destination,
    uint Argument,
    string ResourceIdentity,
    bool IsOperationallyResolved,
    string TextureTarget = "Texture2D");
