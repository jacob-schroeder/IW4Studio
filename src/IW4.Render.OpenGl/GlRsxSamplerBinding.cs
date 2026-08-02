using System.Numerics;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl;

internal readonly record struct GlRsxSamplerBinding(int Destination, uint Texture, TextureTarget Target);
