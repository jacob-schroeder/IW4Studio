namespace MapConverter.Game.IW3.PS3.Rules

open System
open System.Buffers.Binary
open System.IO
open IW4.Game.Assets.Image
open IW4.Game.Codecs.GfxMap

module internal ImageRules =
    /// The inspected COD4 reflection consumer multiplies RGB by alpha;
    /// native IW4 consumers inspected here ignore it. Bake it into an owned copy,
    /// preserving native texel order and each face's alignment padding.
    /// Pipeline Candidate10 validated this correction on PS3. No extra
    /// brightness gain or inferred gamma/authoring transfer is applied.
    let ReflectionProbePixels(image: GfxImageAsset) =
        ArgumentNullException.ThrowIfNull image
        let edge = GfxReflectionProbeCodec.ReflectionProbeEdgeLength
        let mipCount = GfxReflectionProbeCodec.ReflectionProbeMipCount
        let faceCount = GfxReflectionProbeCodec.ReflectionProbeFaceCount
        if image.Format <> byte GfxImageBaseFormat.A8R8G8B8 ||
           image.DimensionCount <> GfxImageDimension.TwoDimensional ||
           image.MultiFaceControl <> 1uy || image.TextureControl1 <> 0x0001AAE4u ||
           int image.Width <> edge || int image.Height <> edge || image.Depth <> 1us ||
           int image.LevelCount <> mipCount || image.UseSrgbReads <> 0uy ||
           image.Cached <> GfxImageCached.No ||
           (image.StreamData |> Seq.exists (fun part -> part.HasStreamingData)) then
            raise (InvalidDataException($"IW3 PS3 reflection probe '{image.Name}' has an unsupported image profile."))
        let payloadSize =
            GfxImagePixelLayout.ComputePayloadByteCount(
                image.FormatEncoding, image.LevelCount, true, image.TextureRemap,
                image.Width, image.Height, image.Depth)
        if image.PayloadByteCount <> payloadSize || image.PayloadBytes.Count <> payloadSize then
            raise (InvalidDataException($"IW3 PS3 reflection probe '{image.Name}' has an invalid payload size."))
        let faceStride = payloadSize / faceCount
        let facePixelBytes =
            [0 .. mipCount - 1]
            |> List.sumBy (fun mip -> let size = max 1 (edge >>> mip) in size * size * 4)
        let pixels = image.PayloadBytes |> Seq.toArray
        for face in 0 .. faceCount - 1 do
            let start = face * faceStride
            for offset in start .. 4 .. start + facePixelBytes - 4 do
                // Native A8R8G8B8 bytes are ARGB, including swizzled mips.
                let alpha = int pixels[offset]
                for channel in 1 .. 3 do
                    pixels[offset + channel] <- byte ((int pixels[offset + channel] * alpha + 127) / 255)
                pixels[offset] <- 255uy
        pixels

    /// Translate a resident COD4 PS3 image root. The copied pixels belong to
    /// the zone: a nonzero cache byte selects IW4's independent card-memory
    /// release path and caused the isolated Bloc sky-loading stall.
    /// Streamed images are constructed by the existing streaming compiler.
    let ResidentImage(name: string, root: ReadOnlySpan<byte>, payload: ReadOnlyMemory<byte>, useSrgbReads: bool) =
        let width = BinaryPrimitives.ReadUInt16BigEndian(root.Slice 12)
        let height = BinaryPrimitives.ReadUInt16BigEndian(root.Slice 14)
        let depth = BinaryPrimitives.ReadUInt16BigEndian(root.Slice 16)
        GfxImageAsset(
            Name = name,
            Format = root[4],
            LevelCount = root[5],
            DimensionCount = LanguagePrimitives.EnumOfValue<byte, GfxImageDimension> root[6],
            MultiFaceControl = root[7],
            TextureControl1 = BinaryPrimitives.ReadUInt32BigEndian(root.Slice 8),
            Width = width, Height = height, Depth = depth,
            MemoryLocation = LanguagePrimitives.EnumOfValue<byte, GfxImageMemoryLocation> root[0x12],
            MinLodControl = root[0x13],
            RenderTargetPitch = BinaryPrimitives.ReadUInt32BigEndian(root.Slice 0x14),
            MapType = LanguagePrimitives.EnumOfValue<byte, MapType> (byte (BinaryPrimitives.ReadUInt32BigEndian root)),
            Category = LanguagePrimitives.EnumOfValue<byte, ImageCategory> root[0x2a],
            TextureSemantic = (if root[0x2a] = byte ImageCategory.Water then TextureSemantic.WaterMap
                               else TextureSemantic.TwoDimensional),
            UseSrgbReads = (if useSrgbReads then 1uy else 0uy),
            BaseWidth = width, BaseHeight = height, BaseDepth = depth,
            BaseLevelCount = root[5],
            CardMemory = uint32 payload.Length,
            Cached = GfxImageCached.No,
            PayloadByteCount = payload.Length,
            PayloadBytes = payload.ToArray())
