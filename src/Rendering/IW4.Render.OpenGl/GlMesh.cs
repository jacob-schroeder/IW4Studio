using System.Numerics;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl;

internal readonly record struct GlMesh(uint VertexArray, uint VertexBuffer, uint ElementBuffer, uint IndexCount);
