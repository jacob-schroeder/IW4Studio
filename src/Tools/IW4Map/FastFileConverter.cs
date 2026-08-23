using IW4.Assets.Assets;
using IW4.Linker.Contracts;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;
using IW4.Studio.Documents;

namespace IW4Map;

internal static class FastFileConverter
{
    public static void ToD3dbsp(string input, string output)
    {
        string inputPath = Path.GetFullPath(input);
        string outputPath = Path.GetFullPath(output);
        RequireDifferentPath(inputPath, outputPath, "fastfile input");
        string outputDirectory = Path.GetDirectoryName(outputPath) ??
            throw new InvalidDataException("The output path has no containing directory.");
        if (!Directory.Exists(outputDirectory))
            throw new DirectoryNotFoundException($"Output directory '{outputDirectory}' does not exist.");
        if (File.Exists(outputPath))
            throw new IOException($"Output file '{outputPath}' already exists.");

        using FastFileWorkspace workspace = FastFileInspector.Open(inputPath);
        D3dbspFile file = FastFileD3dbspEncoder.Encode(workspace);
        file.Write(outputPath);
        Console.WriteLine($"wrote: {outputPath}");
        Console.WriteLine($"d3dbsp-chunks: {file.Lumps.Count}");
        Console.WriteLine($"d3dbsp-bytes: {new FileInfo(outputPath).Length}");
    }

    public static void FromD3dbsp(
        string input,
        string templateFastFile,
        string assetName,
        string output,
        bool forceFullbright,
        IReadOnlyList<string> dependencyFastFiles)
    {
        ArgumentNullException.ThrowIfNull(dependencyFastFiles);
        string inputPath = Path.GetFullPath(input);
        string templatePath = Path.GetFullPath(templateFastFile);
        string outputPath = Path.GetFullPath(output);
        RequireDifferentPath(inputPath, outputPath, "d3dbsp input");
        RequireDifferentPath(templatePath, outputPath, "template fastfile");
        string[] dependencyPaths = dependencyFastFiles
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (string dependencyPath in dependencyPaths)
        {
            RequireDifferentPath(dependencyPath, outputPath, "dependency fastfile");
            if (string.Equals(dependencyPath, templatePath, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The template fastfile '{templatePath}' cannot also be a dependency fastfile.");
            }
        }
        string outputDirectory = Path.GetDirectoryName(outputPath) ??
            throw new InvalidDataException("The output path has no containing directory.");
        if (!Directory.Exists(outputDirectory))
            throw new DirectoryNotFoundException($"Output directory '{outputDirectory}' does not exist.");
        if (File.Exists(outputPath))
            throw new IOException($"Output file '{outputPath}' already exists.");

        D3dbspAssetGraph graph = D3dbspAssetGraphBuilder.Build(
            inputPath,
            assetName,
            forceFullbright);
        using FastFileWorkspace template = FastFileInspector.Open(templatePath);

        LinkAssetPool baseAssets = template.InitialLinkRequest.Assets;
        var existingKeys = baseAssets.Providers
            .Select(provider => provider.Key)
            .ToHashSet();
        foreach (string dependencyPath in dependencyPaths)
        {
            using FastFileWorkspace dependency = FastFileInspector.Open(dependencyPath);
            LinkAssetPool missingAssets = dependency.InitialLinkRequest.Assets
                .WithoutProviders(existingKeys);
            baseAssets = baseAssets.WithHighestPrecedencePool(missingAssets);
            foreach (LinkAssetProvider provider in missingAssets.Providers)
                existingKeys.Add(provider.Key);
        }

        var newSources = new List<LinkAssetProviderSource>(graph.Roots.Count);
        var externalFallbackNames = new List<string>();
        foreach (BaseAsset root in graph.Roots)
            newSources.Add(new LinkAssetProviderSource(root).AsAuthoredDetached());

        foreach (BaseAsset dependency in graph.DependencyReferences)
        {
            AssetKey key = AssetKey.FromDefinition(dependency);
            if (existingKeys.Contains(key))
                continue;
            newSources.Add(new LinkAssetProviderSource(dependency).AsAuthoredDetached());
            externalFallbackNames.Add(
                dependency.SerializedAssetName ?? key.ToString());
            existingKeys.Add(key);
        }

        LinkAssetPool assets = baseAssets
            .WithHighestPrecedenceProviders(newSources);
        LinkRoot[] roots = graph.Roots
            .Select((asset, index) => CreateOwnedRoot(index, asset))
            .ToArray();
        var request = new ZoneLinkRequest(
            assets,
            roots,
            template.InitialLinkRequest.LanguageMask,
            template.InitialLinkRequest.SelectedLanguageMask,
            template.InitialLinkRequest.ScriptStrings);

        ZoneLinkResult link = new ZoneLinker().Link(request);
        if (!link.Succeeded || link.DecodedBytes is not { } decodedBytes)
        {
            throw new InvalidDataException(
                "Fastfile link failed: " + string.Join("; ", link.Errors));
        }

        FastFilePackagingResult package = new FastFilePackager().PackageGreenfield(
            decodedBytes,
            link.LanguageMask,
            link.SelectedLanguageMask,
            link.ImageStreamLanguageTables);
        if (!package.Succeeded || package.Bytes is not { } packageBytes)
        {
            throw new InvalidDataException(
                "Fastfile packaging failed: " +
                string.Join(
                    "; ",
                    package.Errors.Select(error => $"{error.Code}: {error.Message}")));
        }

        WriteNewFileAtomically(outputPath, packageBytes.Span);

        int referenceCount = newSources.Count - graph.Roots.Count;
        Console.WriteLine($"wrote: {outputPath}");
        Console.WriteLine($"map-asset: {assetName}");
        Console.WriteLine($"owned-map-roots: {roots.Length}");
        Console.WriteLine($"template-providers: {template.InitialLinkRequest.Assets.Providers.Count}");
        Console.WriteLine($"dependency-fastfiles: {dependencyPaths.Length}");
        Console.WriteLine(
            graph.DiscardedLightByteCount == 0
                ? "lighting-mode: source has no compiled lightmaps"
                : $"lighting-mode: forced fullbright; discarded {graph.DiscardedLightByteCount} compiled light bytes");
        Console.WriteLine($"available-providers: {baseAssets.Providers.Count}");
        Console.WriteLine($"external-reference-fallbacks: {referenceCount}");
        foreach (string referenceName in externalFallbackNames)
            Console.WriteLine($"external-reference: {referenceName}");
        Console.WriteLine($"decoded-zone-bytes: {decodedBytes.Length}");
        Console.WriteLine($"fastfile-bytes: {packageBytes.Length}");
    }

    private static LinkRoot CreateOwnedRoot(int index, BaseAsset asset)
    {
        string name = asset.SerializedAssetName ??
            throw new InvalidDataException($"{asset.SerializedAssetType} root has no serialized name.");
        return new LinkRoot(
            $"iw4map:{index}:{asset.SerializedAssetType}",
            asset.SerializedAssetType,
            LinkRootIntent.Owned,
            AssetKey.FromDefinition(asset),
            name,
            opaqueHeader: null);
    }

    private static void RequireDifferentPath(
        string source,
        string destination,
        string sourceDescription)
    {
        if (string.Equals(source, destination, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The output path must be different from the {sourceDescription} path.");
        }
    }

    private static void WriteNewFileAtomically(
        string outputPath,
        ReadOnlySpan<byte> bytes)
    {
        string directory = Path.GetDirectoryName(outputPath) ??
            throw new InvalidDataException("The fastfile output path has no containing directory.");
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                FileOptions.SequentialScan))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, outputPath);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
