using System.Diagnostics;

namespace IW4.Radiant.Desktop.MapCommands;

internal static class MapCommandProcess
{
    internal const string CommandLineSwitch = "--d3dbsp-linker";

    internal static ProcessStartInfo CreateStartInfo()
    {
        string host = Environment.ProcessPath ??
            throw new InvalidOperationException("Cannot locate Iw4Radiant to run a map command.");
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
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, $"{typeof(MapCommandProcess).Assembly.GetName().Name}.dll"));
        start.ArgumentList.Add(CommandLineSwitch);
        return start;
    }
}
