# MapConverter

MapConverter converts supported IW3 maps to IW4 and supported IW4 PS3 multiplayer maps to Xbox 360.

Run MapConverter from the repository root.

```bash
dotnet run --project tools/MapConverter/MapConverter.csproj -- <arguments>
```

Use `--help` for IW3 conversion options. Quote paths that contain spaces.

## Xbox 360 multiplayer map

```bash
dotnet run --project tools/MapConverter/MapConverter.csproj -- \
  to-xbox360 ps3/mp_pitch.ff xbox/imagefile7.tsv xbox/mp_pitch.ff
```

The input fastfile supplies the map geometry, lighting layout, collision, models, animations, materials, entities, and scripts. The encoder must place `imagefile7.pak` beside its TSV index. The index has one tab-separated row per image with no header. Its columns are the asset name, width, height, Xenon D3D format, mip count, uncompressed tiled byte count, map type (3 for 2D or 5 for a cube), start offset, and exclusive end offset. Numeric fields are decimal. Each range holds one zlib-compressed Xbox texture mip chain. Regenerate the package and index together when texture content or baked lighting changes.

The output targets the retail Xbox zone layout (`0x10D`) and uses the unsigned `IWffu100` container. Retail Xbox 360 multiplayer maps shipped in the signed format. For development, use the resulting `mp_pitch.ff` with its matching `imagefile7.pak` on a loader that accepts unsigned Xbox fastfiles and mounts image package 7. The TSV is a linker input. The pitch's seven custom PS3 sun and shadow technique sets resolve to their stock Xbox counterparts by name. The target game must supply those native shader programs.
