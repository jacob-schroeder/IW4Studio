namespace MapConverter.Game.IW3.PS3.Rules

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open IW4.Game.Assets.Image
open IW4.Game.Assets.Localize

module internal MapUiRules =
    let ZoneName(mapName: string) = mapName + "_ui"
    let PreviewName(mapName: string) = "preview_" + mapName

    /// The stock map table owns the localization keys and preview material
    /// name; do not derive them from a map's filename or substitute a load FF.
    let SourceRow(mapName: string, columns: int, cells: IReadOnlyList<string>) =
        if columns < 7 || cells.Count % columns <> 0 then
            raise (InvalidDataException("COD4 mp/mapstable.csv has an unsupported layout."))
        let rows =
            [0 .. cells.Count / columns - 1]
            |> List.filter (fun row -> cells[row * columns] = mapName)
        match rows with
        | [row] ->
            // COD4 ui_mp/map_info.inc consumes columns 3/4/6 respectively.
            let values = [| cells[row * columns + 3]; cells[row * columns + 4]; cells[row * columns + 6] |]
            if values |> Array.exists String.IsNullOrWhiteSpace then
                raise (InvalidDataException($"COD4 map table has incomplete UI metadata for '{mapName}'."))
            struct (values[0], values[1], values[2])
        | _ -> raise (InvalidDataException($"COD4 map table requires exactly one row for '{mapName}'."))

    let StringFile(key: string) =
        let separator = key.IndexOf '_'
        if separator <= 0 || (key |> Seq.exists (fun c -> not (Char.IsAsciiLetterOrDigit c || c = '_'))) then
            raise (InvalidDataException($"Invalid COD4 localization key '{key}'."))
        key.Substring(0, separator).ToLowerInvariant() + ".str"

    /// Read the source StringEd English value verbatim, including whitespace
    /// inside quotes. The PS3 output uses the existing English language mask.
    let EnglishText(key: string, source: string) =
        StringFile key |> ignore
        let wanted = key.Substring(key.IndexOf('_') + 1)
        let mutable current = ""
        let values = ResizeArray<string>()
        use reader = new StringReader(source)
        let mutable line = reader.ReadLine()
        while not (isNull line) do
            let parts = line.Trim().Split([| ' '; '\t' |], 2, StringSplitOptions.RemoveEmptyEntries)
            if parts.Length = 2 then
                if parts[0] = "REFERENCE" then
                    current <- parts[1].Trim().Trim('"')
                elif parts[0] = "LANG_ENGLISH" && current = wanted then
                    let value = JsonSerializer.Deserialize<string>(parts[1].Trim())
                    if String.IsNullOrWhiteSpace value || value.Contains '\000' then
                        raise (InvalidDataException($"COD4 localization '{key}' has no usable English value."))
                    values.Add value
            line <- reader.ReadLine()
        if values.Count <> 1 then
            raise (InvalidDataException($"COD4 StringEd source requires exactly one English value for '{key}'."))
        values[0]

    let Localizations(mapName: string, title: string, description: string, factions: FactionProfile) =
        [| LocalizeAsset(Name = mapName, Value = title)
           LocalizeAsset(Name = mapName + "_desc", Value = description)
           LocalizeAsset(Name = mapName + "_factions", Value = FactionRules.ProfileName factions) |]

    /// Frontend previews must be resident before the map or its named PAK is
    /// loaded. Retain PS3 pixels and use the native custom-UI image profile.
    let PreviewImage(mapName: string, root: ReadOnlySpan<byte>, payload: ReadOnlyMemory<byte>) =
        let image = ImageRules.ResidentImage(PreviewName mapName, root, payload, true)
        if image.MapType <> MapType.TwoDimensional ||
           image.DimensionCount <> GfxImageDimension.TwoDimensional ||
           image.IsCubemap || image.Depth <> 1us || image.Width = 0us || image.Height = 0us ||
           image.TextureControl1 <> 0x0001aae4u || image.LevelCount = 0uy then
            raise (InvalidDataException($"COD4 preview for '{mapName}' has an unsupported image profile."))
        let expected = GfxImagePixelLayout.ComputePayloadByteCount(
            image.FormatEncoding, image.LevelCount, false, image.TextureRemap,
            image.Width, image.Height, image.Depth)
        if payload.Length <> expected then
            raise (InvalidDataException($"COD4 preview for '{mapName}' has an invalid pixel payload size."))
        image
