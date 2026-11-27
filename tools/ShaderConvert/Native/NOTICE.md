# ShaderConvert native shader sources

`MojoShader` is vendored at revision
`ad5dff84830c2863c841f4b1f4e3df78c705b383` from
https://github.com/icculus/mojoshader. Its zlib-style license is retained in
`ThirdParty/MojoShader/LICENSE.txt`.

`cgcomp` is the minimal assembler source copied from PSL1GHT revision
`f649a08fd536a9e27c08c7db2d93a2d7ee4c3bbe`. Its MIT license is retained in
`ThirdParty/CgComp/LICENSE`.

ShaderConvert builds these sources into the platform-specific shared library
`iw4_shader_convert`, which MapConverter consumes through ShaderConvert.

The local MojoShader profile reports invalid branch-stack bounds as translation
errors. The local cgcomp assembler returns program bytes or diagnostics through
a C interface and checks the bounds of its RSX output records.
