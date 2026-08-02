namespace IW4.Render.Materials;

public static class MapRenderPassClassifier
{
    private const uint RsxPolygonModeLine = 0x1B01;
    private const uint RsxPolygonModeFill = 0x1B02;

    public const string CameraColor = "CameraColor";
    public const string CameraColorWithUnresolvedCodeSamplers = "CameraColorWithUnresolvedCodeSamplers";
    public const string CameraColorWithMissingState = "CameraColorWithMissingState";
    public const string ShadowDepth = "ShadowDepth";
    public const string NonColorWire = "NonColorWire";
    public const string NonColorWrite = "NonColorWrite";
    public const string NonFillColorWrite = "NonFillColorWrite";

    public static string Classify(string techniqueName, MapRenderState state, int unresolvedCodeSamplerCount)
    {
        if (IsShadowDepthTechnique(techniqueName))
            return ShadowDepth;

        if (!state.HasState)
            return CameraColorWithMissingState;

        if (state.ColorMask == 0 && state.PolygonMode == RsxPolygonModeLine)
            return NonColorWire;

        if (state.ColorMask == 0)
            return NonColorWrite;

        if (state.PolygonMode != RsxPolygonModeFill)
            return NonFillColorWrite;

        return unresolvedCodeSamplerCount > 0
            ? CameraColorWithUnresolvedCodeSamplers
            : CameraColor;
    }

    public static bool CanSubmitToCameraColor(string passClass)
    {
        return passClass is CameraColor or CameraColorWithUnresolvedCodeSamplers or CameraColorWithMissingState;
    }

    private static bool IsShadowDepthTechnique(string techniqueName)
    {
        return techniqueName.Contains("shadowmap", StringComparison.OrdinalIgnoreCase);
    }
}
