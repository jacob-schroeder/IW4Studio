namespace IW4.Render.Assets;

public enum MapRenderWorldTextureBindingStatus
{
    Ready = 0,
    RuntimeStateUnavailable = 1,
    WorldIdentityMismatch = 2,
    SlotOutOfRange = 3,
    SourceImageUnavailable = 4
}
