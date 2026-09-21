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

`--signed-normal-input-mask` and `--decoded-texcoord-input-mask` apply MapConverter's
existing input decoding adaptations when converting shaders written for packed PC
vertex streams. Values are 16-bit RSX input-slot bit masks, in decimal or `0x` hex.
Leave both zero for HLSL written against the actual PS3 vertex inputs.

The repository currently builds the native conversion tools on macOS. The HLSL
frontend is a Windows executable run through Wine on macOS; Windows and Linux
native-tool build and distribution are not provided by this project.
