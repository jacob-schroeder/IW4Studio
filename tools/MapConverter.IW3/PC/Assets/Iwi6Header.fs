namespace MapConverter.Game.IW3.PC.Assets

open System
open System.Buffers.Binary
open System.IO

type internal Iwi6Format =
    | Rgba = 0x01uy
    | Rgb = 0x02uy
    | LuminanceAlpha = 0x03uy
    | Alpha = 0x05uy
    | Luminance = 0x04uy
    | Dxt1 = 0x0buy
    | Dxt3 = 0x0cuy
    | Dxt5 = 0x0duy

[<Flags>]
type internal Iwi6Flags =
    | NoPicMip = 0x01uy
    | NoMipMaps = 0x02uy
    | CubeMap = 0x04uy
    | Volume = 0x08uy
    | LegacyNormals = 0x20uy
    | ClampU = 0x40uy
    | ClampV = 0x80uy

type internal Iwi6Header =
    { Format: Iwi6Format
      Flags: Iwi6Flags
      Width: uint16
      Height: uint16
      Depth: uint16
      FileSizeForPicmip: uint32 array }

    static member Size = 0x1c

    static member Read(source: ReadOnlySpan<byte>, name: string) =
        if source.Length < Iwi6Header.Size then
            raise (InvalidDataException($"IWI6 image '{name}' is shorter than its 0x1C-byte header."))
        if source[0] <> byte 'I' || source[1] <> byte 'W' || source[2] <> byte 'i' then
            raise (InvalidDataException($"Invalid IWI magic in '{name}'."))
        if source[3] <> 6uy then
            raise (NotSupportedException($"Image '{name}' has IWI version {source[3]}. IW3 requires version 6."))

        let dimension field (value: uint16) =
            if value = 0us then
                raise (InvalidDataException($"IWI6 image '{name}' has zero {field}."))
            value

        let sizes = Array.zeroCreate<uint32> 4
        for i in 0 .. sizes.Length - 1 do
            sizes[i] <- BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(0x0c + i * 4))
        { Format = LanguagePrimitives.EnumOfValue source[4]
          Flags = LanguagePrimitives.EnumOfValue source[5]
          Width = dimension "width" (BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(6)))
          Height = dimension "height" (BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(8)))
          Depth = dimension "depth" (BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(10)))
          FileSizeForPicmip = sizes }

    member header.Write(target: Span<byte>) =
        "IWi"B.AsSpan().CopyTo(target)
        target[3] <- 6uy
        target[4] <- byte header.Format
        target[5] <- byte header.Flags
        BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(6), header.Width)
        BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(8), header.Height)
        BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(10), header.Depth)
        for i in 0 .. 3 do
            BinaryPrimitives.WriteUInt32LittleEndian(target.Slice(0x0c + i * 4), header.FileSizeForPicmip[i])
