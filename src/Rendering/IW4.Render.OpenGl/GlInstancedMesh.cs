using System.Numerics;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl;

internal readonly record struct GlInstancedMesh(
    uint VertexArray,
    uint VertexBuffer,
    uint ElementBuffer,
    uint InstanceBuffer,
    uint IndexCount,
    uint InstanceCount);
