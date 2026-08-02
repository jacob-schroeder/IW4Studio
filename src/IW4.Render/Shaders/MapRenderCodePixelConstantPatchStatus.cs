namespace IW4.Render.Shaders;

public enum MapRenderCodePixelConstantPatchStatus
{
    DirectSourceResolved = 0,
    NonStableScopeDeferred,
    DerivedSourceDeferred,
    DestinationUnmapped,
    DefaultOnlyPatchEntry,
    PatchSiteUnmatched,
    PatchSiteAmbiguous
}
