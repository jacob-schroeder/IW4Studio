using IW4.FastFiles.Pointers;
using IW4.Linker.Plans;

namespace IW4.FastFiles.Loaders.Pointers;

/// <summary>
/// Loader-only identity for one captured serialized pointer read. It carries
/// no runtime pointer state and deliberately does not extend the format-level
/// XPointer models with Linker capture concerns.
/// </summary>
internal readonly record struct XPointerReadHandle(CaptureOccurrence Occurrence);

/// <summary>
/// A pointer read together with its optional capture identity. A handle is
/// required when a source cell has no block address and later needs binding.
/// </summary>
internal readonly record struct XPointerRead(
    XPointerReference Pointer,
    XPointerReadHandle? CaptureHandle);
