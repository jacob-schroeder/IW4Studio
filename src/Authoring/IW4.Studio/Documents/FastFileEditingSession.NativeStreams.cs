using IW4.Game.Assets.Sound;
using IW4.Game.Database.Streaming;

namespace IW4.Studio.Documents;

public sealed partial class FastFileEditingSession
{
    private NativeStreamPackageBase? _nativeStreamPackageBase;

    /// <summary>Returns a private copy of the current named image and sound package bytes.</summary>
    internal NativeStreamPackageBase GetNativeStreamPackageBase()
    {
        lock (_gate)
        {
            ThrowIfDisposedCore();
            _nativeStreamPackageBase ??= ReadWorkspaceNativeStreamPackages();
            return _nativeStreamPackageBase.Copy();
        }
    }

    /// <summary>Publishes rebased named package bytes for an already-published semantic revision.</summary>
    private void SetNativeStreamPackageBase(NativeStreamPackageBuildResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            ThrowIfDisposedCore();
            _nativeStreamPackageBase ??= ReadWorkspaceNativeStreamPackages();
            _nativeStreamPackageBase = new NativeStreamPackageBase(
                result.ImagePackage ?? _nativeStreamPackageBase.ImagePackage,
                result.SoundPackage ?? _nativeStreamPackageBase.SoundPackage);
        }
    }

    private NativeStreamPackageBase ReadWorkspaceNativeStreamPackages()
    {
        if (Workspace.IsBlank || string.IsNullOrWhiteSpace(Workspace.SourcePath))
            return new NativeStreamPackageBase(null, null);

        string sourcePath = Path.GetFullPath(Workspace.SourcePath);
        string directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        string parent = Directory.GetParent(directory)?.FullName ?? directory;
        string imageName = DbHeaderImageStreamEntry.GetPackageFileName(
            DbHeaderImageStreamEntry.NamedFileIndex,
            sourcePath);
        string soundName = StreamedSound.GetPackageFileName(
            StreamedSound.NamedFileIndex,
            sourcePath);
        return new NativeStreamPackageBase(
            ReadPackage(directory, parent, imageName),
            ReadPackage(directory, parent, soundName));
    }

    private static byte[]? ReadPackage(string directory, string parent, string fileName)
    {
        string adjacentPath = Path.Combine(directory, fileName);
        string parentPath = Path.Combine(parent, fileName);
        string? path = File.Exists(adjacentPath)
            ? adjacentPath
            : File.Exists(parentPath) ? parentPath : null;
        return path is null ? null : File.ReadAllBytes(path);
    }
}
