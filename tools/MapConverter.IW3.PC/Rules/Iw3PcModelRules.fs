namespace MapConverter.IW3.PC.Rules

open System.Numerics

module internal Iw3PcModelRules =
    /// OpenAssetTools emits IW3 PC D3DCOLOR components in BGRA memory order.
    let DecodeExportVertexColor (bgra: Vector4) =
        Vector4(bgra.Z, bgra.Y, bgra.X, bgra.W)
