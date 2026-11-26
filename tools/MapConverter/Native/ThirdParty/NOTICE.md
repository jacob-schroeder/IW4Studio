# MapConverter shader-tool sources

`MojoShader` is vendored at revision
`ad5dff84830c2863c841f4b1f4e3df78c705b383` from
https://github.com/icculus/mojoshader. Its zlib-style license is retained in
`MojoShader/LICENSE.txt`.

`cgcomp` is the minimal assembler source copied from PSL1GHT revision
`f649a08fd536a9e27c08c7db2d93a2d7ee4c3bbe`, under
`tools/cgcomp`. Its MIT license is retained in `CgComp/LICENSE`.

MapConverter builds these sources into the platform-specific shared library
`iw4_map_shaders`. Both MapConverter and ShaderConvert use it in process.

The local MojoShader profile reports invalid branch-stack bounds as translation
errors. The local cgcomp assembler returns program bytes or diagnostics through
a C interface and checks the bounds of its RSX output records.
