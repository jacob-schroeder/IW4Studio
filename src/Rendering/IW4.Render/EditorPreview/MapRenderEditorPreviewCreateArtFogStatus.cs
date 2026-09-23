namespace IW4.Render.EditorPreview;

public enum MapRenderEditorPreviewCreateArtFogStatus
{
    Ready = 0,
    InvalidMapIdentity,
    AssetPoolRevisionMismatch,
    CanonicalRawFileAbsent,
    RawFileBufferMissing,
    RawFileMetadataInvalid,
    RawFileDecodeFailed,
    SetExpFogMissing,
    SetExpFogAmbiguous,
    SetExpFogMalformed,
    SetExpFogTransitionUnsupported,
    SetExpFogValueOutOfRange
}
