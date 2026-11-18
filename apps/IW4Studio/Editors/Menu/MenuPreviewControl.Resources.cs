using Avalonia.Media.Imaging;
using Avalonia.Threading;
using IW4.Render.UI.Text;
using IW4.Studio.Desktop.Documents.MenuEditing.Preview;
using IW4.Studio.Desktop.Rendering;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuPreviewControl
{
    private readonly Dictionary<string, MenuPreviewMaterialSnapshot>
        _materialSnapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReadOnlyMemory<byte>> _materialPixels =
        new(StringComparer.Ordinal);
    private readonly Dictionary<MaterialBitmapKey, Bitmap> _materialBitmaps = [];
    private readonly Dictionary<string, string> _materialFailures =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, MenuPreviewMaterialStatus>
        _materialStatuses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource>
        _pendingMaterialLoads = new(StringComparer.Ordinal);
    private readonly HashSet<string> _materialNames =
        new(StringComparer.Ordinal);
    private Dictionary<MenuPreviewText, MenuPreviewTextLayout> _textLayouts = [];
    private IMenuPreviewMaterialResolver? _activeMaterialResolver;
    private long _activeMaterialRevision = -1;
    private IMenuTextResourceResolver? _activeTextResourceResolver;
    private MenuPreviewScene? _activeTextScene;
    private MenuTextResourceRevision? _activeTextRevision;

    private bool RefreshTextLayouts(bool reportStatuses = false)
    {
        if (!_isAttached)
            return false;

        MenuPreviewScene? scene = Scene;
        IMenuTextResourceResolver? resolver = TextResourceResolver;
        MenuTextResourceRevision? revision = resolver?.Revision;
        bool resourcesChanged = !ReferenceEquals(_activeTextResourceResolver, resolver) ||
            _activeTextRevision != revision || _activeTextScene?.Settings != scene?.Settings;
        if (!resourcesChanged && ReferenceEquals(_activeTextScene, scene))
            return false;

        _activeTextScene = scene;
        _activeTextResourceResolver = resolver;
        _activeTextRevision = revision;
        if (resourcesChanged)
            _textLayouts.Clear();

        // A clock tick can move the fog while every label keeps its placement
        // and text. Keep glyph plans for the exact primitives still on screen.
        MenuPreviewText[] texts = scene?.Primitives.OfType<MenuPreviewText>().ToArray() ?? [];
        if (!resourcesChanged && _textLayouts.Count == texts.Length && texts.All(_textLayouts.ContainsKey))
            return false;

        var next = new Dictionary<MenuPreviewText, MenuPreviewTextLayout>(texts.Length);
        if (scene is not null && resolver is not null)
        {
            var context = MenuPreviewTextLayoutContext.FromScreenPlacement(scene.Settings.ScreenPlacement);
            foreach (MenuPreviewText text in texts)
            {
                next[text] = _textLayouts.TryGetValue(text, out var retained)
                    ? retained
                    : MenuPreviewTextLayoutPlanner.Plan(text, resolver, context);
            }
        }
        _textLayouts = next;

        if (reportStatuses)
            ReportTextStatuses();
        InvalidateVisual();
        return true;
    }

    private void ReportTextStatuses()
    {
        if (TextResolutionCompleted is null)
            return;

        foreach (MenuPreviewTextLayout layout in _textLayouts.Values)
        {
            TextResolutionCompleted?.Invoke(
                this,
                new MenuPreviewTextResolutionCompletedEventArgs(
                    new MenuPreviewTextStatus(
                        layout.Source.NodeId,
                        layout.Source.Text,
                        layout.UsesGameGlyphs,
                        layout.Diagnostics)
                    {
                        LayoutDescription = layout.GlyphRun is { } run
                            ? $"“{layout.DisplayText}” · advance {run.MaxLineAdvance:0.#} / rect {layout.Source.Placement.VirtualRectangle.Width:0.#} virtual units · {run.FontName}"
                            : $"“{layout.DisplayText}” · game font metrics unavailable"
                    }));
        }
    }

    private void RefreshMaterials(bool reportRetainedStatuses = false)
    {
        if (!_isAttached)
            return;

        if (Scene is not { } scene || MaterialResolver is not { } resolver)
        {
            ResetMaterialState();
            InvalidateVisual();
            return;
        }

        long revision = resolver.Revision;
        bool contextChanged =
            !ReferenceEquals(_activeMaterialResolver, resolver) ||
            _activeMaterialRevision != revision;
        if (contextChanged)
        {
            ResetMaterialState();
            _activeMaterialResolver = resolver;
            _activeMaterialRevision = revision;
        }

        string[] materialNames = scene.Primitives
            .OfType<MenuPreviewMaterial>()
            .Select(value => value.MaterialName)
            .Concat(_textLayouts.Values
                .Select(value => value.GlyphRun?.MaterialName))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var requestedNames = materialNames.ToHashSet(StringComparer.Ordinal);
        foreach (string removedName in _materialNames
                     .Where(name => !requestedNames.Contains(name))
                     .ToArray())
        {
            RemoveMaterial(removedName);
        }

        foreach (string materialName in materialNames)
        {
            _materialNames.Add(materialName);
            if (!_materialStatuses.ContainsKey(materialName) &&
                !_pendingMaterialLoads.ContainsKey(materialName))
            {
                StartMaterialLoad(materialName, resolver, revision);
            }
        }

        if (reportRetainedStatuses && !contextChanged)
        {
            foreach (string materialName in materialNames)
            {
                if (_materialStatuses.TryGetValue(
                        materialName,
                        out MenuPreviewMaterialStatus? status))
                {
                    ReportMaterialStatus(status);
                }
            }
        }

        RefreshMaterialBitmaps();
        InvalidateVisual();
    }

    private void StartMaterialLoad(
        string materialName,
        IMenuPreviewMaterialResolver resolver,
        long revision)
    {
        var cancellation = new CancellationTokenSource();
        _pendingMaterialLoads.Add(materialName, cancellation);
        _ = ResolveMaterialAsync(
            materialName,
            resolver,
            revision,
            cancellation);
    }

    private async Task ResolveMaterialAsync(
        string materialName,
        IMenuPreviewMaterialResolver resolver,
        long revision,
        CancellationTokenSource cancellation)
    {
        CancellationToken cancellationToken = cancellation.Token;
        MenuPreviewMaterialResolution resolution;
        try
        {
            resolution = await resolver.ResolveAsync(
                materialName,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            resolution = MenuPreviewMaterialResolution.Failed(
                exception.Message);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!IsCurrentMaterialLoad(
                    materialName,
                    resolver,
                    revision,
                    cancellation))
            {
                return;
            }

            if (resolver.Revision != revision)
            {
                RefreshMaterials();
                return;
            }

            MenuPreviewMaterialResolution presentedResolution = resolution;
            MenuPreviewMaterialSnapshot? materialSnapshot = null;
            ReadOnlyMemory<byte> materialPixels = default;
            string? failure = null;
            if (resolution.Snapshot is { } snapshot)
            {
                try
                {
                    materialSnapshot = snapshot;
                    materialPixels = ValidateMaterialPayload(snapshot);
                }
                catch (Exception exception) when (
                    exception is not OutOfMemoryException)
                {
                    materialSnapshot = null;
                    failure =
                        $"Decoded image '{snapshot.ImageName}' for material " +
                        $"'{materialName}' could not be opened by the " +
                        $"preview surface: {exception.Message}";
                    presentedResolution = MenuPreviewMaterialResolution.Failed(
                        failure,
                        resolution.PoolRevision,
                        resolution.Diagnostics);
                }
            }
            else
            {
                failure = resolution.Failure ??
                    "Material preview is unavailable.";
            }

            if (!IsCurrentMaterialLoad(
                    materialName,
                    resolver,
                    revision,
                    cancellation))
            {
                return;
            }

            if (resolver.Revision != revision)
            {
                RefreshMaterials();
                return;
            }

            _pendingMaterialLoads.Remove(materialName);
            cancellation.Dispose();
            if (materialSnapshot is not null)
            {
                _materialSnapshots.Add(materialName, materialSnapshot);
                _materialPixels.Add(materialName, materialPixels);
                RefreshMaterialBitmaps();
            }
            else
                _materialFailures.Add(materialName, failure!);

            MenuPreviewMaterialStatus status =
                presentedResolution.CreateStatus(materialName);
            _materialStatuses.Add(materialName, status);
            InvalidateVisual();
            ReportMaterialStatus(status);
        });
    }

    private bool IsCurrentMaterialLoad(
        string materialName,
        IMenuPreviewMaterialResolver resolver,
        long revision,
        CancellationTokenSource cancellation) =>
        _isAttached &&
        !cancellation.IsCancellationRequested &&
        ReferenceEquals(MaterialResolver, resolver) &&
        ReferenceEquals(_activeMaterialResolver, resolver) &&
        _activeMaterialRevision == revision &&
        _materialNames.Contains(materialName) &&
        _pendingMaterialLoads.TryGetValue(
            materialName,
            out CancellationTokenSource? currentCancellation) &&
        ReferenceEquals(currentCancellation, cancellation);

    private void ReportMaterialStatus(MenuPreviewMaterialStatus status) =>
        MaterialResolutionCompleted?.Invoke(
            this,
            new MenuPreviewMaterialResolutionCompletedEventArgs(status));

    private void RemoveMaterial(string materialName)
    {
        _materialNames.Remove(materialName);
        if (_pendingMaterialLoads.Remove(
                materialName,
                out CancellationTokenSource? cancellation))
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        _materialSnapshots.Remove(materialName);
        _materialPixels.Remove(materialName);
        foreach (MaterialBitmapKey key in _materialBitmaps.Keys
                     .Where(key => string.Equals(
                         key.MaterialName,
                         materialName,
                         StringComparison.Ordinal))
                     .ToArray())
        {
            _materialBitmaps.Remove(key, out Bitmap? bitmap);
            bitmap?.Dispose();
        }
        _materialFailures.Remove(materialName);
        _materialStatuses.Remove(materialName);
    }

    private void ResetMaterialState()
    {
        foreach (CancellationTokenSource cancellation in
                 _pendingMaterialLoads.Values)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
        _pendingMaterialLoads.Clear();

        foreach (Bitmap bitmap in _materialBitmaps.Values)
            bitmap.Dispose();
        _materialBitmaps.Clear();
        ReleaseCpuCompositeSurface();
        _materialSnapshots.Clear();
        _materialPixels.Clear();
        _materialFailures.Clear();
        _materialStatuses.Clear();
        _materialNames.Clear();
        _activeMaterialResolver = null;
        _activeMaterialRevision = -1;
    }

    private void ResetTextState()
    {
        _textLayouts.Clear();
        _activeTextResourceResolver = null;
        _activeTextScene = null;
        _activeTextRevision = null;
    }

    private void RefreshMaterialBitmaps()
    {
        if (Scene is not { } scene)
            return;

        MaterialBitmapKey[] required = scene.Primitives
            .OfType<MenuPreviewMaterial>()
            .Where(material =>
                _materialSnapshots.ContainsKey(material.MaterialName))
            .Select(material => MaterialBitmapKey.Create(
                material.MaterialName,
                material.Tint,
                _materialSnapshots[material.MaterialName].SelectMipLevel(
                    Math.Abs(material.Placement.OutputBounds.Width), Math.Abs(material.Placement.OutputBounds.Height))))
            .Concat(_textLayouts.Values.SelectMany(RequiredGlyphBitmapKeys))
            .Distinct()
            .ToArray();
        var requiredSet = required.ToHashSet();
        foreach (MaterialBitmapKey obsolete in _materialBitmaps.Keys
                     .Where(key => !requiredSet.Contains(key))
                     .ToArray())
        {
            _materialBitmaps.Remove(obsolete, out Bitmap? bitmap);
            bitmap?.Dispose();
        }

        foreach (MaterialBitmapKey key in required)
            GetOrCreateMaterialBitmap(_materialSnapshots[key.MaterialName], key);
    }

    private Bitmap GetOrCreateMaterialBitmap(MenuPreviewMaterialSnapshot snapshot, MaterialBitmapKey key)
    {
        if (!_materialBitmaps.TryGetValue(key, out Bitmap? bitmap))
        {
            bitmap = CreateMaterialBitmap(snapshot, key);
            _materialBitmaps.Add(key, bitmap);
        }
        return bitmap;
    }

    private IEnumerable<MaterialBitmapKey> RequiredGlyphBitmapKeys(
        MenuPreviewTextLayout layout)
    {
        if (layout.GlyphRun is not { CanRender: true } glyphRun ||
            string.IsNullOrWhiteSpace(glyphRun.MaterialName) ||
            !_materialSnapshots.ContainsKey(glyphRun.MaterialName))
        {
            yield break;
        }

        foreach (UiGlyphColorRun colorRun in glyphRun.ColorRuns)
        {
            yield return MaterialBitmapKey.Create(
                glyphRun.MaterialName,
                MenuPreviewTextLayoutPlanner.ResolveGlyphColor(
                    layout.Source.Color,
                    colorRun.CaretColorCode));
        }
    }

    private static ReadOnlyMemory<byte> ValidateMaterialPayload(
        MenuPreviewMaterialSnapshot snapshot)
    {
        if (snapshot.Width <= 0 || snapshot.Height <= 0)
        {
            throw new InvalidDataException(
                "Decoded material image dimensions must be positive.");
        }

        int expectedByteCount = checked(snapshot.Width * snapshot.Height * 4);
        if (snapshot.RgbaByteCount != expectedByteCount)
        {
            throw new InvalidDataException(
                "Decoded material image does not contain a complete RGBA payload.");
        }

        return snapshot.RgbaBytes;
    }
}
