# ShaderConvert

ShaderConvert is a .NET 10 class library that converts compiled Direct3D 9 vertex and pixel shaders (SM1–SM3) into native PS3 RSX Cg program blobs. MapConverter uses it for extracted IW3 PC shaders.

## Convert a compiled shader

Pass the bytecode filename for diagnostics and the converted shader's program name.

```csharp
using ShaderConvert;

string path = "shader_bin/vs_sm3_0/example.cso";
ShaderCompilation result = ShaderConverter.ConvertBytecode(File.ReadAllBytes(path), path);
File.WriteAllBytes("example.vert.cg", result.Program);
```

The stage and shader model come from the bytecode. `ShaderConversionOptions.TechniquePath` and zero-based `PassIndex` select original IW3 material assignments from an extracted `.tech` file. Without a technique, known engine bindings are inferred by name. The `SignedNormalInputMask` and `DecodedTexCoordInputMask` options apply only to vertex shaders with packed PC inputs. Both default to zero.

## Output and compatibility

`result.Program` is the Cg blob consumed by PS3 material shader assets. `result.Parameters` lists reflected constants and samplers, their resource indices, register counts, and dimensions. Callers supply matching material bindings, vertex declarations, textures, and render targets. When replacing an existing program, update bindings that depend on parameter order.

For vertex programs, `result.VertexCommands` reports the MW2 command-list reservation. Conversion rejects programs above the unmodified engine's 3072-byte SPU merger budget. The RSX backend also checks the 512-instruction and 32-temporary vertex limits after lowering. Unsupported bytecode instructions and bindings fail explicitly.

## Build and distribution

The backend builds the bundled MojoShader and cgcomp sources into a platform-specific native library. Build on the target OS and, on Linux, the target architecture. Windows builds require LLVM Clang, the Windows SDK, and C++ build tools. The consuming application's build and publish output includes the native library and its notices.
