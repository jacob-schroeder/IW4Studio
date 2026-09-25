# D3dbspLinker

D3dbspLinker inspects and converts IW4 PS3 version 22 `.d3dbsp` and `.ff` files.
It does not compile `.map` source files.

Run it from the repository root:

```bash
dotnet run --project tools/D3dbspLinker/D3dbspLinker.csproj -- <command> <arguments>
```

Put quotes around paths that contain spaces.

## Commands

| Command | What it does | Expected result |
| --- | --- | --- |
| `inspect <input.d3dbsp>` | Reads a compiled map. | Prints the BSP version and a table of its lumps. Does not write a file. |
| `inspect-fastfile <input.ff>` | Reads a linked fastfile. | Prints asset counts and details about its graphics, collision, lighting, and map entities. Does not write a file. |
| `find-fastfile-assets <input.ff> <name-contains>` | Searches asset names without case sensitivity. | Prints each matching asset's type, source, access, and name. Does not write a file. |
| `list-emitter-assets <input.ff>` | Lists FX definitions and sound aliases owned by a PS3 fastfile. | Prints a JSON array of exact `type` and `name` pairs for the Radiant browser. Does not write a file. |
| `inspect-pair <input.d3dbsp> <input.ff>` | Compares a compiled map with its linked fastfile. | Prints matching counts, graph checks, and reversible-lump checks. Does not write a file. |
| `to-d3dbsp <input.ff> <output.d3dbsp>` | Converts a supported fastfile back to a compiled map. | Writes a new `.d3dbsp` and prints its map name, encoding profile, lump count, and byte size. |
| `to-fastfile <input.d3dbsp> <template.ff> <map-asset-name> <output.ff> [--fullbright] [dependency.ff ...]` | Links a supported compiled map into a PS3 fastfile. | Writes a new `.ff` and prints its root counts, dependencies, lighting mode, and byte size. |
| `rewrite <input.d3dbsp> <output.d3dbsp>` | Validates and rewrites a compiled map without converting it. | Writes a fresh copy and prints its output path. |

## Normal disk map builds

```bash
D3dbspLinker build my_map.d3dbsp maps/mp/my_map.d3dbsp my_map.ff \
  --asset-library /path/to/raw --compiled-lighting
```

`build` loads the included `bootstrap/ps3` source bundle and resolves authored map/model/FX assets from the selected library. It does not open donor, template, or supplement fastfiles. Missing or old native source fails with the dependency name. The library must contain native model geometry and physics as well as materials and emitter sources. The repository bootstrap directory is copied into build/publish output automatically.

This profile includes the current Rangers/OpFor startup set. Streamed sound package output and `misc_turret` weapon source import are not yet supported by disk builds and produce explicit errors. Loaded sound aliases are supported. Native image sources preserve GPU compression and original stream profiles. The build writes `<map>.ff` and, when needed, `<map>.pak`. The package contains all selected streamed textures and uses `fileIndex = 0xFFFFFFFF`, matching the patched PS3 executable's same-basename lookup. Keep both files together. The user reported that the disk-only proof pair worked on PS3 on 2026-09-24.

Offline migration commands are separate from normal builds:

- `export-bootstrap <official-map.ff> <existing-raw-root> <new-output-directory>` extracts the fixed startup graph and settings, using the official dependency lifecycle and native resident/streamed texture payloads. Original image packages must be available beside the extraction input (or in its parent directory).
- `export-assets <linked.ff> <existing-raw-root> <new-output-directory> [--xmodel <name>]... [--material <name>]... [--fx <name>]...` extracts requested complete model, material, and FX graphs. At least one `--xmodel`, `--material`, or `--fx` is required. It opens the input in isolation; optional `--dependencies <official.ff>` supplies a native dependency workspace during extraction. Shared material/shader/image references can also resolve from the disk library and included bootstrap. FX extraction follows child effects, runner effects, visual materials and models, mark materials, and sound alias links. Missing complete dependencies fail explicitly. It includes native model geometry, physics, material, technique, shader, and image sources; native images retain their original descriptors and exact stream part bytes. Extraction needs the original adjacent image and streamed sound packages; normal builds only read exported files. This command writes native image sources; use Studio's source dump to add decoded DDS previews when needed.

The legacy `to-fastfile` conversion command retains its template/provider workflow for its existing conversion consumers. Radiant uses `build`.

## Legacy `to-fastfile` arguments

- `input.d3dbsp`: the compiled map to link.
- `template.ff`: a working PS3 fastfile that supplies linking settings and reusable assets.
- `map-asset-name`: the internal map name, such as `maps/mp/my_map.d3dbsp`.
- `output.ff`: the new fastfile path.
- `--fullbright`: replaces compiled lighting with white lightmaps. Use it when the BSP has lighting that the current converter cannot preserve.
- `dependency.ff ...`: optional extra fastfiles that can supply missing referenced assets.
- `--provider-fastfile <path>`: add a native provider for assets not yet covered by source import, such as models. Without `--asset-library`, legacy conversion can also resolve materials and emitters from these files.
- `--fx <name>` and `--sound <alias>`: request exact FX and sound assets.
- `--asset-library <raw-root>`: compile map models and their physics, materials, FX visual dependencies, and requested FX/sound assets from one disk library. This enables `--source-materials`. Material dependencies include techsets, techniques, PS3 shaders, and inline images. Missing or obsolete source data fails with its asset name and file requirement; these assets do not fall back to fastfiles. The legacy conversion command still obtains bootstrap settings/assets and unsupported asset families from its template/providers; use `build` for the disk-only path.
- Streamed sound rows keep their package offsets and metadata; this command does not build `packfileN.pak` from streamed audio sidecars. Use a loaded alias for a self-contained proof fastfile until streamed packaging is implemented.
- `--rawfile <wire-name=source-path>`: package an authored script at its in-game name. When `maps/mp/<map>_fx.gsc` is supplied, the generated default map `main()` calls it before `maps\mp\_load::main()`. A custom map `main()` is preserved and must make that same call before `_load` starts emitter playback. Radiant-generated CreateFX scripts register their placements only once per map load, including when a custom script repeats the call; separate authored markers remain separate emitters.

Source assets are produced by Studio's **Tools → Dump Source Assets** as a separate extraction operation. Re-export older material/technique/shader/image dumps before using this path:

| Asset | Source files |
| --- | --- |
| Model | `xmodel_native/<name>.json` and referenced `xmodelsurfs_native/<name>.json`; editor preview `xmodel_export` remains separate |
| Physics | `physic/<name>.physic.json` and `phys_collmaps/<name>.phys_collmap.json` |
| Material | `materials/<name>.json`, IW4 PS3 material version 2; editable state fields remain authoritative |
| Technique set | `techsets/<name>.techset.json` with symbolic technique references |
| Technique | `techniques/<name>.tech.json` with native passes and argument groups |
| PS3 shader | `shader_bin_ps3/<vertex\|pixel>/<name-with-cg-extension>` and matching `.cg.json` metadata |
| Image | `images/<name>.image.json` version 2 plus `.pixels.bin` and active `.stream0.bin`–`.stream3.bin`; version 1 metadata + DDS remains available for authored pixels |
| FX / sound | `fx/<name>.json`, `soundaliases/<name>.json`, and their exported audio/curve dependencies |

Studio also writes decoded DDS previews when the format can represent the image. Version 2 metadata explicitly selects native payloads, so editing a preview DDS does not silently change native build data. Use version 1 image-source metadata for an authored DDS, or import an authored image through the existing image workflow.

Technique JSON replaces the incomplete OAT `.tech`/`.techset` export for this native linking path. Existing text dumps are not read. The compiler uses the material's final render-state bits and does not need the original authoring state map. This phase consumes existing PS3 shader bytecode; it does not compile new shader source text.

Example:

```bash
dotnet run --project tools/D3dbspLinker/D3dbspLinker.csproj -- \
  to-fastfile \
  my_map.d3dbsp \
  mp_terminal.ff \
  maps/mp/my_map.d3dbsp \
  my_map.ff \
  --fullbright \
  iw4_credits.ff \
  mp_subbase.ff
```

To convert it back:

```bash
dotnet run --project tools/D3dbspLinker/D3dbspLinker.csproj -- \
  to-d3dbsp my_map.ff recovered_map.d3dbsp
```

## Output and errors

- Output commands never overwrite an existing file. Choose a new output path or move the old file first.
- A successful command exits with code `0`.
- Invalid command arguments print the usage list and exit with code `2`.
- Invalid or unsupported data prints a short message beginning with `error:` and exits with code `1`.
- The converter is strict. It stops on unsupported map features instead of writing a fastfile that may be unsafe to load.
