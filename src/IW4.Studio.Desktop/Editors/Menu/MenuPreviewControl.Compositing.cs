using Avalonia.Media;
using IW4.Studio.Documents.MenuEditing.Preview;
using IW4.Studio.Rendering;

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
            for (int index = 0; index < runStart; index++)
                DrawPrimitive(context, scene, primitives[index], transform);
            for (int index = runEnd; index < primitives.Count; index++)
                DrawPrimitive(context, scene, primitives[index], transform);
            return;
        }

        ReleaseCpuCompositeSurface();
        for (int index = 0; index < primitives.Count; index++)
            DrawPrimitive(context, scene, primitives[index], transform);
    }

    /// <summary>
    /// Finds the one native-target dependency sequence that needs CPU
    /// fixed-function compositing. A material whose generic UI packet is
    /// unavailable but whose CPU state is decoded is state-special; every
    /// draw from the scene prefix through the last special material shares
    /// the same native color/alpha target and must be submitted together.
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

        for (int index = 0; index < primitives.Count; index++)
        {
            if (primitives[index] is not MenuPreviewMaterial material ||
                !RequiresCpuComposite(material))
            {
                continue;
            }

            end = index + 1;
        }

        if (end < 0)
        {
            start = -1;
            end = -1;
            return false;
        }

        // A normal source-over draw can lower native alpha because this UI
        // path also applies SRC_ALPHA/ONE_MINUS_SRC_ALPHA to alpha. Therefore
        // the CPU target may begin only at the scene prefix, where DrawStage
        // has established alpha one; it must include every intervening
        // material through the last state-special draw.
        start = 0;
        for (int index = start; index < end; index++)
        {
            if (primitives[index] is not MenuPreviewMaterial material ||
                !HasCpuCompositeSource(material))
            {
                start = -1;
                end = -1;
                return false;
            }
        }

        return true;
    }

    private bool AreSceneMaterialsSettled(MenuPreviewScene scene) =>
        scene.Primitives
            .OfType<MenuPreviewMaterial>()
            .Select(material => material.MaterialName)
            .Distinct(StringComparer.Ordinal)
            .All(materialName =>
                _materialSnapshots.ContainsKey(materialName) ||
                _materialFailures.ContainsKey(materialName));

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

}
