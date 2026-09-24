# PS3 map startup assets

`ps3/` is the disk source bundle for the current Rangers/OpFor Free-for-all and Team Deathmatch profile. D3dbspLinker copies it to `bootstrap/ps3/` beside the executable during build and publish. Normal `build` commands load these files automatically and require no donor fastfile or original image package.

The bundle contains 53 native XModels, 105 XModelSurfs groups, their material/technique/shader/image dependencies, one physics preset, four faction icon materials, and link settings. Bone names and asset references are symbolic; the linker reconstructs pointers and the XAssetList.

The original assets were extracted offline from the local PS3 `mp_terminal.ff` dependency workspace. Native image extraction was completed on 2026-09-24. Version 2 `images/*.image.json` retains each original texture descriptor. `.pixels.bin` contains resident pixels, while `.stream0.bin` through `.stream3.bin` retain active stream parts, including mip tails and padding. These are original GPU payloads, without RGBA expansion, downscaling, or recompression. Decoded DDS is optional editor/authoring data, not a bootstrap build input.

Regenerate into a new staging directory before replacing reviewed sources:

```sh
D3dbspLinker export-bootstrap /path/to/mp_terminal.ff /path/to/raw /path/to/new-bootstrap
```

Extraction may load original dependency zones and their adjacent image packages. It is separate from the ordinary map build command. Missing complete source data fails explicitly.

The bundle now occupies **118,973,392 bytes (113.46 MiB)**, compared with **460,514,911 bytes (439.18 MiB)** of file contents in the initial decoded-DDS bundle. Image sources occupy 85,520,302 bytes. The earlier approximately 443 MB estimate described disk usage; the exact comparison here sums file lengths. The 2,263 files comprise 1,198 image metadata/payload files, 576 shader files, 211 techniques, 15 technique sets, 102 materials, 53 models, 105 surface groups, two physics files, and the bootstrap manifest. The old 210 DDS files have been removed from the source folder.

Normal map builds preserve streamed texture descriptors and write the required native parts into `<map>.pak`, using `fileIndex = 0xFFFFFFFF` in the fastfile's rebuilt image references. This uses the user's patched PS3 executable convention: the named package sits beside `<map>.ff`. Keep the pair together. The build does not locate `imagefile1.pak` or other donor packages.

The disk-only proof candidate linked successfully: 9,562,336-byte `.ff` and 67,011,138-byte `.pak`, 254 streamed images, zero donor fastfiles, and zero external asset fallbacks. On 2026-09-24 the user reported that this candidate “worked perfectly” on PS3. Streamed sound packaging and turret weapon source import remain outside the current profile.
