using System.Diagnostics;

namespace Iw4Radiant.Compilation;

internal static class BundledLinker
{
    internal const string CommandLineSwitch = "--d3dbsp-linker";

    internal static ProcessStartInfo CreateStartInfo()
    {
        string host = Environment.ProcessPath ??
            throw new InvalidOperationException("Cannot locate Iw4Radiant to start the bundled linker.");
        var start = new ProcessStartInfo
        {
            FileName = host,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        // Framework-dependent launches use dotnet; packaged apps reuse their own apphost/runtime.
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(BundledLinker).Assembly.Location);
        start.ArgumentList.Add(CommandLineSwitch);
        return start;
    }
}
