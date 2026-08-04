namespace IW4.Render.UI.Text;

/// <summary>
/// One Menu text layout request in the 640x480 virtual coordinate space. The
/// baseline follows the native renderer convention: signed glyph offsets are
/// applied relative to this point.
/// </summary>
public sealed record UiGlyphRunRequest(
    string Text,
    float BaselineX,
    float BaselineY,
    float TextScale);

public readonly record struct UiGlyphRect(
    float X,
    float Y,
    float Width,
    float Height);

public readonly record struct UiGlyphTextureRect(
    float S0,
    float T0,
    float S1,
    float T1);

/// <summary>
/// An immutable font-atlas quad. RequestedUnicodeScalar retains the source
/// character even when the native missing-glyph fallback is used.
/// </summary>
public sealed record UiGlyphQuad(
    int SourceUtf16Index,
    int SourceUtf16Length,
    int RequestedUnicodeScalar,
    ushort RenderedLetter,
    bool IsFallback,
    UiGlyphRect Bounds,
    UiGlyphTextureRect TextureCoordinates,
    int ColorRunIndex);

/// <summary>
/// A contiguous quad range selected by an IW caret color escape. Null means
/// the caller's base color; '7' is normalized back to that base color. Codes
/// 0-6 and the runtime-dependent team colors 8-9 remain symbolic so a backend
/// can apply the appropriate scenario palette without changing glyph layout.
/// </summary>
public sealed record UiGlyphColorRun(
    int FirstQuadIndex,
    int QuadCount,
    char? CaretColorCode);

/// <summary>
/// Immutable renderer-neutral result of laying out a string against one IW4
/// Font asset. Only the material identity is retained; the renderer resolves
/// the active canonical material at its captured asset-pool revision.
/// </summary>
public sealed class UiGlyphRunPlan
{
    private readonly UiGlyphQuad[] _quads;
    private readonly UiGlyphColorRun[] _colorRuns;
    private readonly UiTextDiagnostic[] _diagnostics;
    private readonly UiTextDiagnostic[] _blockers;

    internal UiGlyphRunPlan(
        string fontName,
        int pixelHeight,
        string? materialName,
        string? glowMaterialName,
        float normalizedScale,
        float maxLineAdvance,
        float endPenX,
        float endPenY,
        IEnumerable<UiGlyphQuad> quads,
        IEnumerable<UiGlyphColorRun> colorRuns,
        IEnumerable<UiTextDiagnostic> diagnostics)
    {
        FontName = fontName;
        PixelHeight = pixelHeight;
        MaterialName = materialName;
        GlowMaterialName = glowMaterialName;
        NormalizedScale = normalizedScale;
        MaxLineAdvance = maxLineAdvance;
        EndPenX = endPenX;
        EndPenY = endPenY;
        _quads = quads.ToArray();
        _colorRuns = colorRuns.ToArray();
        _diagnostics = diagnostics.ToArray();
        _blockers = _diagnostics
            .Where(value =>
                value.Severity == UiTextDiagnosticSeverity.Blocker)
            .ToArray();
        Quads = Array.AsReadOnly(_quads);
        ColorRuns = Array.AsReadOnly(_colorRuns);
        Diagnostics = Array.AsReadOnly(_diagnostics);
        Blockers = Array.AsReadOnly(_blockers);
    }

    public string FontName { get; }

    public int PixelHeight { get; }

    public string? MaterialName { get; }

    /// <summary>
    /// Optional material identity used by native glow text styles. The base
    /// glyph layout is shared by both passes.
    /// </summary>
    public string? GlowMaterialName { get; }

    /// <summary>Native virtual-space scale: textScale * 48 / pixelHeight.</summary>
    public float NormalizedScale { get; }

    /// <summary>
    /// Maximum horizontal advance of any line, matching the width authority
    /// used by native R_TextWidth for multiline alignment.
    /// </summary>
    public float MaxLineAdvance { get; }

    public float EndPenX { get; }

    public float EndPenY { get; }

    public IReadOnlyList<UiGlyphQuad> Quads { get; }

    public IReadOnlyList<UiGlyphColorRun> ColorRuns { get; }

    public IReadOnlyList<UiTextDiagnostic> Diagnostics { get; }

    public IReadOnlyList<UiTextDiagnostic> Blockers { get; }

    public bool CanRender =>
        !string.IsNullOrWhiteSpace(MaterialName) && Blockers.Count == 0;
}
