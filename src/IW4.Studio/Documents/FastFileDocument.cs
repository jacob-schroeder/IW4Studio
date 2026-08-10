using IW4.FastFiles.Loaders.Database;
using IW4.Linker.Model;

namespace IW4.Studio.Documents;

/// <summary>One successfully loaded, immutable fastfile document.</summary>
public sealed class FastFileDocument
{
    internal FastFileDocument(FastFileDocumentOpenRequest request, LoadedXZone loadedZone)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(loadedZone);

        Request = request;
        SourcePath = Path.GetFullPath(request.Path);
        LoadedZone = loadedZone;
        ZoneObjectFile = loadedZone.ZoneObjectFile;
    }

    public FastFileDocumentOpenRequest Request { get; }
    public string SourcePath { get; }
    public LoadedXZone LoadedZone { get; }

    /// <summary>The loader-frozen symbolic input to the no-op linker.</summary>
    public ZoneObjectFile ZoneObjectFile { get; }
}
