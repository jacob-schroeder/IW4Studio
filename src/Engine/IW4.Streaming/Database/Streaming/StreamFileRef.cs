namespace IW4.Streaming.Database.Streaming;

public readonly record struct StreamFileRef(uint FileIndex, string Name, StreamFileKind Kind);
