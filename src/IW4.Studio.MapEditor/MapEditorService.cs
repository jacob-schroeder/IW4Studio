using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Compilation.Import;

namespace IW4.Studio.MapEditor;

public enum MapEditorOpenStatus
{
    Ready,
    NotAMap,
    Ambiguous,
    Invalid
}

public sealed class MapEditorOpenResult
{
    private readonly IReadOnlyList<string> _diagnostics;

    internal MapEditorOpenResult(
        MapEditorOpenStatus status,
        ExistingMapImportResult? session,
        IEnumerable<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if ((status == MapEditorOpenStatus.Ready) != (session is not null))
        {
            throw new ArgumentException(
                "Only a ready map-editor result may contain an imported session.",
                nameof(session));
        }

        Status = status;
        Session = session;
        _diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public MapEditorOpenStatus Status { get; }
    public ExistingMapImportResult? Session { get; }
    public IReadOnlyList<string> Diagnostics => _diagnostics;
    public bool Succeeded => Status == MapEditorOpenStatus.Ready;

    public static MapEditorOpenResult Failure(
        MapEditorOpenStatus status,
        params string[] diagnostics)
    {
        if (status == MapEditorOpenStatus.Ready)
            throw new ArgumentOutOfRangeException(nameof(status));

        return new MapEditorOpenResult(status, null, diagnostics);
    }
}

public sealed class MapEditorService
{
    private readonly ICompiledMapBundleResolver _resolver;
    private readonly IExistingMapImporter _importer;

    public MapEditorService(
        ICompiledMapBundleResolver? resolver = null,
        IExistingMapImporter? importer = null)
    {
        _resolver = resolver ?? new CompiledMapBundleResolver();
        _importer = importer ?? new ExistingMapImporter();
    }

    public MapEditorOpenResult Open(
        FastFileWorkspace workspace,
        CancellationToken cancellationToken = default) =>
        Open(
            workspace,
            sourceEditingSessionRevision: 0,
            cancellationToken);

    public MapEditorOpenResult Open(
        FastFileWorkspace workspace,
        long sourceEditingSessionRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (sourceEditingSessionRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceEditingSessionRevision));
        }
        cancellationToken.ThrowIfCancellationRequested();

        MapBundleResolutionResult resolution = _resolver.Resolve(
            workspace,
            sourceEditingSessionRevision,
            cancellationToken);
        if (!resolution.Succeeded)
        {
            return new MapEditorOpenResult(
                Convert(resolution.Status),
                null,
                resolution.Diagnostics);
        }

        try
        {
            ExistingMapImportResult imported = _importer.Import(
                resolution.Bundle!,
                cancellationToken);
            return new MapEditorOpenResult(
                MapEditorOpenStatus.Ready,
                imported,
                resolution.Diagnostics.Concat(imported.Audit.Diagnostics));
        }
        catch (Exception exception) when (
            exception is not (
                OutOfMemoryException or
                OperationCanceledException))
        {
            return new MapEditorOpenResult(
                MapEditorOpenStatus.Invalid,
                null,
                resolution.Diagnostics.Append(
                    $"Could not project the compiled map document: {exception.Message}"));
        }
    }

    private static MapEditorOpenStatus Convert(
        MapBundleResolutionStatus status) =>
        status switch
        {
            MapBundleResolutionStatus.Ready => MapEditorOpenStatus.Ready,
            MapBundleResolutionStatus.NotAMap => MapEditorOpenStatus.NotAMap,
            MapBundleResolutionStatus.Ambiguous => MapEditorOpenStatus.Ambiguous,
            MapBundleResolutionStatus.Invalid => MapEditorOpenStatus.Invalid,
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
}
