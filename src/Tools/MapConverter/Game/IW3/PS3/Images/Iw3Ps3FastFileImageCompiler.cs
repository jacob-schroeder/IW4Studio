using System.Buffers.Binary;
using System.Text;
using IW4.Assets.Assets.Image;
using MapConverter.Game.IW3.PC.Images;
using MapConverter.Game.IW3.PS3.FastFiles;

namespace MapConverter.Game.IW3.PS3.Images;

/// <summary>
/// Recovers streamed IW3 PS3 image payloads from unsigned console fastfiles.
/// The reader keeps only one packed frame, one decoded 0x10000-byte frame,
/// a small cross-frame scan window, and payloads explicitly requested by the
/// conversion.
/// </summary>
internal static class Iw3Ps3FastFileImageCompiler
{
    private const int XFileHeaderSize = 0x24;
    private const int SourceImageHeaderSize = 0x34;
    private const int MaximumInlineNameLength = 256;
    private const int ScanCarrySize = 512;
    private const int MaximumRetainedPayloadByteCount = 256 * 1024 * 1024;
    private const uint ExpectedTextureRemap = 0x0001aae4;

    private const byte Ps3Dxt1 = 0x86;
    private const byte Ps3Dxt3 = 0x87;
    private const byte Ps3Dxt5 = 0x88;

    private static readonly StringComparer ImageNameComparer =
        StringComparer.OrdinalIgnoreCase;

    internal static IReadOnlyList<(
        Iw3Iwi6StreamedImageCompilation Compilation,
        string SourceFastFilePath)> Compile(
        string sourceDirectory,
        IEnumerable<Iw3IwdImageRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        string fullSourceDirectory = ValidateSourceDirectory(sourceDirectory);
        Iw3IwdImageRequest[] orderedRequests = ValidateAndOrderRequests(requests);
        var unresolved = orderedRequests.ToDictionary(
            request => request.ImageName,
            ImageNameComparer);
        var compiledImages = new List<(
            Iw3Iwi6StreamedImageCompilation Compilation,
            string SourceFastFilePath)>();
        long retainedPayloadByteCount = 0;

        foreach (string fastFilePath in EnumerateFastFiles(fullSourceDirectory))
        {
            if (unresolved.Count == 0)
                break;

            retainedPayloadByteCount = checked(
                retainedPayloadByteCount + CompileFastFile(
                    fastFilePath,
                    unresolved,
                    compiledImages,
                    MaximumRetainedPayloadByteCount - retainedPayloadByteCount));
        }

        var orderedCompiledImages = compiledImages
            .OrderBy(result => result.Compilation.Image.Name, ImageNameComparer)
            .ThenBy(result => result.Compilation.Image.Name, StringComparer.Ordinal)
            .ToArray();
        return Array.AsReadOnly(orderedCompiledImages);
    }

    private static string ValidateSourceDirectory(string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        string fullPath = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"The IW3 PS3 fastfile directory does not exist: '{fullPath}'.");
        }
        return fullPath;
    }

    private static Iw3IwdImageRequest[] ValidateAndOrderRequests(
        IEnumerable<Iw3IwdImageRequest> requests)
    {
        var canonicalNames = new HashSet<string>(ImageNameComparer);
        var materialized = new List<Iw3IwdImageRequest>();
        foreach (Iw3IwdImageRequest request in requests)
        {
            if (request is null)
            {
                throw new ArgumentException(
                    "An IW3 PS3 image request cannot be null.",
                    nameof(requests));
            }

            ValidateImageName(request.ImageName);
            if (!Enum.IsDefined(request.Semantic) ||
                request.Semantic == TextureSemantic.WaterMap)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requests),
                    request.Semantic,
                    "A streamed two-dimensional material-image semantic is required.");
            }
            if (!canonicalNames.Add(request.ImageName))
            {
                throw new ArgumentException(
                    $"IW3 PS3 image request '{request.ImageName}' occurs more than once canonically.",
                    nameof(requests));
            }
            materialized.Add(request);
        }

        return materialized
            .OrderBy(request => request.ImageName, ImageNameComparer)
            .ThenBy(request => request.ImageName, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateImageName(string imageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);
        if (imageName is "." or ".." ||
            imageName[0] == ',' ||
            imageName.Contains('/') ||
            imageName.Contains('\\') ||
            imageName.Contains('\0') ||
            !string.Equals(imageName, imageName.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"IW3 PS3 image request has invalid image name '{imageName}'.");
        }
        if (imageName.Any(character => character > byte.MaxValue))
        {
            throw new InvalidDataException(
                $"IW3 PS3 image name '{imageName}' is not representable as Latin-1.");
        }
    }

    private static IEnumerable<string> EnumerateFastFiles(string sourceDirectory) =>
        Directory.EnumerateFiles(
                sourceDirectory,
                "*",
                SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(
                Path.GetExtension(path),
                ".ff",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(Path.GetFileName, StringComparer.Ordinal)
            .ThenBy(path => path, StringComparer.Ordinal);

    private static long CompileFastFile(
        string fastFilePath,
        IDictionary<string, Iw3IwdImageRequest> unresolved,
        ICollection<(
            Iw3Iwi6StreamedImageCompilation Compilation,
            string SourceFastFilePath)> compiledImages,
        long retainedPayloadBudget)
    {
        using (var stream = new FileStream(
            fastFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 8,
            FileOptions.SequentialScan))
        {
            Span<byte> magic = stackalloc byte[8];
            int magicLength = stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false);
            if (magicLength != magic.Length || !magic.SequenceEqual("IWffu100"u8))
                return 0;
        }

        try
        {
            var scanner = new FastFileScanner(
                unresolved,
                retainedPayloadBudget);
            Iw3Ps3FastFileFrames.Read(
                fastFilePath,
                frame => scanner.ProcessDecodedFrame(frame.Span));

            IReadOnlyList<PayloadCapture> captures = scanner.Complete();
            foreach (PayloadCapture capture in captures)
            {
                try
                {
                    compiledImages.Add((
                        Iw3Iwi6StreamedImageCompiler.CompileTopLevelFirstPs3Payload(
                            capture.Request.ImageName,
                            capture.Header.Format,
                            capture.Header.MipCount,
                            capture.Header.Width,
                            capture.Header.Height,
                            capture.Payload,
                            capture.Request.Semantic,
                            capture.Request.UseSrgbReads),
                        fastFilePath));
                }
                catch (Exception exception) when (exception is
                    ArgumentException or
                    InvalidDataException or
                    NotSupportedException or
                    OverflowException)
                {
                    throw new InvalidDataException(
                        $"streamed image '{capture.Request.ImageName}' could not be compiled: " +
                        exception.Message,
                        exception);
                }

                unresolved.Remove(capture.Request.ImageName);
            }
            return captures.Sum(capture => (long)capture.Payload.Length);
        }
        catch (Exception exception) when (exception is
            EndOfStreamException or
            InvalidDataException or
            OverflowException)
        {
            throw new InvalidDataException(
                $"IW3 PS3 fastfile '{fastFilePath}' is invalid: {exception.Message}",
                exception);
        }
    }

    private sealed class FastFileScanner
    {
        private readonly IDictionary<string, Iw3IwdImageRequest> _requests;
        private readonly byte[] _xfileHeader = new byte[XFileHeaderSize];
        private readonly byte[] _carry = new byte[ScanCarrySize];
        private readonly byte[] _window = new byte[Iw3Ps3FastFileFrames.DecodedFrameSize + ScanCarrySize];
        private readonly List<PayloadCapture> _captures = [];
        private readonly HashSet<string> _matchedNames = new(ImageNameComparer);
        private readonly long _retainedPayloadBudget;

        private int _xfileHeaderLength;
        private int _carryLength;
        private long _decodedOffset;
        private long _logicalEnd = -1;
        private long _tailStart = -1;
        private long _delayedByteCount = -1;
        private long _queuedImageByteCount;
        private long _capturedPayloadByteCount;
        private long _nextHeaderOffset = XFileHeaderSize;
        private bool _queueComplete;

        internal FastFileScanner(
            IDictionary<string, Iw3IwdImageRequest> requests,
            long retainedPayloadBudget)
        {
            _requests = requests;
            _retainedPayloadBudget = retainedPayloadBudget;
        }

        internal void ProcessDecodedFrame(ReadOnlySpan<byte> frame)
        {
            if (frame.IsEmpty || frame.Length > Iw3Ps3FastFileFrames.DecodedFrameSize)
                throw new InvalidDataException("a decoded packed frame has an invalid byte count.");

            long frameStart = _decodedOffset;
            long frameEnd = checked(frameStart + frame.Length);
            if (_xfileHeaderLength < _xfileHeader.Length)
            {
                int copied = Math.Min(
                    frame.Length,
                    _xfileHeader.Length - _xfileHeaderLength);
                frame[..copied].CopyTo(
                    _xfileHeader.AsSpan(_xfileHeaderLength));
                _xfileHeaderLength += copied;
                if (_xfileHeaderLength == _xfileHeader.Length)
                    ReadXFileLayout();
            }

            if (_logicalEnd >= 0 && frameEnd > _logicalEnd)
            {
                int paddingOffset = checked((int)Math.Max(
                    0,
                    _logicalEnd - frameStart));
                if (frame[paddingOffset..].IndexOfAnyExcept((byte)0) >= 0)
                {
                    throw new InvalidDataException(
                        $"decoded data contains non-zero padding after XFile logical end " +
                        $"0x{_logicalEnd:X}.");
                }
            }

            _carry.AsSpan(0, _carryLength).CopyTo(_window);
            frame.CopyTo(_window.AsSpan(_carryLength));
            int windowLength = checked(_carryLength + frame.Length);
            long windowStart = frameStart - _carryLength;
            if (_logicalEnd >= 0 && !_queueComplete)
            {
                ScanHeaders(
                    _window.AsSpan(0, windowLength),
                    windowStart,
                    Math.Min(frameEnd, _logicalEnd));
            }

            foreach (PayloadCapture capture in _captures)
                capture.CopyFrom(frame, frameStart);

            _carryLength = Math.Min(ScanCarrySize, windowLength);
            _window.AsSpan(windowLength - _carryLength, _carryLength)
                .CopyTo(_carry);
            _decodedOffset = frameEnd;
        }

        internal IReadOnlyList<PayloadCapture> Complete()
        {
            if (_xfileHeaderLength != _xfileHeader.Length)
                throw new InvalidDataException("decoded data is shorter than the XFile header.");
            if (_decodedOffset < _logicalEnd)
            {
                throw new InvalidDataException(
                    $"decoded data ends at 0x{_decodedOffset:X}; " +
                    $"XFile logical end is 0x{_logicalEnd:X}.");
            }
            if (!_queueComplete || _queuedImageByteCount != _delayedByteCount)
            {
                throw new InvalidDataException(
                    $"streamed GfxImage headers account for 0x{_queuedImageByteCount:X} " +
                    $"of the 0x{_delayedByteCount:X} delayed payload bytes.");
            }
            PayloadCapture? incomplete = _captures.FirstOrDefault(
                capture => !capture.IsComplete);
            if (incomplete is not null)
            {
                throw new InvalidDataException(
                    $"streamed image '{incomplete.Request.ImageName}' payload is truncated.");
            }
            return _captures.AsReadOnly();
        }

        private void ReadXFileLayout()
        {
            uint declaredSize = BinaryPrimitives.ReadUInt32BigEndian(
                _xfileHeader.AsSpan(0, sizeof(uint)));
            // IW3 PS3 declares the content size after its seven-block 0x24-byte
            // XFile header. The PC nine-block header is eight bytes larger.
            _logicalEnd = checked((long)declaredSize + XFileHeaderSize);
            _delayedByteCount = checked(
                (long)BinaryPrimitives.ReadUInt32BigEndian(
                    _xfileHeader.AsSpan(0x10, sizeof(uint))) +
                BinaryPrimitives.ReadUInt32BigEndian(
                    _xfileHeader.AsSpan(0x14, sizeof(uint))));
            _tailStart = checked(_logicalEnd - _delayedByteCount);
            if (_logicalEnd < XFileHeaderSize || _tailStart < XFileHeaderSize)
            {
                throw new InvalidDataException(
                    "XFile sizes place the delayed payload tail before its header.");
            }
            _queueComplete = _delayedByteCount == 0;
        }

        private void ScanHeaders(
            ReadOnlySpan<byte> window,
            long windowStart,
            long decodedEnd)
        {
            const int maximumRecordSpan =
                SourceImageHeaderSize + MaximumInlineNameLength + 1;
            long maximumHeaderOffsetExclusive = decodedEnd >= _tailStart
                ? checked(_tailStart - SourceImageHeaderSize + 1)
                : checked(decodedEnd - maximumRecordSpan + 1);
            maximumHeaderOffsetExclusive = Math.Min(
                maximumHeaderOffsetExclusive,
                checked(_tailStart - SourceImageHeaderSize + 1));
            if (maximumHeaderOffsetExclusive <= _nextHeaderOffset)
                return;
            if (_nextHeaderOffset < windowStart)
            {
                throw new InvalidDataException(
                    "the rolling GfxImage scan window lost undecoded header bytes.");
            }

            long firstHeaderOffset = Math.Max(_nextHeaderOffset, windowStart);
            long windowEnd = checked(windowStart + window.Length);
            long scanEnd = Math.Min(maximumHeaderOffsetExclusive, windowEnd);
            int availableStructuralEnd = checked((int)(
                Math.Min(decodedEnd, _tailStart) - windowStart));
            for (long headerOffset = firstHeaderOffset;
                 headerOffset < scanEnd && !_queueComplete;
                 headerOffset++)
            {
                int localOffset = checked((int)(headerOffset - windowStart));
                uint dataPointer = BinaryPrimitives.ReadUInt32BigEndian(
                    window.Slice(localOffset + 0x2c, sizeof(uint)));
                if (dataPointer is not (0xfffffffe or 0xffffffff))
                    continue;

                if (!TryReadImageHeader(
                    window,
                    localOffset,
                    availableStructuralEnd,
                    out SourceImageHeader header))
                {
                    continue;
                }
                RegisterStreamedImage(header);
            }
            _nextHeaderOffset = maximumHeaderOffsetExclusive;
        }

        private void RegisterStreamedImage(SourceImageHeader header)
        {
            long payloadOffset = checked(_tailStart + _queuedImageByteCount);
            _queuedImageByteCount = checked(
                _queuedImageByteCount + header.PayloadByteCount);
            if (_queuedImageByteCount > _delayedByteCount)
            {
                throw new InvalidDataException(
                    $"streamed GfxImage payloads exceed the delayed tail by " +
                    $"0x{_queuedImageByteCount - _delayedByteCount:X} byte(s).");
            }

            if (header.Name is not null &&
                _requests.TryGetValue(
                    header.Name,
                    out Iw3IwdImageRequest? request) &&
                request is not null)
            {
                if (!_matchedNames.Add(request.ImageName))
                {
                    throw new InvalidDataException(
                        $"streamed image '{request.ImageName}' occurs more than once.");
                }
                ValidateRequestedImageProfile(header, request.ImageName);
                _capturedPayloadByteCount = checked(
                    _capturedPayloadByteCount + header.PayloadByteCount);
                if (_capturedPayloadByteCount > _retainedPayloadBudget)
                {
                    throw new InvalidDataException(
                        "requested IW3 PS3 image payloads exceed the " +
                        $"{MaximumRetainedPayloadByteCount / (1024 * 1024)} MiB " +
                        "retained-memory limit.");
                }
                _captures.Add(new PayloadCapture(
                    request,
                    header,
                    payloadOffset));
            }

            _queueComplete = _queuedImageByteCount == _delayedByteCount;
        }
    }

    private static bool TryReadImageHeader(
        ReadOnlySpan<byte> window,
        int offset,
        int availableStructuralEnd,
        out SourceImageHeader header)
    {
        header = default;
        if (offset < 0 ||
            offset > availableStructuralEnd - SourceImageHeaderSize)
        {
            return false;
        }

        ReadOnlySpan<byte> source = window.Slice(offset, SourceImageHeaderSize);
        uint mapType = BinaryPrimitives.ReadUInt32BigEndian(source);
        byte format = source[0x04];
        byte mipCount = source[0x05];
        byte dimension = source[0x06];
        byte isCubemap = source[0x07];
        uint textureRemap = BinaryPrimitives.ReadUInt32BigEndian(source[0x08..]);
        ushort textureWidth = BinaryPrimitives.ReadUInt16BigEndian(source[0x0c..]);
        ushort textureHeight = BinaryPrimitives.ReadUInt16BigEndian(source[0x0e..]);
        ushort textureDepth = BinaryPrimitives.ReadUInt16BigEndian(source[0x10..]);
        byte memoryLocation = source[0x12];
        byte minimumLod = source[0x13];
        uint pitch = BinaryPrimitives.ReadUInt32BigEndian(source[0x14..]);
        uint textureOffset = BinaryPrimitives.ReadUInt32BigEndian(source[0x18..]);
        uint payloadByteCount = BinaryPrimitives.ReadUInt32BigEndian(source[0x20..]);
        ushort width = BinaryPrimitives.ReadUInt16BigEndian(source[0x24..]);
        ushort height = BinaryPrimitives.ReadUInt16BigEndian(source[0x26..]);
        ushort depth = BinaryPrimitives.ReadUInt16BigEndian(source[0x28..]);
        byte category = source[0x2a];
        byte streaming = source[0x2b];
        uint dataPointer = BinaryPrimitives.ReadUInt32BigEndian(source[0x2c..]);
        uint namePointer = BinaryPrimitives.ReadUInt32BigEndian(source[0x30..]);

        if (dataPointer is not (0xfffffffe or 0xffffffff) ||
            mapType is < 3 or > 5 ||
            !IsKnownPs3Format(format) ||
            mipCount is < 1 or > 16 ||
            dimension is < 1 or > 3 ||
            isCubemap > 1 ||
            (textureWidth, textureHeight, textureDepth) != (width, height, depth) ||
            width is 0 or > 8192 ||
            height is 0 or > 8192 ||
            depth is 0 or > 512 ||
            memoryLocation > 1 ||
            textureOffset != 0 ||
            (format == 0xa1 ? pitch != width : pitch != 0) ||
            payloadByteCount is 0 or > 0x10000000 ||
            payloadByteCount % 0x80 != 0 ||
            category > 7 ||
            streaming != 1)
        {
            return false;
        }

        string? name;
        if (namePointer == uint.MaxValue)
        {
            int nameOffset = checked(offset + SourceImageHeaderSize);
            int maximumNameEnd = Math.Min(
                availableStructuralEnd,
                checked(nameOffset + MaximumInlineNameLength + 1));
            if (maximumNameEnd <= nameOffset)
                return false;
            int terminator = window[nameOffset..maximumNameEnd].IndexOf((byte)0);
            if (terminator is <= 0 or > MaximumInlineNameLength)
                return false;

            ReadOnlySpan<byte> nameBytes = window.Slice(nameOffset, terminator);
            for (int index = 0; index < nameBytes.Length; index++)
            {
                if (nameBytes[index] is < 0x20 or > 0x7e)
                    return false;
            }
            name = Encoding.ASCII.GetString(nameBytes);
        }
        else if (namePointer is >= 0x80000001 and <= 0x9fffffff)
        {
            name = null;
        }
        else
        {
            return false;
        }

        header = new SourceImageHeader(
            format,
            mipCount,
            dimension,
            isCubemap != 0,
            textureRemap,
            memoryLocation,
            minimumLod,
            pitch,
            textureOffset,
            payloadByteCount,
            width,
            height,
            depth,
            category,
            name,
            mapType);
        return true;
    }

    private static bool IsKnownPs3Format(byte format) => format is
        0x81 or 0x82 or 0x85 or 0x86 or 0x87 or 0x88 or
        0x8b or 0x8c or 0x8d or 0x8e or 0x9e or 0xa1;

    private static void ValidateRequestedImageProfile(
        SourceImageHeader header,
        string imageName)
    {
        if (header.Format is not (Ps3Dxt1 or Ps3Dxt3 or Ps3Dxt5) ||
            header.MapType != 3 ||
            header.Dimension != 2 ||
            header.IsCubemap ||
            header.TextureRemap != ExpectedTextureRemap ||
            header.MemoryLocation != 0 ||
            header.MinimumLod != 0 ||
            header.Pitch != 0 ||
            header.TextureOffset != 0 ||
            header.Depth != 1 ||
            header.Category != (byte)ImageCategory.LoadFromFile)
        {
            throw new InvalidDataException(
                $"streamed image '{imageName}' does not use the proven two-dimensional " +
                "PS3 DXT1/DXT3/DXT5 payload profile.");
        }

        int maximumMipCount = 1;
        int width = header.Width;
        int height = header.Height;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            maximumMipCount++;
        }
        if (header.MipCount > maximumMipCount)
        {
            throw new InvalidDataException(
                $"streamed image '{imageName}' has invalid mip count " +
                $"{header.MipCount} for {header.Width}x{header.Height}.");
        }

        int expectedPayloadByteCount = GfxImagePixelLayout.ComputePayloadByteCount(
            new GfxImageFormat(header.Format),
            header.MipCount,
            isCubemap: false,
            new GfxImageTextureRemap(header.TextureRemap),
            header.Width,
            header.Height,
            depth: 1);
        if (header.PayloadByteCount != expectedPayloadByteCount)
        {
            throw new InvalidDataException(
                $"streamed image '{imageName}' declares 0x{header.PayloadByteCount:X} " +
                $"payload bytes; its proven PS3 mip profile requires " +
                $"0x{expectedPayloadByteCount:X}.");
        }
        if (expectedPayloadByteCount > 0x03ffffff)
        {
            throw new InvalidDataException(
                $"streamed image '{imageName}' exceeds the PS3 26-bit " +
                "stream-length field.");
        }
    }

    private sealed class PayloadCapture
    {
        private readonly long _payloadOffset;
        private int _writtenByteCount;

        internal PayloadCapture(
            Iw3IwdImageRequest request,
            SourceImageHeader header,
            long payloadOffset)
        {
            Request = request;
            Header = header;
            _payloadOffset = payloadOffset;
            Payload = new byte[checked((int)header.PayloadByteCount)];
        }

        internal Iw3IwdImageRequest Request { get; }
        internal SourceImageHeader Header { get; }
        internal byte[] Payload { get; }
        internal bool IsComplete => _writtenByteCount == Payload.Length;

        internal void CopyFrom(ReadOnlySpan<byte> frame, long frameOffset)
        {
            long payloadEnd = checked(_payloadOffset + Payload.Length);
            long frameEnd = checked(frameOffset + frame.Length);
            long copyStart = Math.Max(_payloadOffset, frameOffset);
            long copyEnd = Math.Min(payloadEnd, frameEnd);
            if (copyEnd <= copyStart)
                return;

            int sourceOffset = checked((int)(copyStart - frameOffset));
            int destinationOffset = checked((int)(copyStart - _payloadOffset));
            int byteCount = checked((int)(copyEnd - copyStart));
            if (destinationOffset != _writtenByteCount)
            {
                throw new InvalidDataException(
                    $"streamed image '{Request.ImageName}' payload was encountered out of order.");
            }
            frame.Slice(sourceOffset, byteCount).CopyTo(
                Payload.AsSpan(destinationOffset, byteCount));
            _writtenByteCount = checked(_writtenByteCount + byteCount);
        }
    }

    private readonly record struct SourceImageHeader(
        byte Format,
        byte MipCount,
        byte Dimension,
        bool IsCubemap,
        uint TextureRemap,
        byte MemoryLocation,
        byte MinimumLod,
        uint Pitch,
        uint TextureOffset,
        uint PayloadByteCount,
        ushort Width,
        ushort Height,
        ushort Depth,
        byte Category,
        string? Name,
        uint MapType);
}
