namespace IW4.Render.OpenGl;

internal readonly record struct GlSkyMesh(
    uint VertexArray,
    uint VertexBuffer,
    uint ElementBuffer,
    uint IndexCount,
    uint Texture);
