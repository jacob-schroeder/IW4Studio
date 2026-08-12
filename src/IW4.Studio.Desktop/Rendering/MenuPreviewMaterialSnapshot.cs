using IW4.Render.Materials;
using IW4.Render.Textures;
using IW4.Render.UI;

namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// Immutable, UI-neutral material preview payload. Callers receive their own
/// RGBA/PNG copies so neither Avalonia nor another presentation framework
/// leaks into the Studio document and rendering boundaries.
/// </summary>
public sealed class MenuPreviewMaterialSnapshot
{
    private readonly byte[] _rgbaBytes;
    private readonly byte[] _pngBytes;
    private readonly IReadOnlyList<UiMaterialPreviewDiagnostic> _diagnostics;
    private readonly IReadOnlyList<UiMaterialExecutionDiagnostic>
        _executionDiagnostics;
    private readonly IReadOnlyList<UiMaterialExecutionDiagnostic>
        _cpuPreviewDiagnostics;

    internal MenuPreviewMaterialSnapshot(
        UiMaterialPreviewPlan plan,
        GfxImagePreviewSnapshot preview,
        UiMaterialDrawPlan? executionPlan = null,
        UiMaterialCpuPreviewPlan? cpuPreviewPlan = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(preview);

        MaterialName = plan.Material.Name;
        ImageName = plan.SelectedImageMetadata?.Name ?? preview.Name;
        Role = plan.SelectedTexture?.Role ??
            MapRenderEditorMaterialTextureRole.Unknown;
        Width = preview.Width;
        Height = preview.Height;
        Format = preview.Format;
        HasTransparency = preview.HasTransparency;
        ExecutionTemplate = executionPlan?.Packet;
        CpuPreviewCompositeState = cpuPreviewPlan?.CompositeState;
        Fidelity = plan.Fidelity;
        Atlas = plan.Atlas;
        SamplerState = plan.SelectedSamplerState;
        _diagnostics = Array.AsReadOnly(plan.Diagnostics.ToArray());
        _executionDiagnostics = Array.AsReadOnly(
            executionPlan?.Diagnostics.ToArray() ?? []);
        _cpuPreviewDiagnostics = Array.AsReadOnly(
            cpuPreviewPlan?.Diagnostics.ToArray() ?? []);
        _rgbaBytes = preview.GetRgbaBytesCopy();
        _pngBytes = preview.GetPngBytesCopy();
    }

    public string MaterialName { get; }

    public string ImageName { get; }

    public MapRenderEditorMaterialTextureRole Role { get; }

    public int Width { get; }

    public int Height { get; }

    public string Format { get; }

    public bool HasTransparency { get; }

    public int RgbaByteCount => _rgbaBytes.Length;

    public long RetainedByteCount => checked(
        _rgbaBytes.LongLength + _pngBytes.LongLength);

    public UiMaterialPreviewFidelity Fidelity { get; }

    /// <summary>
    /// Unit-quad execution template proving the material's canonical
    /// 2d/slot-4 unlit capability. It does not promote the displayed preview's
    /// fidelity: a geometry backend must re-plan and execute each actual draw
    /// through UiMaterialDrawPlanner instead of submitting this unit quad.
    /// </summary>
    public UiMaterialDrawPacket? ExecutionTemplate { get; }

    /// <summary>
    /// Fixed-function state that the Menu preview can composite with its CPU
    /// target. This remains distinct from <see cref="ExecutionTemplate"/>,
    /// which is reserved for generic renderer execution.
    /// </summary>
    public UiMaterialCpuPreviewCompositeState? CpuPreviewCompositeState { get; }

    public UiMaterialPreviewAtlasMetadata Atlas { get; }

    public MapRenderSamplerState? SamplerState { get; }

    public IReadOnlyList<UiMaterialPreviewDiagnostic> Diagnostics =>
        _diagnostics;

    public IReadOnlyList<UiMaterialExecutionDiagnostic>
        ExecutionDiagnostics => _executionDiagnostics;

    public IReadOnlyList<UiMaterialExecutionDiagnostic>
        CpuPreviewDiagnostics => _cpuPreviewDiagnostics;

    /// <summary>
    /// Read-only decoded RGBA storage shared with presentation backends that
    /// need to sample the material.
    /// </summary>
    public ReadOnlyMemory<byte> RgbaBytes => _rgbaBytes;

    public byte[] GetRgbaBytesCopy() => _rgbaBytes.ToArray();

    public byte[] GetPngBytesCopy() => _pngBytes.ToArray();
}
