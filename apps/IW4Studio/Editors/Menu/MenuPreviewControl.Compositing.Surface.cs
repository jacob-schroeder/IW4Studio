using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuPreviewControl
{
    private WriteableBitmap? _cpuCompositeBitmap;

    private void UploadCpuComposite(CpuCompositeRaster raster)
    {
        var size = new PixelSize(raster.Width, raster.Height);
        if (_cpuCompositeBitmap is null || _cpuCompositeBitmap.PixelSize != size)
        {
            _cpuCompositeBitmap?.Dispose();
            _cpuCompositeBitmap = new WriteableBitmap(
                size, new Vector(96, 96), PixelFormats.Rgba8888, AlphaFormat.Unpremul);
        }

        using ILockedFramebuffer framebuffer = _cpuCompositeBitmap.Lock();
        int sourceStride = checked(raster.Width * 4);
        if (framebuffer.RowBytes == sourceStride)
        {
            Marshal.Copy(raster.Rgba, 0, framebuffer.Address, raster.Rgba.Length);
            return;
        }
        if (framebuffer.RowBytes < sourceStride)
            throw new InvalidDataException("The Menu CPU compositor framebuffer row is too small.");

        for (int row = 0; row < raster.Height; row++)
        {
            Marshal.Copy(raster.Rgba, checked(row * sourceStride),
                IntPtr.Add(framebuffer.Address, checked(row * framebuffer.RowBytes)), sourceStride);
        }
    }

    private void ReleaseCpuCompositeBitmap()
    {
        _cpuCompositeBitmap?.Dispose();
        _cpuCompositeBitmap = null;
        _displayedCpuComposite = null;
    }

    private sealed class CpuCompositeRaster
    {
        public CpuCompositeRaster(int width, int height)
        {
            Width = width;
            Height = height;
            int count = checked(width * height);
            PremultipliedRed = new float[count];
            PremultipliedGreen = new float[count];
            PremultipliedBlue = new float[count];
            Coverage = new float[count];
            DestinationAlpha = new float[count];
            Columns = new CpuTextureAxisSample[width];
            Rows = new CpuTextureAxisSample[height];
            Rgba = new byte[checked(count * 4)];
        }

        public int Width { get; }

        public int Height { get; }

        public float[] PremultipliedRed { get; }

        public float[] PremultipliedGreen { get; }

        public float[] PremultipliedBlue { get; }

        public float[] Coverage { get; }

        public float[] DestinationAlpha { get; }

        public CpuTextureAxisSample[] Columns { get; }

        public CpuTextureAxisSample[] Rows { get; }

        public byte[] Rgba { get; }

        public void Clear()
        {
            Array.Clear(PremultipliedRed);
            Array.Clear(PremultipliedGreen);
            Array.Clear(PremultipliedBlue);
            Array.Clear(Coverage);
            Array.Fill(DestinationAlpha, 1f);
        }

        public bool ConvertToRgba(CancellationToken cancellation)
        {
            for (int row = 0; row < Height; row++)
            {
                if (cancellation.IsCancellationRequested)
                    return false;
                int first = row * Width;
                int last = first + Width;
                for (int index = first; index < last; index++)
                {
                    float coverage = Coverage[index];
                    int offset = index * 4;
                    if (coverage <= 0)
                    {
                        Rgba[offset] = 0;
                        Rgba[offset + 1] = 0;
                        Rgba[offset + 2] = 0;
                        Rgba[offset + 3] = 0;
                        continue;
                    }

                    Rgba[offset] = ToByte(PremultipliedRed[index] / coverage);
                    Rgba[offset + 1] = ToByte(PremultipliedGreen[index] / coverage);
                    Rgba[offset + 2] = ToByte(PremultipliedBlue[index] / coverage);
                    Rgba[offset + 3] = ToByte(coverage);
                }
            }
            return true;
        }

        private static byte ToByte(float value) =>
            (byte)Math.Round(Math.Clamp(value, 0f, 1f) * 255f);
    }

    private readonly record struct CpuTextureAxisSample(int FirstOffset, int SecondOffset, float Ratio);
}
