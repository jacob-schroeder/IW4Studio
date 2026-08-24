# D3dbspLinker

D3dbspLinker inspects and converts IW4 PS3 version 22 `.d3dbsp` and `.ff` files.
It does not compile `.map` source files.

Run it from the repository root:

```bash
dotnet run --project src/Tools/D3dbspLinker/D3dbspLinker.csproj -- <command> <arguments>
```

Put quotes around paths that contain spaces.

## Commands

| Command | What it does | Expected result |
| --- | --- | --- |
| `inspect <input.d3dbsp>` | Reads a compiled map. | Prints the BSP version and a table of its lumps. Does not write a file. |
| `inspect-fastfile <input.ff>` | Reads a linked fastfile. | Prints asset counts and details about its graphics, collision, lighting, and map entities. Does not write a file. |
| `find-fastfile-assets <input.ff> <name-contains>` | Searches asset names without case sensitivity. | Prints each matching asset's type, source, access, and name. Does not write a file. |
| `inspect-pair <input.d3dbsp> <input.ff>` | Compares a compiled map with its linked fastfile. | Prints matching counts, graph checks, and reversible-lump checks. Does not write a file. |
| `to-d3dbsp <input.ff> <output.d3dbsp>` | Converts a supported fastfile back to a compiled map. | Writes a new `.d3dbsp` and prints its map name, encoding profile, lump count, and byte size. |
| `to-fastfile <input.d3dbsp> <template.ff> <map-asset-name> <output.ff> [--fullbright] [dependency.ff ...]` | Links a supported compiled map into a PS3 fastfile. | Writes a new `.ff` and prints its root counts, dependencies, lighting mode, and byte size. |
| `rewrite <input.d3dbsp> <output.d3dbsp>` | Validates and rewrites a compiled map without converting it. | Writes a fresh copy and prints its output path. |

## `to-fastfile` arguments

- `input.d3dbsp`: the compiled map to link.
- `template.ff`: a working PS3 fastfile that supplies linking settings and reusable assets.
- `map-asset-name`: the internal map name, such as `maps/mp/my_map.d3dbsp`.
- `output.ff`: the new fastfile path.
- `--fullbright`: replaces compiled lighting with white lightmaps. Use it when the BSP has lighting that the current converter cannot preserve.
- `dependency.ff ...`: optional extra fastfiles that can supply missing referenced assets.

Example:

```bash
dotnet run --project src/Tools/D3dbspLinker/D3dbspLinker.csproj -- \
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
dotnet run --project src/Tools/D3dbspLinker/D3dbspLinker.csproj -- \
  to-d3dbsp my_map.ff recovered_map.d3dbsp
```

## Output and errors

- Output commands never overwrite an existing file. Choose a new output path or move the old file first.
- A successful command exits with code `0`.
- Invalid command arguments print the usage list and exit with code `2`.
- Invalid or unsupported data prints a short message beginning with `error:` and exits with code `1`.
- The converter is strict. It stops on unsupported map features instead of writing a fastfile that may be unsafe to load.
