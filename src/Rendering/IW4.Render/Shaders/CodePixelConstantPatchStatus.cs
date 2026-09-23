namespace IW4.Render.Shaders;

public enum CodePixelConstantPatchStatus
{
    DirectSourceResolved = 0,
    NonStableScopeDeferred,
    DerivedSourceDeferred,
    DestinationUnmapped,
    DefaultOnlyPatchEntry,
    PatchSiteUnmatched,
    PatchSiteAmbiguous
}
