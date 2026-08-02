namespace IW4.Render.EditorPreview;

public enum MapRenderEditorPreviewVisionStatus
{
    Ready,
    InvalidMapIdentity,
    AssetPoolRevisionMismatch,
    CreateArtRawFileAbsent,
    CreateArtRawFileInvalid,
    VisionSetCallMissing,
    VisionSetCallAmbiguous,
    VisionSetCallMalformed,
    VisionSetTransitionUnsupported,
    VisionRawFileAbsent,
    VisionRawFileInvalid,
    VisionFieldMalformed,
    VisionFieldMissing,
    VisionFieldValueOutOfRange
}
