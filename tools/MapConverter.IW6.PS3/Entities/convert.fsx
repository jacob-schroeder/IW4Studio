open System
open System.Buffers.Binary
open System.Collections.Generic
open System.Globalization
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions

// Convert the retail PS3 mp_swamp zone recovered from IW6 fastfile version 558.
// Known serialized offsets below belong to this exact map revision.
let args = fsi.CommandLineArgs |> Array.tail
if args.Length <> 2 then
    failwith "Usage: dotnet fsi convert-iw6-swamp-entities.fsx -- <mp_swamp.zone> <output-directory>"

let zone = File.ReadAllBytes(args[0])
let digest = Convert.ToHexString(SHA256.HashData(zone))
if digest <> "101C19B32F44F996A43D4BC277B761C656D30B7B4615C2360FAD0A5007D8D6F8" then
    failwith "This is a different zone revision. Its record offsets require independent recovery."

let destination = Path.GetFullPath(args[1])
let write relative (bytes: byte[]) =
    let path = Path.Combine(destination, relative)
    Directory.CreateDirectory(Path.GetDirectoryName(path)) |> ignore
    if File.Exists(path) then
        if not ((File.ReadAllBytes path).AsSpan().SequenceEqual(bytes)) then
            failwithf "Preserving existing output with different content: %s" path
    else
        File.WriteAllBytes(path, bytes)
let writeText path (source: string) = write path (Encoding.UTF8.GetBytes(source))

let u16 pos = int (BinaryPrimitives.ReadUInt16BigEndian(zone.AsSpan(pos, 2)))
let i32 pos = BinaryPrimitives.ReadInt32BigEndian(zone.AsSpan(pos, 4))
let cstring pos =
    let ending = Array.IndexOf(zone, 0uy, pos)
    if ending < 0 then failwithf "Unterminated string at %x" pos
    Encoding.ASCII.GetString(zone, pos, ending-pos), ending+1
let vector pos =
    [0; 4; 8]
    |> List.map (fun offset ->
        let value = BitConverter.Int32BitsToSingle(i32 (pos + offset))
        if not (Single.IsFinite(value)) then failwithf "Non-finite vector at %x" pos
        value.ToString("R", CultureInfo.InvariantCulture))
    |> String.concat " "

// Script-string slot 0 is null. The remaining inline strings follow the pointer table.
let scriptStringCount = i32 0x28
let mutable nextString = 0x3c + 4 * scriptStringCount
let scriptStrings = Array.init scriptStringCount (fun i ->
    match i32 (0x3c + 4 * i) with
    | 0 -> ""
    | -1 ->
        let name, following = cstring nextString
        nextString <- following
        name
    | other -> failwithf "Unexpected script-string pointer %x" other)

// The PS3 header agrees with the Xbox MapEnts entityString, numEntityChars and
// spawnList fields at +4, +8 and +112. SpawnPointEntityRecord has a 32-byte stride.
let mapEnts = 0x3cd4b73
let entityText, entityEnd = cstring (mapEnts + 128)
if entityEnd - mapEnts - 128 <> i32 (mapEnts + 8) then failwith "MapEnts length mismatch"
// Preserve all DLC token IDs verbatim. Later token dictionaries disagree on
// some level-specific keys. The three core fields used below match iw6_ps.
writeText "source/mp_swamp.ents" entityText

let supportedSpawns =
    set ["mp_dm_spawn"; "mp_tdm_spawn"; "mp_tdm_spawn_allies_start"; "mp_tdm_spawn_axis_start"
         "mp_dom_spawn"; "mp_dom_spawn_allies_start"; "mp_dom_spawn_axis_start"
         "mp_sd_spawn_attacker"; "mp_sd_spawn_defender"]
let spawnCount = u16 (mapEnts + 112)
let spawnTable = 0x3cf7065
let sourceSpawns = ResizeArray<string>(["index,classname,target,script_noteworthy,origin,angles"])
let authoring = ResizeArray<string>(["iwmap 4"; "{"; "\"classname\" \"worldspawn\""; "}"])
let fieldNames =
    Map.ofList [
        "97","classname"; "47","angles"; "430","origin"; "392","model"
        "640","target"; "642","targetname"; "158","dmg"; "542","spawnflags"
        "476","radius"; "272","height"; "27614","script_gameobjectname"
        "27658","script_label"; "27477","script_bombmode_original"; "27562","script_exploder"
        "27417","script_accel"; "27428","script_airspeed"; "755","script_delay"
    ]
let objectiveModels =
    Map.ofList ["mp_missile_bombsite","com_bomb_objective"; "mp_missile_bombsite_destroyed","com_bomb_objective_d";
                "prop_suitcase_bomb","mil_tntbomb_mp"; "com_plasticcase_green_big_us_dirt","com_plasticcase_rangers"]
let point name origin angles =
    authoring.AddRange([|"{"; $"\"classname\" \"{name}\""; $"\"origin\" \"{origin}\""; $"\"angles\" \"{angles}\""; "}"|])
let sourceEntities =
    Regex.Matches(entityText, @"\{\n([^{}]*)\}")
    |> Seq.map (fun block ->
        Regex.Matches(block.Groups[1].Value, @"(?m)^(\d+) ""([^""\r\n]*)""$")
        |> Seq.map (fun field -> field.Groups[1].Value, field.Groups[2].Value) |> Map.ofSeq)
    |> Seq.toArray
let caveBreachEntity (properties: Map<string, string>) =
    let retarget name = properties.Add("642", name)
    if properties.TryFind "642" <> Some "pf646_auto3" then
        None
    else
        match properties.TryFind "97", properties.TryFind "506", properties.TryFind "392" with
        | Some "trigger_use_touch", Some "trigger_use", Some "?14" ->
            Some (retarget "swamp_breach_use")
        | Some "script_brushmodel", Some "delete", Some ("*41" | "*42") ->
            Some (retarget "swamp_breach_blocker")
        | Some "script_model", Some "show_useside_on_use,delete", Some "weapon_c4_mp" ->
            Some ((retarget "swamp_breach_prop").Add("392", "mil_tntbomb_mp"))
        | Some "script_model", Some "delete", Some ("swamp_cave_planks" | "swamp_wood_basement_door" as model) ->
            Some ((retarget "swamp_breach_prop").Add("392", "iw6/mp_swamp/" + model))
        | Some "script_struct", Some "fx", _ ->
            match properties.TryFind "27611" with
            | Some ("vfx_cave_entrance_breach" | "vfx_cellar_door_breach") ->
                Some ((retarget "swamp_breach_effect").Add("97", "script_origin"))
            | _ -> None
        | _ -> None
let writeEntity classname (properties: Map<string, string>) =
    authoring.Add("{")
    for KeyValue(token, value) in properties do
        match fieldNames.TryFind token with
        | Some "model" when classname = "trigger_radius" -> () // IW4 DOM spawns the flag model.
        | Some field ->
            let translated = if field = "model" then objectiveModels.TryFind(value) |> Option.defaultValue value else value
            authoring.Add($"\"{field}\" \"{translated}\"")
        | None -> ()
    authoring.Add("}")
// Preserve the named flight paths and all linked waypoints for IW4's stock killstreak scripts.
let flightTargets = HashSet<string>(
    [ "heli_start"; "heli_leave"; "heli_dest"; "heli_loop_start"; "heli_crash_start"
      "heli_attack_area"; "remoteMissileSpawn"; "airstrikeheight" ])
let mutable addedTarget = true
while addedTarget do
    addedTarget <- false
    for entity in sourceEntities do
        match entity.TryFind "97", entity.TryFind "642", entity.TryFind "640" with
        | Some "script_origin", Some name, Some target when flightTargets.Contains(name) ->
            if flightTargets.Add(target) then addedTarget <- true
        | _ -> ()
for properties in sourceEntities do
    match caveBreachEntity properties, properties.TryFind "97" with
    | Some breach, _ -> writeEntity breach["97"] breach
    | None, Some ("info_player_start" | "mp_global_intermission" as name) ->
        point name properties["430"] properties["47"]
    | None, Some classname when classname = "script_brushmodel" || classname = "trigger_hurt"
                          || properties.TryFind "642" = Some "minimap_corner"
                          || properties.TryFind "642" = Some "flag_primary"
                          || (classname = "script_origin" && (properties.TryFind "642" |> Option.exists flightTargets.Contains))
                          || (classname = "script_model" && List.contains (properties.TryFind "642") [Some "care_package"; Some "airdrop_crate"])
                          || List.contains (properties.TryFind "27614") [Some "sd"; Some "bombzone"] ->
        writeEntity classname properties
    | _ -> ()
let mutable keptSpawns = 0
for i in 0 .. spawnCount-1 do
    let pos = spawnTable + 32*i
    if u16 pos <> i then failwithf "Unexpected spawn record %d" i
    let name = scriptStrings[u16 (pos+2)]
    let target = scriptStrings[u16 (pos+4)]
    let note = scriptStrings[u16 (pos+6)]
    let origin, angles = vector (pos+8), vector (pos+20)
    sourceSpawns.Add($"{i},{name},{target},{note},{origin},{angles}")
    if supportedSpawns.Contains(name) then
        if target <> "" || note <> "" then failwithf "Unhandled spawn fields on %d" i
        point name origin angles
        keptSpawns <- keptSpawns + 1
writeText "source/mp_swamp-spawns.csv" (String.concat "\n" sourceSpawns + "\n")
writeText "iw4/map_source/mp_swamp.map" (String.concat "\n" authoring + "\n")
write "iw4/raw/maps/mp/mp_swamp.gsc" (File.ReadAllBytes(Path.Combine(__SOURCE_DIRECTORY__, "mp_swamp.gsc")))

let rawFile nameOffset expectedName =
    let name, start = cstring nameOffset
    let size = i32 (nameOffset-12)
    if name <> expectedName || i32 (nameOffset-16) <> -1 || i32 (nameOffset-4) <> -1 then
        failwithf "Unexpected RawFile header at %x" nameOffset
    use input = new MemoryStream(zone, start, size, false)
    use inflater = new ZLibStream(input, CompressionMode.Decompress)
    use output = new MemoryStream()
    inflater.CopyTo(output)
    if output.Length <> int64 (i32 (nameOffset-8)) then failwithf "RawFile length mismatch for %s" name
    Encoding.ASCII.GetString(output.ToArray()).Replace("\r\n", "\n")

let fogArguments (fields: Map<string, string>) =
    let baseFog = ["startDist"; "halfwayDist"; "red"; "green"; "blue"; "maxOpacity"; "transitionTime"]
    // Both PS3 engines multiply fog density by the final directional scale.
    let sunFog = ["sunRed"; "sunGreen"; "sunBlue"; "sunDir"; "sunBeginFadeAngle"; "sunEndFadeAngle"; "normalFogScale"]
    let keys = if fields["sunFogEnabled"] = "1" then baseFog @ sunFog else baseFog
    keys |> List.map (fun key -> fields[key]) |> String.concat ", "

let fogSource = rawFile 0x9d64b "maps/createart/mp_swamp_fog.gsc"
let fogSection = fogSource.Split("create_vision_set_fog( \"mp_swamp\" );").[1].Split("create_vision_set_fog").[0]
let fogFields =
    Regex.Matches(fogSection, @"ent\.(\w+)\s*=\s*([^;\r\n]+);")
    |> Seq.map (fun m -> m.Groups[1].Value, m.Groups[2].Value.Trim()) |> Map.ofSeq
writeText "iw4/raw/maps/mp/mp_swamp_fog.gsc"
    (String.concat "\n" ["main()"; "{"; "\tmaps\\mp\\_compass::setupMiniMap( \"compass_map_mp_swamp\" );"
                         $"\tsetExpFog( {fogArguments fogFields} );"; "\tVisionSetNaked( \"mp_swamp\", 0 );"; "}"; ""])

let createPath = "maps/createfx/mp_swamp_fx.gsc"
let registryPath = "maps/mp/mp_swamp_fx.gsc"
let createFx = rawFile 0x99abd createPath
let registry = rawFile 0x8cc93 registryPath
let registrations = Regex.Matches(registry, @"(?m)^\s*level\._effect\[\s*""([^""]+)""\s*\]\s*=\s*loadfx\(\s*""([^""]+)""\s*\);")
let effectNames = registrations |> Seq.map (fun m -> m.Groups[1].Value, m.Groups[2].Value) |> Map.ofSeq
let used = HashSet<string>()
let statements = ResizeArray<string>()
let decl = Regex(@"(?m)^\tent = (\w+)\(.*?\);$")
let starts = decl.Matches(createFx) |> Seq.toArray
let transform = Regex(@"(?m)^\tent set_origin_and_angles\( \(([^)]+)\), \(([^)]+)\) \);$")
let mutable loopSounds = 0
let mutable oneShots = 0
for i in 0 .. starts.Length-1 do
    let entry = starts[i]
    let ending = if i+1 < starts.Length then starts[i+1].Index else createFx.LastIndexOf('}')
    let body = createFx.Substring(entry.Index, ending-entry.Index).TrimEnd()
    match entry.Groups[1].Value with
    | "createLoopSound" | "createOneshotEffect" ->
        if transform.Matches(body).Count <> 1 then failwithf "Missing placement transform at %d" entry.Index
        if entry.Groups[1].Value = "createOneshotEffect" then
            let id = Regex.Match(body, @"ent\.v\[ ""fxid"" \] = ""([^""]+)"";").Groups[1].Value
            if not (effectNames.ContainsKey(id)) then failwithf "Unknown effect %s" id
            used.Add(id) |> ignore
            oneShots <- oneShots+1
        else loopSounds <- loopSounds+1
        statements.Add(transform.Replace(body, "\tent.v[ \"origin\" ] = ( ${1} );\n\tent.v[ \"angles\" ] = ( ${2} );"))
    | "createReactiveEnt" | "createIntervalSound" | "createExploder" -> ()
    | name -> failwithf "Unknown source CreateFX constructor %s" name
let createHeader = "#include common_scripts\\utility;\n#include common_scripts\\_createfx;\nmain()\n{\n"
writeText ("iw4/raw/"+createPath) (createHeader + String.concat "\n\n" statements + "\n}\n")
let registrationLines =
    registrations |> Seq.filter (fun m -> used.Contains(m.Groups[1].Value))
    |> Seq.map (fun m -> "\t"+m.Value.Trim()) |> String.concat "\n"
let callCreateFx = "\n/#\n\tif ( getdvar( \"clientSideEffects\" ) != \"1\" )\n\t\tmaps\\createfx\\mp_swamp_fx::main();\n#/\n}\n"
writeText ("iw4/raw/"+registryPath) ("main()\n{\n" + registrationLines + callCreateFx)
printfn "%d/%d multiplayer spawns, %d one-shot FX placements (%d definitions referenced), %d looping sounds." keptSpawns spawnCount oneShots used.Count loopSounds
