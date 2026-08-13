
namespace IW4.Render.Shaders;

public sealed record ShaderProgramIdentity(
    string Stage,
    string Name,
    uint DeclaredDataSize,
    int LoadedDataSize,
    string DataSha256,
    bool HasProgramData);
