namespace MapConverter.IW3.PC.Rules

open System.Numerics

module internal Iw3PcModelRules =
    /// OpenAssetTools emits IW3 PC D3DCOLOR components in BGRA memory order.
    let DecodeExportVertexColor (bgra: Vector4) =
        Vector4(bgra.Z, bgra.Y, bgra.X, bgra.W)

    /// Maps a source placement to the static entity used by the IW4 world linker.
    let StaticEntityModelName name =
        match name with
        | "candy_cane" | "foliage_xmas_tree" | "giftbox_01" | "giftbox_02"
        | "giftbox_03" | "giftbox_05" | "santa" | "snow_tree_lights01" | "snowman" -> None
        | "vehicle_uaz_winter_destructible" -> Some "vehicle_uaz_winter_static"
        | other -> Some other
