using System.Runtime.InteropServices;
using System.Text;

namespace IW4.Studio.Desktop.Views;

/// <summary>
/// Optional, explicitly configured Live Preview startup trace. Each launch
/// replaces the previous dump so one file describes one process lifetime.
/// </summary>
internal static class LivePreviewDebugDump
{
    private const string FileName = "error_dump.txt";

    private static readonly Lock Sync = new();
    private static StreamWriter? s_writer;

    internal static bool IsEnabled
    {
        get
        {
            lock (Sync)
                return s_writer is not null;
        }
    }

    internal static void Configure(bool enabled)
    {
        lock (Sync)
        {
            DisposeWriter();
            if (!enabled)
                return;

            try
            {
                string path = Path.Combine(
                    AppContext.BaseDirectory,
                    FileName);
                var stream = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite);
                s_writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = true
                };
                WriteCore(
                    $"diagnostic session started; " +
                    $"os={RuntimeInformation.OSDescription}; " +
                    $"framework={RuntimeInformation.FrameworkDescription}; " +
                    $"architecture={RuntimeInformation.ProcessArchitecture}");
            }
            catch
            {
                DisposeWriter();
            }
        }
    }

    internal static void Write(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        lock (Sync)
        {
            if (s_writer is null)
                return;

            try
            {
                WriteCore(message);
            }
            catch
            {
                DisposeWriter();
            }
        }
    }

    internal static void Shutdown()
    {
        lock (Sync)
        {
            if (s_writer is not null)
            {
                try
                {
                    WriteCore("diagnostic session ended");
                }
                catch
                {
                    // Shutdown still releases the writer after an I/O failure.
                }
            }

            DisposeWriter();
        }
    }

    private static void WriteCore(string message) =>
        s_writer!.WriteLine(
            $"{DateTimeOffset.UtcNow:O} " +
            $"[thread {Environment.CurrentManagedThreadId}] {message}");

    private static void DisposeWriter()
    {
        try
        {
            s_writer?.Dispose();
        }
        catch
        {
            // Diagnostics must never prevent startup or shutdown.
        }
        finally
        {
            s_writer = null;
        }
    }
}
