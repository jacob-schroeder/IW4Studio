# PS3 map startup assets

`ps3/` is the disk source bundle for the current Rangers/OpFor Free-for-all and Team Deathmatch profile. D3dbspLinker copies it to `bootstrap/ps3/` beside the executable during build and publish. Normal `build` commands load these files automatically and require no donor fastfile or original image package.

Full IW3 PC and IW3 PS3 conversions accept `--factions us/russia` (the default Rangers/Russian airborne bundle) or `--factions tf141/opfor` (Afghan's desert TF141/Middle Eastern OpFor bundle). F# `FactionRules` selects the model and faction-icon roots together with the map GSC's `game["allies"]` and `game["axis"]` values. The Afghan native IDs are `socom_141_desert` and `opforce_composite`. Its player classes include assault, SMG, LMG, shotgun, sniper, and riot shield, with their heads, viewhands, and faction cases. These profiles use the game's existing character and mptype scripts.

The Afghan sources were extracted on 2026-11-14 from PS3 `mp_afghan.ff` with `common_mp.ff` dependencies: 37 model roots and four faction-icon materials. The extraction added 1,195 resource files and reused 1,031 byte-identical files without replacing existing payloads. Only the selected profile is linked into a converted map.

`<map>_ui.ff` includes Localize `<map>_factions`, whose value is the selected profile name. The matching custom-map loader reads that value at startup and selects the arena's faction IDs and ghillie environment together: urban for `us/russia`, desert for `tf141/opfor`. Custom PC maps supply this faction key in their UI fastfile. Older UI bundles without the key keep the existing Rangers/Russia fallback; authored arena rows still take priority. Selecting TF141/OpFor requires this loader update because the previous loader's generated arena rows force Rangers/Russia over map-script defaults.

The initial startup bundle contained 53 native XModels, 105 XModelSurfs groups, their material/technique/shader/image dependencies, one physics preset, four faction icon materials, and link settings. Bone names and asset references are symbolic; the linker reconstructs pointers and the XAssetList.

The original assets were extracted offline from the local PS3 `mp_terminal.ff` dependency workspace. Native image extraction was completed on 2026-09-24. Version 2 `images/*.image.json` retains each original texture descriptor. `.pixels.bin` contains resident pixels, while `.stream0.bin` through `.stream3.bin` retain active stream parts, including mip tails and padding. These are original GPU payloads, without RGBA expansion, downscaling, or recompression. Decoded DDS is optional editor/authoring data, not a bootstrap build input.

Regenerate into a new staging directory before replacing reviewed sources:

```sh
D3dbspLinker export-bootstrap /path/to/mp_terminal.ff /path/to/raw /path/to/new-bootstrap
```

Extraction may load original dependency zones and their adjacent image packages. It is separate from the ordinary map build command. Missing complete source data fails explicitly.

The 2026-09-24 native-image bundle occupied **118,973,392 bytes (113.46 MiB)**, compared with **460,514,911 bytes (439.18 MiB)** of file contents in the initial decoded-DDS bundle. Image sources occupied 85,520,302 bytes. The earlier approximately 443 MB estimate described disk usage; the exact comparison here sums file lengths. Those 2,263 files comprised 1,198 image metadata/payload files, 576 shader files, 211 techniques, 15 technique sets, 102 materials, 53 models, 105 surface groups, two physics files, and the bootstrap manifest. The old 210 DDS files were removed from the source folder. Later additions are not included in these historical counts.

Normal map builds preserve streamed texture descriptors and write the required native parts into `<map>.pak`, using `fileIndex = 0xFFFFFFFF` in the fastfile's rebuilt image references. This uses the user's patched PS3 executable convention: the named package sits beside `<map>.ff`. Keep the pair together. The build does not locate `imagefile1.pak` or other donor packages.

Both full conversion paths include `ps3/callsigns.ff`, a scoped native UI bundle with the two callsign tables and their 439 material roots (362 owned, 77 resident-global references). It preserves the stock artwork's original imagefile stream references; these images use the game's installed native image packages, rather than `<map>.pak`. Conversion does not need those packages locally. The bundle was extracted from the working Bloc callsigns11 graph on 2026-11-13; it contains only the selected roots and their linked dependencies. Its SHA-256 is `2d9822fd15f9262bf3599e2b991da12d7f8a461cb485510f9d29fbbf278eef8b`.

F# `SoundRules.NativeRuntimeAliases` supplies the native map-owned UI pulse, rocket surface/layer, snow bullet-impact, player footstep, landing, cargo-container and tire sound aliases. The 57 source definitions come from the native PS3 `mp_terminal.ff` asset library. Their original alias names, playback fields and sample bytes are preserved. The sources use comma references for the 33 loaded samples and four curves resolved by the stock `common_mp` load; common explosion layers, gear-rattle aliases and sprint dirt/sand aliases remain resident. Seven additional loaded samples are bundled, and the six streamed sand-explosion layers are relocated into `<map>_snd.pak`. This bootstrap covers engine-requested sounds that map GSC scanning cannot discover. Normal conversion uses only these bundled sources, without opening a stock MW2 map.

F# `TechniqueRules.NativeLocalLight` converts the ordinary `w_l_sm_r0c0n0s0` and blended `l_sm_r0c0n0s0_b1c1n1` / `l_sm_r0c0n0s0_b1c1n1s1` local-light passes together. The two `iw4_local_light_*` technique sets contain only native IW4 spot, omni, and shadowed-spot passes (slots 27–29), backed by the original PS3 shaders from the local native asset library. The rule checks source state words, exclusive state ownership, vertex declarations, and shader bindings before replacing those passes and their shared blend state. Other passes remain unchanged; other source families retain existing conversion behavior. On 2026-11-14 the user confirmed that Backlot Candidate05 removed the muzzle-flash lighting cutoffs at all reported locations. These bundled sources let MapConverter apply the same transformation without a separate candidate script or donor fastfile.

The `iw4_sun_shadow_*` partial technique sets supply native IW4 sun-shadow receiver passes (slots 13–14) for those same opaque world families and the three-layer `l_sm_r0c0n0s0_b1c1n1s1_b2c2n2s2` family. Each includes the matching normal-fog and depth-fog vertex/pixel programs and argument tables. The F# sun rule gives these slots a dedicated native render state, preserving the source state shared by other passes and the accepted local-light conversion. Source textures and material constants remain map-owned. Native shader bytes come from the existing PS3 asset library; no shader recompilation is involved. This addresses the identified IW3 depth-based versus IW4 atlas-coverage shadow transition difference. On 2026-11-14 the user confirmed that Crossfire Candidate05 fixed the reported world sun shadows. The three-layer extension addresses an adjacent ground surface that retained its source pass and render state; its visual result is pending hardware confirmation. Other world families and transparent materials retain their existing sun conversion.

The sun rule also covers the opaque model families `m_l_sm_r0c0n0s0`, `mc_l_sm_r0c0n0s0`, and `mc_l_sm_t0c0n0s0`, selected by shader contract rather than model name. These native passes retain model-lighting sampling, including its alpha input, and copy the matching vertex declarations, argument groups and pixel bindings. Their dedicated gamma-enabled states preserve model depth writes, authored back-face or two-sided culling and, for the alpha-tested family, GE128 cutouts. The first two native families use a distinct depth-fog pass; the native `mc_l_sm_t0c0n0sf0` donor for the third aliases the ordinary pass in both slots, which the rule preserves. The rule leaves caster flags and source states used by other passes unchanged. Native comparators are `m/me_window_01b`, `mc/mtl_chainlink_fence_pole` and `mc/mtl_stalingradpowerpole` in the existing PS3 asset library. These model extensions await hardware confirmation; other model families retain existing conversion.

The IW3 PS3 audio path uses the bundled `ambient_airport_tarmac_l`, `ambient_airport_tarmac_r`, and `emt_fly_loop` definitions as native alias metadata templates, following the working Bloc audio conversion. Their payload sidecars satisfy the existing source importer. Converted maps use the original COD4 samples, copied from their source fastfile or `packfileN.pak` into `<map>_snd.pak`; the donor audio is not emitted. F# `SoundRules` selects playback/channel flags and map-owned alias names. Source volume, pitch, distance, loop state, and secondary/chain alias relationships are retained.

Full IW3 PC conversion uses the same PS3 sound rules and runtime aliases. It resolves map-script and shared prefab sounds from the custom map and stock PC fastfiles, follows their secondary and chain aliases, and writes their audio to `<map>_snd.pak`. Streamed samples come from the source IWD archives. The converter preserves existing MP3 frames and encodes PCM audio in memory with the bundled LAME library.

The disk-only proof candidate linked successfully: 9,562,336-byte `.ff` and 67,011,138-byte `.pak`, 254 streamed images, zero donor fastfiles, and zero external asset fallbacks. On 2026-09-24 the user reported that this candidate “worked perfectly” on PS3. That evidence predates the turret addition below.

## Stock mounted turret — 2026-09-29

`turret_minigun_mp` and the placed `weapon_minigun` model are bundled with their material, texture, shader, physics, FX, sound, tracer and rumble dependencies. Normal map builds resolve this profile automatically from bootstrap plus any configured library overrides; users do not need to extract turret assets from a stock fastfile. Existing build/publish content rules include these files.

MapConverter's full IW3 PC and IW3 PS3 conversions replace mounted SAW entities with this turret. Both paths reuse the seven generic turret sound aliases already owned by IW4 `common_mp.ff`. The shared F# `SoundRules` keeps the ten map-specific `minigun_gatling_*` aliases owned by the converted map, with their existing external sample and curve references. This ownership split applies to every converted map using the native turret profile. On 2026-11-13, the user confirmed Pipeline Candidate09 worked with the mounted minigun restored after this shared audio graph was omitted; the individual failing asset within that graph remains unidentified.

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
