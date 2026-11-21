// Retail PS3 surface tables agree through metal (13) and again from fruit (27).
// Ghosts adds metal_grate at 14. IW4's closest contact material is metal.
let private targetType = function
    | 14 -> 13
    | n when n >= 15 && n <= 26 -> n - 1
    | n -> n

let collisionFlags (flags: uint32) =
    let index = int ((flags >>> 20) &&& 31u)
    (flags &&& ~~~0x01f00000u) ||| (uint32 (targetType index) <<< 20)

// Material bits are one-hot, starting at surface type 1. Layered materials
// can contain multiple types, and metal and metal_grate can coalesce.
let materialBits (bits: uint32) =
    [1..32] |> List.fold (fun mask index ->
        if bits &&& (1u <<< (index - 1)) = 0u then mask
        else mask ||| (1u <<< (targetType index - 1))) 0u
