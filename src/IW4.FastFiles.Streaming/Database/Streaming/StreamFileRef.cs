namespace IW4.FastFiles.Streaming.Database.Streaming;

public readonly record struct StreamFileRef(uint FileIndex, string Name, StreamFileKind Kind);
