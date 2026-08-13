namespace IW4.Render.Techniques;

public static class MaterialPassClassifier
{
    public const string CameraColor = "CameraColor";
    public const string CameraColorWithUnresolvedCodeSamplers = "CameraColorWithUnresolvedCodeSamplers";
    public const string CameraColorWithMissingState = "CameraColorWithMissingState";
    public const string ShadowDepth = "ShadowDepth";
    public const string NonColorWire = "NonColorWire";
    public const string NonColorWrite = "NonColorWrite";
    public const string NonFillColorWrite = "NonFillColorWrite";

    public static string Classify(string techniqueName, RenderState state, int unresolvedCodeSamplerCount)
    {
        if (IsShadowDepthTechnique(techniqueName))
            return ShadowDepth;

        if (!state.HasState)
            return CameraColorWithMissingState;

        if (state.ColorMask == RsxColorMask.None &&
            state.PolygonMode == RsxPolygonMode.Line)
            return NonColorWire;

        if (state.ColorMask == RsxColorMask.None)
            return NonColorWrite;

        if (state.PolygonMode != RsxPolygonMode.Fill)
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
