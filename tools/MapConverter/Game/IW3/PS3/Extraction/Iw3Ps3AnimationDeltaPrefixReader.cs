namespace MapConverter.Game.IW3.PS3.Extraction;

internal static partial class Iw3Ps3FastFileCatalog
{
    private sealed partial class Reader
    {
        // Current source d3f68/bd228/bc960/b8650/b84a8. Delta is retained
        // only to advance stock shader-prefix traversal; no target animation
        // is emitted by this reader.
        private void AnimationDeltaPrefix(uint raw, int frameCount)
        {
            Pointer? delta = PresenceArray(raw, 4, 8, "animation-delta");
            if (delta is null) return;
            int start = delta.Region.FileOffset;

            Pointer? trans = PresenceArray(U32(start), 4, 4,
                "animation-delta-translation");
            if (trans is not null)
            {
                int row = trans.Region.FileOffset;
                int size = U16(row);
                if (size == 0) ReadBytes(12);
                else
                {
                    Region frames = ReadBytes(0x1c);
                    int indexWidth = frameCount < 256 ? 1 : 2;
                    ReadBytes(Size(size + 1, indexWidth));
                    uint frameRaw = U32(frames.FileOffset + 0x18);
                    if (frameRaw != 0)
                    {
                        bool small = _data[row + 2] != 0;
                        PresenceArray(frameRaw, small ? 1 : 4,
                            Size(size + 1, small ? 3 : 6),
                            "animation-delta-translation-frames");
                    }
                }
            }

            Pointer? quat = PresenceArray(U32(start + 4), 4, 4,
                "animation-delta-quaternion");
            if (quat is null) return;
            int quaternionSize = U16(quat.Region.FileOffset);
            if (quaternionSize == 0)
            {
                ReadBytes(4);
                return;
            }
            Region quaternionFrames = ReadBytes(4);
            int quaternionIndexWidth = frameCount < 256 ? 1 : 2;
            ReadBytes(Size(quaternionSize + 1, quaternionIndexWidth));
            uint quaternionFrameRaw = U32(quaternionFrames.FileOffset);
            if (quaternionFrameRaw != 0)
                PresenceArray(quaternionFrameRaw, 4, Size(quaternionSize + 1, 4),
                    "animation-delta-quaternion-frames");
        }
    }
}
