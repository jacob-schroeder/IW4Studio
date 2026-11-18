using System.ComponentModel;
using System.Diagnostics;
using IW4.Game.Assets.TechniqueSet;
using IW4.Render.Shaders;

namespace IW4.Studio.Desktop.Editors.Shader;

internal enum CgSourceCompilerKind
{
    Managed,
    Ps3Dk,
    Sdk,
    ShaderConvert
}

public sealed class ShaderCompilerChoice
{
    internal ShaderCompilerChoice(CgSourceCompilerKind kind, string name, string? path = null)
    {
        Kind = kind;
        Name = name;
        Path = path;
    }

    public string Name { get; }
    internal CgSourceCompilerKind Kind { get; }
    internal string? Path { get; }
    public override string ToString() => Name;
}

internal static class CgSourceCompiler
{
    internal static IReadOnlyList<ShaderCompilerChoice> Locate()
    {
        var choices = new List<ShaderCompilerChoice> { new(CgSourceCompilerKind.Managed, "Built-in Cg") };
        if (FindPs3DkCompiler() is { } ps3dk) choices.Add(new(CgSourceCompilerKind.Ps3Dk, "PS3DK", ps3dk));
        if (FindSdkCompiler() is { } sdk) choices.Add(new(CgSourceCompilerKind.Sdk, "sce-cgc", sdk));
        if (FindShaderConvert() is { } bridge) choices.Add(new(CgSourceCompilerKind.ShaderConvert, "ShaderConvert", bridge));
        return choices;
    }

    internal static async Task<(byte[] Program, string Messages)> CompileAsync(
        string source, MaterialShaderKind stage, ShaderCompilerChoice compiler, CancellationToken cancellation)
    {
        if (compiler.Kind == CgSourceCompilerKind.Managed)
        {
            byte[] program = await Task.Run(() => ManagedCgCompiler.Compile(source, stage), cancellation);
            return (program, string.Empty);
        }
        string executable = compiler.Path ?? throw new InvalidOperationException("The selected compiler has no executable path.");
        string directory = Directory.CreateTempSubdirectory("iw4-cg-").FullName;
        try
        {
            const string input = "source.cg";
            const string output = "program.cg";
            await File.WriteAllTextAsync(Path.Combine(directory, input), source, cancellation);
            bool vertex = stage == MaterialShaderKind.Vertex;
            string profile = vertex ? "sce_vp_rsx" : "sce_fp_rsx";
            bool wine = compiler.Kind == CgSourceCompilerKind.Sdk && !OperatingSystem.IsWindows() &&
                Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase);
            var start = new ProcessStartInfo
            {
                FileName = wine ? "wine" : executable,
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true
            };
            if (wine) start.ArgumentList.Add(executable);
            string[] arguments = compiler.Kind switch
            {
                CgSourceCompilerKind.Ps3Dk => ["-p", profile, "-e", "main", "--emit-container", output, input],
                CgSourceCompilerKind.Sdk => ["-profile", profile, "-entry", "main", "-o", output, input],
                CgSourceCompilerKind.ShaderConvert => ["--format", "cg", "--stage", vertex ? "vertex" : "pixel", "--input", input, "--output", output],
                _ => throw new InvalidOperationException("No shader compiler was selected.")
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the shader compiler.");
            Task<string> errors = process.StandardError.ReadToEndAsync();
            Task<string> messages = process.StandardOutput.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(cancellation);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }
            string log = string.Join(Environment.NewLine, new[] { await errors, await messages }
                .Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
            if (process.ExitCode != 0 || !File.Exists(Path.Combine(directory, output)))
                throw new InvalidDataException(string.IsNullOrEmpty(log) ? $"Shader compiler exited with code {process.ExitCode}." : log);
            return (await File.ReadAllBytesAsync(Path.Combine(directory, output), cancellation), log);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException($"Could not start the shader toolchain. {exception.Message}", exception);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string? FindPs3DkCompiler()
    {
        string tool = OperatingSystem.IsWindows() ? "rsx-cg-compiler.exe" : "rsx-cg-compiler";
        if (OnPath(tool) is { } installed) return installed;
        if (!OperatingSystem.IsMacOS()) return null;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) return null;
        string local = Path.Combine(home, ".local", "bin", tool);
        return File.Exists(local) ? local : null;
    }

    private static string? FindSdkCompiler()
    {
        if (Environment.GetEnvironmentVariable("SCE_PS3_ROOT") is { Length: > 0 } sdk)
        {
            string[] hosts = OperatingSystem.IsLinux()
                ? ["host-linux/Cg/bin/sce-cgc", "host-win32/Cg/bin/sce-cgc.exe"]
                : ["host-win32/Cg/bin/sce-cgc.exe"];
            foreach (string host in hosts)
            {
                string path = Path.Combine(sdk, host);
                if (File.Exists(path)) return Path.GetFullPath(path);
            }
        }
        return OnPath("sce-cgc") ?? OnPath("sce-cgc.exe");
    }

    private static string? FindShaderConvert()
    {
        string tool = OperatingSystem.IsWindows() ? "ShaderConvert.exe" : "ShaderConvert";
        string adjacent = Path.Combine(AppContext.BaseDirectory, tool);
        if (File.Exists(adjacent)) return adjacent;
        if (OnPath(tool) is { } installed) return installed;
        // A development build can use its sibling tool without installing it on PATH.
        for (var parent = new DirectoryInfo(AppContext.BaseDirectory); parent is not null; parent = parent.Parent)
        {
            string project = Path.Combine(parent.FullName, "tools", "ShaderConvert");
            if (!File.Exists(Path.Combine(project, "ShaderConvert.csproj"))) continue;
            foreach (string configuration in new[] { "Release", "Debug" })
            {
                string path = Path.Combine(project, "bin", configuration, "net10.0", tool);
                if (File.Exists(path)) return path;
            }
            break;
        }
        return null;
    }

    private static string? OnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(Path.IsPathFullyQualified).Select(dir => Path.Combine(dir, name)).FirstOrDefault(File.Exists);
}
