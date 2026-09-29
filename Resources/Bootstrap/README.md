# PS3 map startup assets

`ps3/` is the disk source bundle for the current Rangers/OpFor Free-for-all and Team Deathmatch profile. D3dbspLinker copies it to `bootstrap/ps3/` beside the executable during build and publish. Normal `build` commands load these files automatically and require no donor fastfile or original image package.

The initial startup bundle contained 53 native XModels, 105 XModelSurfs groups, their material/technique/shader/image dependencies, one physics preset, four faction icon materials, and link settings. Bone names and asset references are symbolic; the linker reconstructs pointers and the XAssetList.

The original assets were extracted offline from the local PS3 `mp_terminal.ff` dependency workspace. Native image extraction was completed on 2026-09-24. Version 2 `images/*.image.json` retains each original texture descriptor. `.pixels.bin` contains resident pixels, while `.stream0.bin` through `.stream3.bin` retain active stream parts, including mip tails and padding. These are original GPU payloads, without RGBA expansion, downscaling, or recompression. Decoded DDS is optional editor/authoring data, not a bootstrap build input.

Regenerate into a new staging directory before replacing reviewed sources:

```sh
D3dbspLinker export-bootstrap /path/to/mp_terminal.ff /path/to/raw /path/to/new-bootstrap
```

Extraction may load original dependency zones and their adjacent image packages. It is separate from the ordinary map build command. Missing complete source data fails explicitly.

The 2026-09-24 native-image bundle occupied **118,973,392 bytes (113.46 MiB)**, compared with **460,514,911 bytes (439.18 MiB)** of file contents in the initial decoded-DDS bundle. Image sources occupied 85,520,302 bytes. The earlier approximately 443 MB estimate described disk usage; the exact comparison here sums file lengths. Those 2,263 files comprised 1,198 image metadata/payload files, 576 shader files, 211 techniques, 15 technique sets, 102 materials, 53 models, 105 surface groups, two physics files, and the bootstrap manifest. The old 210 DDS files were removed from the source folder. Later additions are not included in these historical counts.

Normal map builds preserve streamed texture descriptors and write the required native parts into `<map>.pak`, using `fileIndex = 0xFFFFFFFF` in the fastfile's rebuilt image references. This uses the user's patched PS3 executable convention: the named package sits beside `<map>.ff`. Keep the pair together. The build does not locate `imagefile1.pak` or other donor packages.

The disk-only proof candidate linked successfully: 9,562,336-byte `.ff` and 67,011,138-byte `.pak`, 254 streamed images, zero donor fastfiles, and zero external asset fallbacks. On 2026-09-24 the user reported that this candidate “worked perfectly” on PS3. That evidence predates the turret addition below.

## Stock mounted turret — 2026-09-29

`turret_minigun_mp` and the placed `weapon_minigun` model are bundled with their material, texture, shader, physics, FX, sound, tracer and rumble dependencies. Normal map builds resolve this profile automatically from bootstrap plus any configured library overrides; users do not need to extract turret assets from a stock fastfile. Existing build/publish content rules include these files.

Version-1 material exports in an older library do not shadow a same-name bundled native material. The compiler uses bootstrap for those entries and leaves the library files untouched. Version-2 material overrides still take precedence; malformed or unsupported sources still report their errors. Material source readiness uses this same selection rule.

The weapon is owned by both PS3 Karachi (`mp_checkpoint.ff`) and Skidrow (`mp_nightshift.ff`); their legacy weapon text exports are byte-identical. The bundled native graph was extracted from Karachi with `common_mp.ff` supplying its dependency workspace and the existing source library supplying any available fallback assets. The extraction contains 119 asset definitions. Existing identical bootstrap sources are reused.

`weapon_native/turret_minigun_mp.json` preserves native modeled scalars, fixed arrays, null/empty strings and storage presence without retaining runtime addresses. `tracer_native/stryker_50cal_tracer.json` preserves tracer values and its named material. Legacy `WEAPONFILE`/`TRACER` text is also included, but disk linking uses the native JSON. Arbitrary weapon profiles and weapon model/animation/physics/notetrack dependencies outside this bounded turret profile fail explicitly.

The weapon's `heavygun_fire` and `minigun_rumble` definitions, plus their four `.rmb` graphs, are bundled and included as RawFile roots when a turret is built. Other maps do not acquire these turret roots.

Offline regeneration, for maintainers only:

```sh
D3dbspLinker export-assets /path/to/mp_checkpoint.ff /path/to/raw /path/to/new-turret-bundle --weapon turret_minigun_mp --xmodel weapon_minigun --dependencies /path/to/common_mp.ff
```

In IW4Radiant, open the **Weapons** tab (or **Create → Weapons…**) and drag **Mounted minigun** onto a camera surface or grid, or use **Place mounted minigun**. Both this browser and the existing gameplay entity menu create the same `misc_turret` with its model and weapon references already set. The browser reads the bundled native model for its thumbnail and supplies model/material fallbacks for viewport drawing; loaded library assets retain precedence in the scene. No separate model extraction is required.

Evidence: targeted production linker and IW4Radiant compilation, native source extraction, successful saved-map FF builds, and confirmation of all six rumble RawFiles in the output. On 2026-09-29 the user reported successful loading and operation on PS3 hardware after the script-string null-index correction. Detailed browser interactions and importer roundtrip were not independently exercised.

## Stock runtime soccer ball — 2026-09-29

`soccer_ball` is bundled with its native XModel surfaces, material, images, PhysPreset and PhysCollmap. The model appears in the **Models** browser without an extracted model library. Place it, then use **Entity inspector → Runtime physics** or **right-click → Physics → Enable runtime physics**. Disabling the property restores a static model; both changes preserve the transform and support undo.

The supported runtime profile comes from PS3 `mp_favela.ff`, model DynEntity list 0, entry 453: type-1 CLUTTER, Contents=1, zero mass vectors and brush indices, and no destruction FX. Its named `soccer_ball` preset and model collision data are retained. The mapper controls placement and rotation; scale must remain 1. Arbitrary models and custom physics parameters are outside this first profile. Physics → Drop remains an editor placement tool.

Offline extraction with `common_mp.ff` recovered 55 asset definitions. Of 144 source/payload files, 121 matched existing bootstrap files exactly and 23 were added (270,012 bytes). Normal map builds resolve these sources without loading stock fastfiles:

```sh
D3dbspLinker export-assets /path/to/mp_favela.ff /path/to/raw /path/to/new-soccer-bundle --xmodel soccer_ball --dependencies /path/to/common_mp.ff
```

On 2026-09-29 the user built a fastfile containing the authored ball, loaded it on PS3, and confirmed that shooting it made it bounce around. This is user-reported authored-map loading and bullet-response evidence; the candidate was not independently hashed. The user then confirmed that walking into the stock Favela ball does not move it, matching the authored ball, and explicitly accepted RAD-028 as complete and working on PS3 hardware. Explosion response and multiplayer synchronization remain unverified. No physics values were changed for the player-contact comparison.
