using System.ComponentModel;
using Avalonia.Threading;
using IW4.Formats.RawFile;
using IW4.Formats.XModel;
using IW4.Game.Assets.RawFile;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Gsc.Analysis;
using IW4.Gsc.Character;
using IW4.Render;
using IW4.Render.SceneBuilding;
using IW4.Render.EditorPreview;
using IW4.Studio.Desktop.Editors.Gsc;
using IW4.Studio.Desktop.Editors.XModel;
using IW4.Studio.Desktop.Gsc;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Desktop.Workbench.Tools.GscUsages;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class CharacterEditorViewModel : RawFileEditorViewModel
{
    private const string AliasFolder = "xmodelalias/";
    private readonly AssetEditorSession _session;
    private readonly Action<XAssetType, string>? _openReference;
    private CharacterScriptProjection _projection;
    private (string Body, string Head, string Hands)? _previewNames;
    private CancellationTokenSource? _previewRequest;
    private bool _disposed;
    private bool _importing;
    private string _previewStatus = string.Empty;
    private readonly HashSet<string> _localAliases = new(StringComparer.OrdinalIgnoreCase);

    internal CharacterEditorViewModel(AssetEditorSession session, IGscAnalyzer analyzer,
        GscWorkspaceIndexService? workspace, IGscSourceNavigator? navigator,
        IGscUsagesPresenter? usages, Action<XAssetType, string>? openReference)
        : base(session, analyzer, workspace, navigator, usages)
    {
        _session = session;
        _openReference = openReference;
        _projection = CharacterScriptProjection.Parse(PayloadInput);
        RefreshOptions();
        PropertyChanged += DraftPropertyChanged;
        _session.WorkspaceAssetsChanged += WorkspaceChanged;
        QueuePreview();
    }

    public bool CanOverrideCharacter => _openReference is not null && _session.CanOverrideCharacter;
    public string CharacterName => Path.GetFileNameWithoutExtension(OriginalName.Replace('\\', '/'));
    public string BodyModel { get => _projection.Body.Value ?? ""; set => Replace(CharacterFieldKind.Body, value); }
    public string HeadAlias { get => _projection.HeadAlias.Value ?? ""; set => Replace(CharacterFieldKind.HeadAlias, value); }
    public string ViewHands { get => _projection.Viewhands.Value ?? ""; set => Replace(CharacterFieldKind.Viewhands, value); }
    public string Voice { get => _projection.Voice.Value ?? ""; set => Replace(CharacterFieldKind.Voice, value); }
    public bool CanEditBody => IsEditable && _projection.Body.CanEdit;
    public bool CanEditHead => IsEditable && _projection.HeadAlias.CanEdit;
    public bool CanEditHands => IsEditable && _projection.Viewhands.CanEdit;
    public bool CanEditVoice => IsEditable && _projection.Voice.CanEdit;
    public IReadOnlyList<string> BodyOptions { get; private set; } = [];
    public IReadOnlyList<string> HeadOptions { get; private set; } = [];
    public IReadOnlyList<string> HandsOptions { get; private set; } = [];
    public XModelRenderScene? ThirdPersonScene { get; private set; }
    public XModelRenderScene? BodyScene { get; private set; }
    public XModelRenderScene? HeadScene { get; private set; }
    public XModelRenderScene? HandsScene { get; private set; }
    public string PreviewStatus { get => _previewStatus; private set => SetProperty(ref _previewStatus, value); }

    private void RefreshOptions()
    {
        var models = _session.CharacterReferenceNames(XAssetType.XModel);
        BodyOptions = models.OrderByDescending(name => name.Contains("body", StringComparison.OrdinalIgnoreCase)).ToArray();
        HandsOptions = models.OrderByDescending(name => name.Contains("hands", StringComparison.OrdinalIgnoreCase)).ToArray();
        HeadOptions = _session.CharacterReferenceNames(XAssetType.RawFile)
            .Where(name => name.StartsWith(AliasFolder, StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase))
            .Select(name => name[AliasFolder.Length..^4]).ToArray();
        OnPropertyChanged(nameof(BodyOptions));
        OnPropertyChanged(nameof(HeadOptions));
        OnPropertyChanged(nameof(HandsOptions));
    }

    private void Replace(CharacterFieldKind kind, string? value)
    {
        if (!IsEditable || string.IsNullOrWhiteSpace(value))
            return;
        if (_projection.TryReplace(kind, value, out string source, out string? reason))
            PayloadInput = source;
        else if (reason is not null)
            StatusMessage = reason;
    }

    private void DraftPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(PayloadInput))
            return;
        _projection = CharacterScriptProjection.Parse(PayloadInput);
        OnPropertyChanged(nameof(BodyModel));
        OnPropertyChanged(nameof(HeadAlias));
        OnPropertyChanged(nameof(ViewHands));
        OnPropertyChanged(nameof(Voice));
        OnPropertyChanged(nameof(CanEditBody));
        OnPropertyChanged(nameof(CanEditHead));
        OnPropertyChanged(nameof(CanEditHands));
        OnPropertyChanged(nameof(CanEditVoice));
        QueuePreview();
    }

    private void WorkspaceChanged(object? sender, EventArgs args)
    {
        // A publication can notify both the row and provider collections on a worker thread.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => WorkspaceChanged(sender, args));
            return;
        }
        if (_disposed) return;
        RefreshOptions();
        OnPropertyChanged(nameof(CanOverrideCharacter));
        _previewNames = null;
        QueuePreview();
    }

    public void OverrideCharacter()
    {
        try { OpenReference(XAssetType.RawFile, _session.OverrideCharacter()); }
        catch (Exception ex) when (IsAssetError(ex)) { StatusMessage = ex.Message; }
    }

    public void OpenBody() => OpenReference(XAssetType.XModel, BodyModel);
    public void OpenHands() => OpenReference(XAssetType.XModel, ViewHands);

    private void OpenReference(XAssetType type, string name)
    {
        if (!string.IsNullOrWhiteSpace(name)) _openReference?.Invoke(type, name);
    }

    public void OpenHeadAlias(bool shared)
    {
        if (string.IsNullOrEmpty(HeadAlias)) return;
        string path = AliasPath(HeadAlias);
        if (shared || _localAliases.Contains(HeadAlias))
        {
            OpenReference(XAssetType.RawFile, path);
            return;
        }
        if (!CanEditHead) return;
        try
        {
            var alias = _session.ResolveCharacterReference<RawFileAsset>(XAssetType.RawFile, path, out _)
                ?? throw new InvalidOperationException($"Load the fastfile containing '{path}' to copy this alias.");
            string localName = AvailableName("alias_" + CharacterName, HeadOptions);
            if (!_projection.TryReplace(CharacterFieldKind.HeadAlias, localName, out string source, out string? reason))
                throw new InvalidOperationException(reason);
            _session.CopyCharacterAlias(alias, AliasPath(localName));
            _localAliases.Add(localName);
            PayloadInput = source;
            OpenReference(XAssetType.RawFile, AliasPath(localName));
        }
        catch (Exception ex) when (IsAssetError(ex)) { StatusMessage = ex.Message; }
    }

    public async Task ImportGlbAsync(string path, bool viewHands)
    {
        if (_importing || !(viewHands ? CanEditHands : CanEditBody)) return;
        _importing = true;
        try
        {
            string oldName = viewHands ? ViewHands : BodyModel;
            var template = _session.ResolveCharacterReference<XModelAsset>(XAssetType.XModel, oldName, out _)
                ?? throw new InvalidOperationException($"Load the fastfile containing template '{oldName}' before importing.");
            string newName = AvailableName(CharacterName + (viewHands ? "_viewhands" : "_body"), BodyOptions);
            var compiled = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                if (!XModelGlbReader.TryRead(stream, XModelImportImageDecoder.Decode,
                        out var document, out var blockers) || document is null)
                    throw new InvalidDataException(string.Join(" ", blockers));
                return XModelCharacterImporter.Compile(template, document, newName, path, viewHands);
            });
            if (_disposed) return;
            if (oldName != (viewHands ? ViewHands : BodyModel))
                throw new InvalidOperationException("The template changed during import. Select the intended template and import again.");
            if (!_projection.TryReplace(viewHands ? CharacterFieldKind.Viewhands : CharacterFieldKind.Body,
                    newName, out string source, out string? reason))
                throw new InvalidOperationException(reason);
            _session.AddCharacterModel(compiled);
            PayloadInput = source;
            StatusMessage = $"Added '{newName}' to the fastfile. Apply to use it in this character.";
        }
        catch (Exception ex) when (IsAssetError(ex)) { if (!_disposed) StatusMessage = ex.Message; }
        finally { _importing = false; }
    }

    private static string AliasPath(string alias) => $"{AliasFolder}{alias}.gsc";

    private static string AvailableName(string name, IReadOnlyList<string> existing)
    {
        var names = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string candidate = name;
        for (int suffix = 2; names.Contains(candidate); suffix++)
            candidate = $"{name}_{suffix}";
        return candidate;
    }

    private void QueuePreview()
    {
        var names = (Body: BodyModel, Head: HeadAlias, Hands: ViewHands);
        if (_previewNames == names) return;
        _previewNames = names;
        _previewRequest?.Cancel();
        var request = new CancellationTokenSource();
        _previewRequest = request;
        PreviewStatus = "Loading character models…";
        _ = LoadPreviewAsync(names, request);
    }

    private async Task LoadPreviewAsync((string Body, string Head, string Hands) names, CancellationTokenSource request)
    {
        try
        {
            CancellationToken token = request.Token;
            await Task.Delay(140, token);
            var result = await Task.Run(() =>
            {
                var missing = new List<string>();
                var builder = new XModelSceneBuilder();
                var images = new WorkspaceGfxImagePayloadResolver(_session.Workspace);
                XModelRenderScene? Build(string name, string label, out XModelAsset? asset)
                {
                    token.ThrowIfCancellationRequested();
                    asset = _session.ResolveCharacterReference<XModelAsset>(XAssetType.XModel, name, out var providers);
                    if (asset is not null && !_session.Workspace.IsBlank)
                    {
                        var source = WorkspaceRenderAssetSource.Create(_session.Workspace, "character models");
                        var scene = builder.Build(asset, source, images, providers);
                        if (scene.DefaultLodIndex >= 0) return scene;
                    }
                    missing.Add(label);
                    return null;
                }

                var body = Build(names.Body, "body", out var bodyModel);
                string head = "";
                var alias = _session.ResolveCharacterReference<RawFileAsset>(XAssetType.RawFile, AliasPath(names.Head), out _);
                if (alias is not null)
                {
                    var raw = new RawFileDraft(alias);
                    if (raw.GetContentClassification().TextEncoding is { } encoding &&
                        CharacterScriptProjection.TryReadHeadAlias(
                            RawFileContentClassifier.DecodeText(raw.GetLogicalContentCopy(), encoding), out var heads, out _))
                        head = heads.FirstOrDefault(model => _session.ResolveCharacterReference<XModelAsset>(XAssetType.XModel, model, out _) is not null) ?? "";
                }
                var headScene = Build(head, "head", out var headModel);
                var hands = Build(names.Hands, "view hands", out _);
                string status = missing.Count == 0 ? "" : $"Unavailable in this workspace: {string.Join(", ", missing)}.";
                var assembled = body;
                if (body is not null && headScene is not null && bodyModel is not null && headModel is not null)
                {
                    // Stock empty-tag head attachments share the head root's name with a body bone.
                    // A head is authored in its own bind space, so align it to that body transform.
                    try
                    {
                        string? root = headModel.BoneNames.FirstOrDefault()?.Text;
                        if (headModel.NumRootBones != 1 || string.IsNullOrEmpty(root))
                            throw new InvalidDataException("The head has no unique named attachment root.");
                        assembled = XModelSceneBuilder.Compose([new(bodyModel), new(headModel, root)],
                            body.DefaultLodIndex, model => ReferenceEquals(model, bodyModel) ? body : headScene);
                        // Frame the alias in its worn orientation in the References inspector, too.
                        var bodyLod = body.Lods.First(lod => lod.LodIndex == body.DefaultLodIndex);
                        var attachedSurfaces = assembled.Lods[0].Surfaces.Skip(bodyLod.Surfaces.Count).ToArray();
                        var bounds = RenderBounds.Empty;
                        foreach (var surface in attachedSurfaces.Where(surface => surface.Bounds.IsValid))
                            bounds = bounds.Include(surface.Bounds.Min).Include(surface.Bounds.Max);
                        if (bounds.IsValid)
                        {
                            var headLod = new XModelRenderLod(0, 0, bounds, attachedSurfaces);
                            headScene = new XModelRenderScene(headScene.Name, [headLod], 0, bounds,
                                assembled.Bones, headScene.Diagnostics);
                        }
                    }
                    catch (Exception ex) when (IsAssetError(ex))
                    {
                        status = $"Head attachment unavailable: {ex.Message} {status}".TrimEnd();
                    }
                }
                return (body, headScene, hands, assembled, status);
            }, token);
            token.ThrowIfCancellationRequested();
            SetScenes(result.body, result.headScene, result.hands, result.assembled, result.status);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) when (IsAssetError(ex))
        {
            if (!request.IsCancellationRequested) SetScenes(null, null, null, null, ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_previewRequest, request)) _previewRequest = null;
            request.Dispose();
        }
    }

    private static bool IsAssetError(Exception ex) =>
        ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or OverflowException or UnauthorizedAccessException;

    private void SetScenes(XModelRenderScene? body, XModelRenderScene? head, XModelRenderScene? hands, XModelRenderScene? assembled, string status)
    {
        ThirdPersonScene = assembled;
        BodyScene = body;
        HeadScene = head;
        HandsScene = hands;
        OnPropertyChanged(nameof(ThirdPersonScene));
        OnPropertyChanged(nameof(BodyScene));
        OnPropertyChanged(nameof(HeadScene));
        OnPropertyChanged(nameof(HandsScene));
        PreviewStatus = status;
    }

    public override void Dispose()
    {
        _disposed = true;
        _session.WorkspaceAssetsChanged -= WorkspaceChanged;
        PropertyChanged -= DraftPropertyChanged;
        _previewRequest?.Cancel();
        base.Dispose();
    }
}
