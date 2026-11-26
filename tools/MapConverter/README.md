# MapConverter

MapConverter converts supported IW3 maps to IW4 and supported IW4 PS3 multiplayer maps to Xbox 360.

Run MapConverter from the repository root.

```bash
dotnet run --project tools/MapConverter/MapConverter.csproj -- <arguments>
```

Use `--help` for IW3 conversion options. Quote paths that contain spaces.

## IW3 conversion dependencies

MapConverter reads IW3 PC and PS3 fastfiles in process. The IW3 PC C# readers
and asset exporters live under `tools/MapConverter.IW3.PC` in the `Readers`
and `Assets` namespaces. The F# project owns IWD handling, the IWI6 header,
and PC conversion rules. MapConverter builds the version 22 D3DBSP and passes
it to the IW4 linker. OpenAssetTools is the reference for the IW3 PC formats.
Both MapConverter and ShaderConvert use the bundled shader library for compiled
D3D9 shaders.

Windows x64 and Apple Silicon builds of Studio and MapConverter automatically download
and verify FFmpeg on the first build and bundle it beside the executable. PC audio
conversion uses this copy for MP3 encoding. For other targets, development builds
can use `ffmpeg` on `PATH`. To publish on those targets, supply a standalone FFmpeg
with `libmp3lame` and its license. This example targets Linux x64.

```sh
dotnet publish tools/MapConverter/MapConverter.csproj -c Release -r linux-x64 --self-contained \
  -p:MapConverterFfmpegPath=/path/to/distribution/ffmpeg \
  -p:MapConverterFfmpegLicensePath=/path/to/distribution/LICENSE
```

`MapConverterFfmpegNoticePath` can include an additional distribution notice.
The publish directory contains the executable and license alongside MapConverter.

## Xbox 360 multiplayer map

```bash
dotnet run --project tools/MapConverter/MapConverter.csproj -- \
  to-xbox360 ps3/mp_pitch.ff xbox/imagefile7.tsv xbox/mp_pitch.ff
```

The input fastfile supplies the map geometry, lighting layout, collision, models, animations, materials, entities, and scripts. The encoder must place `imagefile7.pak` beside its TSV index. The index has one tab-separated row per image with no header. Its columns are the asset name, width, height, Xenon D3D format, mip count, uncompressed tiled byte count, map type (3 for 2D or 5 for a cube), start offset, and exclusive end offset. Numeric fields are decimal. Each range holds one zlib-compressed Xbox texture mip chain. Regenerate the package and index together when texture content or baked lighting changes.

The output targets the retail Xbox zone layout (`0x10D`) and uses the unsigned `IWffu100` container. Retail Xbox 360 multiplayer maps shipped in the signed format. For development, use the resulting `mp_pitch.ff` with its matching `imagefile7.pak` on a loader that accepts unsigned Xbox fastfiles and mounts image package 7. The TSV is a linker input. The pitch's seven custom PS3 sun and shadow technique sets resolve to their stock Xbox counterparts by name. The target game must supply those native shader programs.
