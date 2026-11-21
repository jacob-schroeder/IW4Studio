#load "../Collision/SurfaceTypes.fsx"

open System
open System.Globalization
open System.Text.Json

type Placement =
    { source_flags: int
      source_primary_light: Nullable<byte>
      source_reflection_probe: Nullable<byte>
      ground_lighting_rgba: int[]
      lighting_origin: single[]
      cull_distance: Nullable<uint16> }

let placementProperties (p: Placement) =
    let groundLit = p.source_flags &&& 0x20 <> 0
    [ if p.source_flags &&& 0x10 <> 0 then
          yield "spawnflags", "2"
      if groundLit || p.source_primary_light.HasValue then
          let color =
              if groundLit then
                  match p.ground_lighting_rgba with
                  | [| r; g; b; a |] -> sprintf "%02x%02x%02x%02x" b g r a
                  | _ -> invalidArg "ground_lighting_rgba" "A ground-lit prop needs four color channels."
              else "00000000"
          let light = p.source_primary_light
          yield "gndLt", color + (if light.HasValue then sprintf "%02x" light.Value else "")
      if p.source_reflection_probe.HasValue then
          yield "reflectionProbeIndex", string p.source_reflection_probe.Value
      if not (isNull p.lighting_origin) then
          if p.lighting_origin.Length <> 3 || not (Array.forall Single.IsFinite p.lighting_origin) then
              invalidArg "lighting_origin" "A static prop needs three finite lighting-origin coordinates."
          let coordinates = p.lighting_origin |> Array.map (fun v -> v.ToString("R", CultureInfo.InvariantCulture))
          yield "lightingOrigin", String.concat " " coordinates
      // This is already a scaled placement distance. Both engines reserve zero for an uncapped instance.
      if p.cull_distance.HasValue then
          yield "cullDist", string p.cull_distance.Value ]
    |> Map.ofList

type LodRequest = { key: string; sourceDistances: float[]; availableIndices: int[] }
type Inventory = { placements: Placement[]; lods: LodRequest[]; collisionFlags: int[] }

// Keep the last available mesh until Ghosts' terminal distance when a farther
// mesh cannot be recovered. Earlier thresholds remain tied to their source LODs.
let lodDistances (m: LodRequest) =
    m.availableIndices
    |> Array.mapi (fun i sourceIndex ->
        if i = m.availableIndices.Length - 1 then Array.last m.sourceDistances
        else m.sourceDistances[sourceIndex])

let inventory = JsonSerializer.Deserialize<Inventory>(Console.In.ReadToEnd())
let lods = inventory.lods |> Array.map (fun m -> {| key = m.key; distances = lodDistances m |})

let collisionFlags = inventory.collisionFlags |> Array.map (uint32 >> SurfaceTypes.collisionFlags >> int)
JsonSerializer.Serialize({| placements = Array.map placementProperties inventory.placements
                            lods = lods; collisionFlags = collisionFlags |})
|> Console.WriteLine
