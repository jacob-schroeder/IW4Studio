using IW4.Formats.SourceFormat.XAnim;
using IW4.Game.Assets.XModel;
using IW4.Render;
using IW4.Render.EditorPreview;
using IW4.Studio.Desktop.Editors.XModel;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.XAnim;

/// <summary>An explicit use of a clip. Only some uses identify a model to render.</summary>
public sealed class XAnimPreviewReference
{
    internal delegate (XAnimPreviewScene? PoseScene, XModelRenderScene? ModelScene, string? Reason)
        BuildModel(XAnimPlaybackClip clip);

    internal XAnimPreviewReference(string name, string source, XModelCameraPreset cameraPreset,
        BuildModel? build = null)
    {
        Name = name;
        Source = source;
        CameraPreset = cameraPreset;
        Build = build;
    }

    public string Name { get; }
    public string Source { get; }
    internal XModelCameraPreset CameraPreset { get; }
    internal BuildModel? Build { get; }

    internal static IReadOnlyList<XAnimPreviewReference> Find(FastFileWorkspace workspace, string animationName)
    {
        var matches = XAnimWeaponPreview.Find(workspace, animationName).ToList();
        var uses = XAnimMapReferences.Find(workspace, animationName)
            .Concat(XAnimScriptReferences.Find(workspace, animationName))
            .Concat(XAnimTableReferences.Find(workspace, animationName));
        foreach (var (source, model) in uses.Distinct())
        {
            if (model is null)
            {
                string sourceType = Path.GetExtension(source).ToLowerInvariant() switch
                {
                    ".atr" => "Animation tree",
                    ".gsc" or ".csc" or ".script" => "Script",
                    ".csv" => "String table",
                    _ => "Map entity"
                };
                matches.Add(new(source, sourceType, XModelCameraPreset.Model));
            }
            else
            {
                matches.Add(new(model, source, XModelCameraPreset.Model, clip =>
                {
                    if (!XAnimModelPreview.TryResolveModel(workspace, model, out XModelAsset? asset, out _) || asset is null)
                        return (null, null, $"Model '{model}' is unavailable in this workspace.");
                    return XAnimModelPreview.Build(workspace, clip, [new XAnimPreviewModelComponent(asset)], []);
                }));
            }
        }

        return matches.OrderBy(reference => reference.Build is null)
            .ThenBy(reference => reference.CameraPreset != XModelCameraPreset.Side)
            .ThenBy(reference => reference.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
