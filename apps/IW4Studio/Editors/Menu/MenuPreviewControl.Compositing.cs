using Avalonia.Media;
using IW4.Render.Techniques;
using IW4.Render.UI;
using IW4.Studio.Desktop.Documents.MenuEditing.Preview;
using IW4.Studio.Desktop.Rendering;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuPreviewControl
{
    private void DrawPrimitives(
        DrawingContext context,
        MenuPreviewScene scene,
        PreviewTransform transform)
    {
        IReadOnlyList<MenuPreviewPrimitive> primitives = scene.Primitives;
        if (TryGetCpuCompositeRun(
                scene,
                primitives,
                out int runStart,
                out int runEnd) &&
            DrawCpuCompositeRun(
                context,
                scene,
                primitives,
                runStart,
                runEnd,
                transform))
        {
            for (int index = runEnd; index < primitives.Count; index++)
                DrawPrimitive(context, scene, primitives[index], transform);
            return;
        }
        if (runStart < 0)
            ReleaseCpuCompositeSurface();
        for (int index = 0; index < primitives.Count; index++)
            DrawPrimitive(context, scene, primitives[index], transform);
    }

    /// <summary>
    /// Finds the one native-target dependency sequence that needs CPU
    /// fixed-function compositing. A material whose generic UI packet is
    /// unavailable but whose CPU state is decoded is state-special. The run
    /// retains the preceding alpha history unless its first special draw
    /// clears alpha across the stage while preserving destination RGB.
    /// </summary>
    private bool TryGetCpuCompositeRun(
        MenuPreviewScene scene,
        IReadOnlyList<MenuPreviewPrimitive> primitives,
        out int start,
        out int end)
    {
        start = -1;
        end = -1;
        if (!AreSceneMaterialsSettled(scene))
            return false;

        int firstSpecial = -1;
        for (int index = 0; index < primitives.Count; index++)
        {
            if (primitives[index] is not MenuPreviewMaterial material ||
                !RequiresCpuComposite(material))
            {
                continue;
            }

            if (firstSpecial < 0)
                firstSpecial = index;
            end = index + 1;
        }

        if (firstSpecial < 0)
            return false;

        // A normal source-over draw can lower native alpha because this UI
        // path applies SRC_ALPHA/ONE_MINUS_SRC_ALPHA to alpha. A full alpha
        // clear makes earlier alpha irrelevant and allows the remaining draws
        // to become an overlay on top of the earlier scene, including text.
        start = primitives[firstSpecial] is MenuPreviewMaterial firstMaterial &&
                ClearsStageAlpha(firstMaterial, scene.Settings)
            ? firstSpecial
            : 0;
        for (int index = start; index < end; index++)
        {
            if (primitives[index] is MenuPreviewMaterial material &&
                (HasZeroArea(CompositeBounds(material, scene.Settings)) ||
                 HasCpuCompositeSource(material)))
                continue;

            start = -1;
            end = -1;
            return false;
        }

        return true;
    }

    private bool AreSceneMaterialsSettled(MenuPreviewScene scene)
    {
        foreach (MenuPreviewPrimitive primitive in scene.Primitives)
        {
            if (primitive is MenuPreviewMaterial material &&
                !_materialSnapshots.ContainsKey(material.MaterialName) &&
                !_materialFailures.ContainsKey(material.MaterialName))
            {
                return false;
            }
        }

        return true;
    }

    private bool RequiresCpuComposite(MenuPreviewMaterial material) =>
        _materialSnapshots.TryGetValue(
            material.MaterialName,
            out MenuPreviewMaterialSnapshot? snapshot) &&
        snapshot.CpuPreviewCompositeState is not null &&
        snapshot.ExecutionTemplate is null;

    private bool HasCpuCompositeSource(MenuPreviewMaterial material) =>
        _materialSnapshots.TryGetValue(
            material.MaterialName,
            out MenuPreviewMaterialSnapshot? snapshot) &&
        snapshot.CpuPreviewCompositeState is not null &&
        snapshot.SamplerState is not null &&
        _materialPixels.ContainsKey(material.MaterialName);

    private bool ClearsStageAlpha(MenuPreviewMaterial material, MenuPreviewSettings settings)
    {
        if (!_materialSnapshots.TryGetValue(material.MaterialName, out var snapshot) ||
            !ClearsAlpha(snapshot.CpuPreviewCompositeState))
        {
            return false;
        }

        var bounds = VisibleMaterialBounds(material, CompositeBounds(material, settings));
        return double.IsFinite(bounds.Left) && double.IsFinite(bounds.Top) &&
            double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) &&
            bounds.Left <= 0 && bounds.Top <= 0 &&
            bounds.Right >= settings.CanvasWidth && bounds.Bottom >= settings.CanvasHeight;
    }

    private static bool ClearsAlpha(UiMaterialCpuPreviewCompositeState? state) => state is
    {
        ColorWriteMask: UiMaterialCpuPreviewColorWriteMask.Alpha,
        AlphaTest: AlphaTestMode.Disabled,
        CullMode: CullMode.Disabled,
        Blend:
        {
            IsEnabled: true,
            AlphaEquation: UiMaterialCpuPreviewBlendEquation.Add,
            SourceAlphaFactor: UiMaterialCpuPreviewBlendFactor.Zero,
            DestinationAlphaFactor: UiMaterialCpuPreviewBlendFactor.Zero
        }
    };
}
