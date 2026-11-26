namespace MapConverter.IW3.PC.Readers

open System
open System.Collections.Generic
open System.IO
open System.IO.Compression

type internal Iw3PcIwdEntry =
    { Bytes: byte array
      Sources: IReadOnlyList<string> }

module internal Iw3PcIwdReader =
    let private entryComparer = StringComparer.OrdinalIgnoreCase

    let private compareNames (comparer: StringComparer) left right =
        let primary = comparer.Compare(left, right)
        if primary <> 0 then primary else StringComparer.Ordinal.Compare(left, right)

    let private validatePath (path: string) =
        if String.IsNullOrEmpty(path) || path[0] = '/' || path.Contains('\\') || path.Contains('\000') then
            raise (InvalidDataException($"IWD entry '{path}' has an unsafe path."))
        let name = if path.EndsWith('/') then path[..path.Length - 2] else path
        if name.Split('/') |> Array.exists (fun part -> part = "" || part = "." || part = ".." || part.Contains(':')) then
            raise (InvalidDataException($"IWD entry '{path}' has an unsafe path."))

    let ValidateImageName(name: string, source: string) =
        ArgumentException.ThrowIfNullOrWhiteSpace(name)
        if name = "." || name = ".." || name[0] = ',' || name.Contains('/') ||
           name.Contains('\\') || name.Contains('\000') || name <> name.Trim() then
            raise (InvalidDataException($"{source} has invalid image name '{name}'."))
        if name |> Seq.exists (fun character -> int character > 255) then
            raise (InvalidDataException($"{source} image name '{name}' is not representable as Latin-1."))

    let private archivePath (path: string) =
        let fullPath =
            try
                ArgumentException.ThrowIfNullOrWhiteSpace(path)
                Path.GetFullPath(path)
            with ex when (ex :? ArgumentException) || (ex :? NotSupportedException) || (ex :? PathTooLongException) ->
                raise (ArgumentException($"IWD path '{path}' is invalid: {ex.Message}", "iwdPaths", ex))
        if not (File.Exists(fullPath)) then
            raise (FileNotFoundException($"The IW3 IWD '{fullPath}' does not exist.", fullPath))
        fullPath

    let private files (archive: ZipArchive) = seq {
        for entry in archive.Entries do
            validatePath entry.FullName
            if entry.Name.Length <> 0 then yield entry.FullName, entry
    }

    let private images archive =
        let seen = Dictionary<string, string>(entryComparer)
        files archive
        |> Seq.choose (fun (path, entry) ->
            if path.StartsWith("images/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".iwi", StringComparison.OrdinalIgnoreCase) then
                let name = path.Substring(7, path.Length - 11)
                ValidateImageName(name, path)
                match seen.TryGetValue(name) with
                | true, previous ->
                    raise (InvalidDataException(
                        $"IWD image name '{name}' occurs more than once canonically in entries '{previous}' and '{path}'."))
                | _ ->
                    seen.Add(name, path)
                    Some(name, entry)
            else None)
        |> Seq.sortWith (fun (a, _) (b, _) -> compareNames entryComparer a b)

    let private readEntry (entry: ZipArchiveEntry) =
        try
            if entry.Length > int64 Int32.MaxValue then
                raise (InvalidDataException($"IWD entry '{entry.FullName}' is too large to materialize."))
            let data = Array.zeroCreate<byte> (int entry.Length)
            use input = entry.Open()
            input.ReadExactly(data, 0, data.Length)
            if input.ReadByte() <> -1 then
                raise (InvalidDataException($"IWD entry '{entry.FullName}' contains more bytes than its ZIP length."))
            data
        with ex when (ex :? IOException) || (ex :? InvalidDataException) || (ex :? NotSupportedException) ||
                     (ex :? ArgumentException) || (ex :? OverflowException) ->
            raise (InvalidDataException($"IWD entry '{entry.FullName}' could not be read: {ex.Message}", ex))

    let private resolve (iwdPaths: IReadOnlyList<string>) (requestedNames: seq<string>) inventory =
        ArgumentNullException.ThrowIfNull(iwdPaths)
        ArgumentNullException.ThrowIfNull(requestedNames)
        let requested = HashSet<string>(requestedNames, entryComparer)
        let pathComparer =
            if OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() then StringComparer.OrdinalIgnoreCase
            else StringComparer.Ordinal
        let seenPaths = HashSet<string>(pathComparer)
        let paths = iwdPaths |> Seq.map archivePath |> Seq.sortWith (compareNames pathComparer) |> Seq.filter seenPaths.Add
        let resolved = Dictionary<string, byte array * ResizeArray<string>>(entryComparer)
        for path in paths do
            try
                use stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                            1024 * 1024, FileOptions.SequentialScan)
                use archive = new ZipArchive(stream, ZipArchiveMode.Read)
                for name, (entry: ZipArchiveEntry) in inventory archive do
                    match requested.TryGetValue(name) with
                    | false, _ -> ()
                    | true, key ->
                        let source = $"{path}:{entry.FullName}"
                        let bytes = readEntry entry
                        match resolved.TryGetValue(key) with
                        | true, (previous, sources) ->
                            if not (previous.AsSpan().SequenceEqual(bytes.AsSpan())) then
                                let prior = String.Join("', '", sources)
                                raise (InvalidDataException($"IWD entry '{key}' has conflicting payloads in '{prior}' and '{source}'."))
                            if not (sources.Contains(source)) then sources.Add(source)
                        | _ -> resolved.Add(key, (bytes, ResizeArray([source])))
            with ex when (ex :? IOException) || (ex :? InvalidDataException) || (ex :? NotSupportedException) ||
                         (ex :? ArgumentException) || (ex :? OverflowException) || (ex :? UnauthorizedAccessException) ->
                raise (InvalidDataException($"IWD '{path}' could not be read: {ex.Message}", ex))
        let result = Dictionary<string, Iw3PcIwdEntry>(entryComparer)
        for KeyValue(key, (bytes, sources)) in resolved do
            result.Add(key, { Bytes = bytes; Sources = sources.AsReadOnly() })
        result :> IReadOnlyDictionary<_, _>

    let ResolveImages(iwdPaths, names) = resolve iwdPaths names images

    let ResolveFiles(iwdPaths, entryPaths: IReadOnlyList<string>) =
        ArgumentNullException.ThrowIfNull(entryPaths)
        for path in entryPaths do
            validatePath path
            if path.EndsWith('/') then invalidArg "entryPaths" $"IWD entry request '{path}' identifies a directory."
        resolve iwdPaths (entryPaths |> Seq.sortWith (compareNames entryComparer)) files

    /// Fill missing standalone image outputs from the selected archive and the game's loose/IWD search directories.
    let ExtractExternalImages(fastFile: string, selectedIwd: string, relativePaths: string array,
                              destination: string, token: Threading.CancellationToken) = task {
        let pending = Dictionary<string, string>(entryComparer)
        for relative in relativePaths do
            validatePath relative
            let target = Path.Combine(destination, relative)
            if not (File.Exists(target)) then pending.TryAdd(relative, target) |> ignore

        let copy key (input: Stream) = task {
            let target = pending[key]
            Directory.CreateDirectory(Path.GetDirectoryName(target)) |> ignore
            use output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)
            do! input.CopyToAsync(output, token)
            pending.Remove(key) |> ignore
        }
        let archives = HashSet<string>(StringComparer.Ordinal)
        let extractArchive path = task {
            if pending.Count > 0 && archives.Add(path) then
                use archive = ZipFile.OpenRead(path)
                for key, entry in files archive do
                    token.ThrowIfCancellationRequested()
                    if pending.ContainsKey(key) then
                        use input = entry.Open()
                        do! copy key input
        }
        if pending.Count > 0 then
            let source = FileInfo(fastFile).Directory
            let directories = ResizeArray<string>()
            if not (isNull selectedIwd) then
                let path = archivePath selectedIwd
                do! extractArchive path
                directories.Add(Path.GetDirectoryName(path))
            directories.Add(source.FullName)
            if not (isNull source.Parent) && entryComparer.Equals(source.Parent.Name, "zone") then
                directories.AddRange(Directory.EnumerateDirectories(source.Parent.FullName) |> Seq.sort)
                directories.Add(Path.Combine(source.Parent.FullName, "..", "main"))
            for directory in directories |> Seq.map Path.GetFullPath |> Seq.distinct do
                if pending.Count > 0 && Directory.Exists(directory) then
                    for key in pending.Keys |> Seq.toArray do
                        token.ThrowIfCancellationRequested()
                        let path = Path.Combine(directory, key)
                        if File.Exists(path) then
                            use input = File.OpenRead(path)
                            do! copy key input
                    if pending.Count > 0 then
                        for path in Directory.EnumerateFiles(directory) |> Seq.filter (fun path ->
                            path.EndsWith(".iwd", StringComparison.OrdinalIgnoreCase)) |> Seq.sort do
                            do! extractArchive path
    }
