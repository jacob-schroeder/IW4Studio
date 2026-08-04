namespace IW4.Render.UI.Text;

public enum UiTextDiagnosticSeverity
{
    Information = 0,
    Warning = 1,
    Blocker = 2
}

public enum UiTextDiagnosticCode
{
    UnknownFontEnum = 0,
    FontAssetNotFound = 1,
    AssetPoolChanged = 2,
    InvalidTextScale = 3,
    InvalidFontPixelHeight = 4,
    FontGlyphTableEmpty = 5,
    FontGlyphCountMismatch = 6,
    DuplicateGlyph = 7,
    MissingGlyph = 8,
    UnsupportedUnicodeScalar = 9,
    InvalidGlyphTextureCoordinates = 10,
    FontMaterialMissing = 11,
    FontMaterialNameMissing = 12,
    InvalidTextOrigin = 13,
    UnsupportedInlineMaterialCommand = 14,
    InvalidNativeGlyphTable = 15
}

/// <summary>
/// Renderer-neutral diagnostic produced while selecting or laying out an IW4
/// UI font. Source indices address UTF-16 input because that is the indexing
/// convention used by .NET callers.
/// </summary>
public sealed record UiTextDiagnostic(
    UiTextDiagnosticCode Code,
    UiTextDiagnosticSeverity Severity,
    string Message,
    int? SourceUtf16Index = null,
    int? UnicodeScalar = null);
