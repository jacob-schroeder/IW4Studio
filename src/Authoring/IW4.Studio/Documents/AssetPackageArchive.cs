using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

/// <summary>One selected serialized row, including structural rows with no asset name.</summary>
public sealed record AssetPackageRoot(
    XAssetType Type,
    string? WireName,
    LinkRootIntent Intent,
    int? OpaqueHeader = null);

/// <summary>One logical provider identity required by the native subset.</summary>
public sealed record AssetPackageMember(XAssetType Type, string WireName, bool Included);

/// <summary>Native subset fastfile and its optional same-stem stream packages.</summary>
public sealed class AssetPackageContents
{
    public AssetPackageContents(
        byte[] fastFile,
        byte[]? imagePackage,
        byte[]? soundPackage,
        IReadOnlyList<AssetPackageRoot> selectedRoots,
        IReadOnlyList<AssetPackageMember> members)
    {
        FastFile = fastFile ?? throw new ArgumentNullException(nameof(fastFile));
        ImagePackage = imagePackage;
        SoundPackage = soundPackage;
        SelectedRoots = Array.AsReadOnly((selectedRoots ??
            throw new ArgumentNullException(nameof(selectedRoots))).ToArray());
        Members = Array.AsReadOnly((members ??
            throw new ArgumentNullException(nameof(members))).ToArray());
    }

    public byte[] FastFile { get; }
    public byte[]? ImagePackage { get; }
    public byte[]? SoundPackage { get; }
    public IReadOnlyList<AssetPackageRoot> SelectedRoots { get; }
    public IReadOnlyList<AssetPackageMember> Members { get; }
}

/// <summary>
/// Bounded ZIP container for native IW4 PS3 package files. Native stream ranges
/// are relative to the fixed zone stem, so consumers materialize these entries
/// beside one another before loading the subset.
/// </summary>
public static class AssetPackageArchive
{
    private const string FormatIdentity = "iw4-studio-package";
    private const string GameIdentity = "iw4";
    private const string PlatformIdentity = "ps3";
    public const string FastFileEntryName = "zone.ff";
    public const string ImagePackageEntryName = "zone.pak";
    public const string SoundPackageEntryName = "zone_snd.pak";
    private const string ManifestEntryName = "manifest.json";
    private const int MaxManifestBytes = 16 * 1024 * 1024;
    private const long MaxArchiveBytes = int.MaxValue - 1024L * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter<XAssetType>(allowIntegerValues: false),
            new JsonStringEnumConverter<LinkRootIntent>(allowIntegerValues: false)
        }
    };

    public static void Write(Stream destination, AssetPackageContents contents)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(contents);
        if (!destination.CanWrite)
            throw new ArgumentException("Destination must be writable.", nameof(destination));
        ValidateContents(contents);

        (string Name, byte[] Bytes)[] native = NativeEntries(contents);
        var manifest = new Manifest(
            FormatIdentity,
            GameIdentity,
            PlatformIdentity,
            contents.SelectedRoots.ToArray(),
            contents.Members.ToArray(),
            native.Select(entry => new ManifestFile(
                entry.Name,
                entry.Bytes.Length,
                Hash(entry.Bytes))).ToArray());
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        if (manifestBytes.Length > MaxManifestBytes)
            throw new InvalidDataException("Asset package manifest is too large.");

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        WriteEntry(archive, ManifestEntryName, manifestBytes, CompressionLevel.Optimal);
        foreach ((string name, byte[] bytes) in native)
            WriteEntry(archive, name, bytes, CompressionLevel.NoCompression);
    }

    public static AssetPackageContents Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("Source must be readable.", nameof(source));

        using Stream bounded = BufferIfNeeded(source);
        using var archive = new ZipArchive(bounded, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count is < 2 or > 4 ||
            archive.Entries.Select(entry => entry.FullName)
                .Distinct(StringComparer.Ordinal).Count() != archive.Entries.Count ||
            archive.Entries.Any(entry => entry.FullName is not
                (ManifestEntryName or FastFileEntryName or
                 ImagePackageEntryName or SoundPackageEntryName)))
        {
            throw new InvalidDataException("Asset package has unexpected or duplicate ZIP entries.");
        }

        ZipArchiveEntry manifestEntry = archive.GetEntry(ManifestEntryName) ??
            throw new InvalidDataException("Asset package has no manifest.");
        Manifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<Manifest>(
                    ReadEntry(manifestEntry, MaxManifestBytes), JsonOptions)
                ?? throw new InvalidDataException("Asset package manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Asset package manifest is malformed.", exception);
        }
        if (manifest.Format != FormatIdentity || manifest.Game != GameIdentity ||
            manifest.Platform != PlatformIdentity)
            throw new InvalidDataException("Asset package is not an IW4 PS3 package.");
        if (manifest.Files is null || manifest.SelectedRoots is null || manifest.Members is null)
            throw new InvalidDataException("Asset package manifest omits required fields.");
        if (manifest.Files.Any(file => file is null) ||
            manifest.Files.Length != archive.Entries.Count - 1 ||
            manifest.Files.Select(file => file.Name).Distinct(StringComparer.Ordinal).Count() !=
            manifest.Files.Length)
            throw new InvalidDataException("Asset package manifest file inventory is invalid.");

        var native = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (ManifestFile file in manifest.Files)
        {
            if (file.Name is not (FastFileEntryName or ImagePackageEntryName or SoundPackageEntryName) ||
                file.Length < 0 || file.Length > MaxArchiveBytes ||
                file.Sha256 is null || file.Sha256.Length != 64)
                throw new InvalidDataException("Asset package has an invalid native file entry.");
            ZipArchiveEntry entry = archive.GetEntry(file.Name) ??
                throw new InvalidDataException($"Asset package is missing '{file.Name}'.");
            byte[] bytes = ReadEntry(entry, MaxArchiveBytes - totalBytes);
            totalBytes = checked(totalBytes + bytes.Length);
            if (bytes.LongLength != file.Length ||
                !string.Equals(Hash(bytes), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Asset package file '{file.Name}' failed its SHA-256 check.");
            native.Add(file.Name, bytes);
        }
        if (!native.TryGetValue(FastFileEntryName, out byte[]? fastFile))
            throw new InvalidDataException("Asset package has no native fastfile.");

        var contents = new AssetPackageContents(
            fastFile,
            native.GetValueOrDefault(ImagePackageEntryName),
            native.GetValueOrDefault(SoundPackageEntryName),
            manifest.SelectedRoots,
            manifest.Members);
        try { ValidateContents(contents); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw new InvalidDataException("Asset package manifest contains an invalid asset identity.",
                exception);
        }
        return contents;
    }

    private static void ValidateContents(AssetPackageContents contents)
    {
        if (contents.FastFile.Length == 0 ||
            NativeEntries(contents).Sum(entry => (long)entry.Bytes.Length) > MaxArchiveBytes)
            throw new InvalidDataException("Asset package native payload is empty or too large.");
        if (contents.SelectedRoots.Count == 0)
            throw new InvalidDataException("Asset package has no selected rows.");
        foreach (AssetPackageRoot root in contents.SelectedRoots)
        {
            if (root is null || !Enum.IsDefined(root.Type) || !Enum.IsDefined(root.Intent))
                throw new InvalidDataException("Asset package has an invalid selected row.");
            if (root.Intent is LinkRootIntent.Owned or LinkRootIntent.External)
            {
                if (root.WireName is null || root.OpaqueHeader is not null)
                    throw new InvalidDataException("Asset package asset row lacks a valid wire name.");
                _ = new LinkRoot("package:validation", root.Type, root.Intent,
                    AssetKey.FromWireName(
                        CanonicalAssetFamily.FromSerializedType(root.Type), root.WireName),
                    root.WireName, null);
            }
            else if (root.WireName is not null ||
                     (root.Intent == LinkRootIntent.Null) != (root.OpaqueHeader is null))
                throw new InvalidDataException("Asset package structural row is malformed.");
        }
        var memberKeys = new HashSet<AssetKey>();
        foreach (AssetPackageMember member in contents.Members)
        {
            if (member is null || !Enum.IsDefined(member.Type) ||
                member.WireName is null ||
                member.Included == member.WireName.StartsWith(','))
                throw new InvalidDataException("Asset package has an invalid member.");
            AssetKey key = AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(member.Type), member.WireName);
            if (!memberKeys.Add(key))
                throw new InvalidDataException("Asset package repeats one logical member.");
        }
    }

    private static (string Name, byte[] Bytes)[] NativeEntries(AssetPackageContents contents) =>
        contents.ImagePackage is { } image && contents.SoundPackage is { } sound
            ? [(FastFileEntryName, contents.FastFile), (ImagePackageEntryName, image),
                (SoundPackageEntryName, sound)]
            : contents.ImagePackage is { } imageOnly
                ? [(FastFileEntryName, contents.FastFile), (ImagePackageEntryName, imageOnly)]
                : contents.SoundPackage is { } soundOnly
                    ? [(FastFileEntryName, contents.FastFile), (SoundPackageEntryName, soundOnly)]
                    : [(FastFileEntryName, contents.FastFile)];

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes,
        CompressionLevel compression)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, compression);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using Stream output = entry.Open();
        output.Write(bytes);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, long limit)
    {
        if (entry.Length < 0 || entry.Length > limit || entry.Length > int.MaxValue)
            throw new InvalidDataException($"Asset package entry '{entry.FullName}' is too large.");
        using Stream input = entry.Open();
        using var output = new MemoryStream(checked((int)entry.Length));
        byte[] buffer = new byte[64 * 1024];
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            if (output.Length + count > limit)
                throw new InvalidDataException($"Asset package entry '{entry.FullName}' is too large.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static Stream BufferIfNeeded(Stream source)
    {
        if (source.CanSeek)
        {
            if (source.Length - source.Position > MaxArchiveBytes)
                throw new InvalidDataException("Asset package ZIP is too large.");
            return new NonClosingStream(source);
        }
        var buffer = new MemoryStream();
        byte[] chunk = new byte[64 * 1024];
        int read;
        while ((read = source.Read(chunk)) != 0)
        {
            if (buffer.Length + read > MaxArchiveBytes)
            {
                buffer.Dispose();
                throw new InvalidDataException("Asset package ZIP is too large.");
            }
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        return buffer;
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record Manifest(
        string Format,
        string Game,
        string Platform,
        AssetPackageRoot[] SelectedRoots,
        AssetPackageMember[] Members,
        ManifestFile[] Files);

    private sealed record ManifestFile(string Name, long Length, string Sha256);

    private sealed class NonClosingStream(Stream source) : Stream
    {
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => source.CanSeek;
        public override bool CanWrite => false;
        public override long Length => source.Length;
        public override long Position { get => source.Position; set => source.Position = value; }
        public override void Flush() => source.Flush();
        public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
