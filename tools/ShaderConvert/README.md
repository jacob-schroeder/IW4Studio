# ShaderConvert

Converts Direct3D 9 vertex and pixel shaders to PS3 RSX Cg program blobs. It uses
MapConverter's existing MojoShader → RSX assembly → Cg packaging implementation.
HLSL source first compiles to Direct3D bytecode with `D3DCompileFromFile`.

Build on macOS with .NET 10, a C/C++ toolchain, and LLVM (`clang` and `lld-link`):

```sh
dotnet build tools/ShaderConvert/ShaderConvert.csproj -c Release
```

Compiled shader input does not need Wine:

```sh
dotnet run --project tools/ShaderConvert -c Release --no-build -- \
  --input /path/to/shader.cso --output /path/to/shader.vert.cg
```

For HLSL, install Wine with `d3dcompiler_47.dll` available and its standard `Z:`
mapping to the host filesystem. `wine` is found on `PATH`; `--wine /path/to/wine`
can select another installation. Includes resolve relative to the source file.
Compiler warnings and errors are retained on stderr.
Wine's builtin include handler may fail for source directories containing non-ASCII
characters; use an ASCII source directory when compiling a shader with includes.
`--d3dcompiler /path/to/d3dcompiler_47.dll` loads a specific 64-bit compiler DLL
without changing the Wine installation. This is useful for matching a known PC
compiler's optimization and include behavior. The default is Wine's configured
`d3dcompiler_47.dll`; the tool does not download or install a compiler.

```sh
dotnet run --project tools/ShaderConvert -c Release --no-build -- \
  --input /path/to/ocean.hlsl --output /path/to/ocean.vert.cg \
  --stage vertex --entry vs_main --profile vs_3_0
```

`.hlsl` and `.fx` select HLSL input; use `--format hlsl` or `--format bytecode`
to override. Compiled SM1/SM2/SM3 input determines its stage and model from the
bytecode. Source input requires `--stage vertex|pixel`; entry point defaults to
`main`, and profile defaults to `vs_3_0` or `ps_3_0`.

For editable PS3 Cg text, use `--format cg --stage vertex` or `--format cg --stage pixel`.
This mode accepts the Cg/SM3-compatible subset, maps RSX semantics such as
`ATTR0`, `C4` and `TEXUNIT0` to native bindings, and skips the IW3 PC material
adaptations. It requires the same Wine frontend as HLSL on macOS.
Both editable source and compiled stock program blobs can have a `.cg` extension.

Studio defaults to an in-process C# compiler for vertex and pixel Cg. It supports scalar
and vector arithmetic, swizzles, `dot`, `min`, `max`, `saturate`, and scalar
comparisons. Vertex numeric uniforms use explicit `Cn` bindings. Pixel programs
support named numeric uniforms, `tex2D`, and scalar `if` guards on assignments. The compiler menu also lists installed external tools. Studio
looks for PS3DK's `rsx-cg-compiler` on `PATH` (and in `~/.local/bin` on macOS),
and for `sce-cgc` via `SCE_PS3_ROOT` or `PATH`. Choose ShaderConvert explicitly
to use this tool's SM3 frontend.
ShaderConvert defines `IW4_SHADERCONVERT_SM3` when it compiles Cg through the
SM3 frontend. Reconstructed fragment shaders that need reciprocal clip W for
explicit perspective correction use `WPOS.w` and require `rsx-cg-compiler` or
`sce-cgc`. The SM3 pixel-position input supplies only x and y.

Use `--technique /path/to/material.tech` to supply the original IW3 code/material
argument assignments, and `--pass 0` to select a pass (zero by default). The existing
MapConverter technique parser and binding rules are reused. Without a technique,
known engine parameter names are inferred by those same binding rules.

The output is the native Cg blob consumed by PS3 material shader assets, including
parameters, executable instructions, and embedded literal constants. ShaderConvert
prints parameter resource indices and register counts for the material binding
step. It does not create engine material bindings, vertex declarations, textures,
or render targets. Unsupported instructions and bindings fail with a nonzero exit
code. An existing output is replaced only after conversion completes successfully;
input and output cannot be the same file.

The current vertex conversion path supports at most 512 emitted instructions and
32 temporary registers. The instruction check runs after instruction expansion,
so a source program that fits its PC profile can still exceed the supported Cg
limit. Dynamic array addressing and other unsupported lowering patterns fail
explicitly. A successful conversion does not establish that the caller's engine
bindings or render resources match the PC shader; those must be supplied by the
material using the converted program.

For vertex shaders, ShaderConvert also calculates the exact native MW2 vertex
command-list reservation from the generated Cg blob: instruction upload commands,
per-eight-instruction headers, embedded default vectors, and the final RETURN.
It rejects converted vertex shaders requiring more than 3072 command-list bytes
before replacing the output, and prints the blob size, instruction and temporary
register counts, default-vector/default-word counts, and command-list usage on
success. This conservative 3 KiB check is for compatibility with the unmodified
MW2 engine's SPU merger; it is not a general RSX vertex-program limit. Pixel
shaders are not subject to this rejection.

`--signed-normal-input-mask` and `--decoded-texcoord-input-mask` apply MapConverter's
existing input decoding adaptations when converting shaders written for packed PC
vertex streams. Values are 16-bit RSX input-slot bit masks, in decimal or `0x` hex.
Leave both zero for HLSL written against the actual PS3 vertex inputs.

For compiled D3D9 shaders, build on the target OS. On Linux, use a build host
with the target architecture. The build
uses a C/C++ compiler on Linux or macOS and LLVM Clang with the Windows SDK
and C++ build tools on Windows. The resulting build and publish directories
include the native shader library. Compiled shader conversion calls it directly.
The separate HLSL frontend build above targets Wine on macOS.
