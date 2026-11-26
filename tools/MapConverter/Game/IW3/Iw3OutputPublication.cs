using System.Security.Cryptography;
using System.Text;

namespace MapConverter.Game.IW3;

/// <summary>Publishes one map's files together while retaining the previous output on failure.</summary>
internal static class Iw3OutputPublication
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static void ValidateDestinations(IEnumerable<string> destinations, IEnumerable<string> inputs)
    {
        var sourcePaths = inputs.Select(CanonicalPath).ToHashSet(PathComparer);
        var seen = new HashSet<string>(PathComparer);
        foreach (string destination in destinations)
        {
            string fullPath = Path.GetFullPath(destination);
            if (!seen.Add(fullPath))
                throw new IOException($"Duplicate conversion output: '{fullPath}'.");
            if (Directory.Exists(fullPath))
                throw new IOException($"Output path is an existing directory: '{fullPath}'.");
            if (sourcePaths.Contains(CanonicalPath(fullPath)))
                throw new IOException($"Output would replace a conversion input: '{fullPath}'.");
        }
    }

    internal static void Publish(
        IReadOnlyList<(string Staged, string Destination)> outputs,
        IEnumerable<string> inputs)
    {
        if (outputs.Count == 0) return;
        string directory = Path.GetDirectoryName(outputs[0].Destination) ??
            throw new IOException("Conversion output has no directory.");
        Directory.CreateDirectory(directory);
        string mutexName = "MapConverter-IW3-" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalPath(directory).ToUpperInvariant())));
        using var mutex = new Mutex(false, mutexName);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { /* The abandoned publisher no longer owns the lock. */ }
        try
        {
            ValidateDestinations(outputs.Select(output => output.Destination), inputs);
            string token = Guid.NewGuid().ToString("N");
            var prepared = new List<(string Stage, string Destination, string? Backup)>();
            var published = new List<string>();
            bool committed = false;
            try
            {
                foreach (var (staged, destination) in outputs)
                {
                    string localStage = Path.Combine(directory, $".{Path.GetFileName(destination)}.{token}.new");
                    prepared.Add((localStage, destination, null));
                    File.Move(staged, localStage);
                }

                for (int index = 0; index < prepared.Count; index++)
                {
                    var item = prepared[index];
                    if (!File.Exists(item.Destination) && new FileInfo(item.Destination).LinkTarget is null)
                        continue;
                    string backup = Path.Combine(directory, $".{Path.GetFileName(item.Destination)}.{token}.old");
                    File.Move(item.Destination, backup);
                    prepared[index] = (item.Stage, item.Destination, backup);
                }

                foreach (var (stage, destination, _) in prepared)
                {
                    File.Move(stage, destination);
                    published.Add(destination);
                }
                committed = true;
            }
            catch (Exception error)
            {
                var rollbackErrors = new List<Exception>();
                foreach (string destination in published)
                {
                    try { File.Delete(destination); }
                    catch (Exception rollbackError) { rollbackErrors.Add(rollbackError); }
                }
                foreach (var (_, destination, backup) in prepared)
                {
                    if (backup is null) continue;
                    try { File.Move(backup, destination); }
                    catch (Exception rollbackError) { rollbackErrors.Add(rollbackError); }
                }
                if (rollbackErrors.Count != 0)
                    throw new AggregateException("Conversion publication and rollback failed.",
                        new[] { error }.Concat(rollbackErrors));
                throw;
            }
            finally
            {
                foreach (var (stage, _, backup) in prepared)
                {
                    if (File.Exists(stage)) File.Delete(stage);
                    if (committed && backup is not null && File.Exists(backup)) File.Delete(backup);
                }
            }
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static string CanonicalPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? parent = Path.GetDirectoryName(fullPath);
        if (parent is null) return fullPath;
        string child = Path.Combine(CanonicalPath(parent), Path.GetFileName(fullPath));
        FileSystemInfo info = Directory.Exists(child) ? new DirectoryInfo(child) : new FileInfo(child);
        return info.LinkTarget is null ? child : info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ??
            throw new IOException($"Unable to resolve link '{child}'.");
    }
}
