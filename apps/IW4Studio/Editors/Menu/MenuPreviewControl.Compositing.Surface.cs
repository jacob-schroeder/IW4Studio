using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuPreviewControl
{
    private CpuCompositeSurface? _cpuCompositeSurface;

    private CpuCompositeSurface EnsureCpuCompositeSurface(Rect stage)
    {
        int width = Math.Max(1, (int)Math.Ceiling(stage.Width));
        int height = Math.Max(1, (int)Math.Ceiling(stage.Height));
        if (_cpuCompositeSurface is { Width: var currentWidth,
                                     Height: var currentHeight } &&
            currentWidth == width && currentHeight == height)
        {
            return _cpuCompositeSurface;
        }

        ReleaseCpuCompositeSurface();
        _cpuCompositeSurface = new CpuCompositeSurface(width, height);
        return _cpuCompositeSurface;
    }

    private void ReleaseCpuCompositeSurface()
    {
        _cpuCompositeSurface?.Dispose();
        _cpuCompositeSurface = null;
    }

    private sealed class CpuCompositeSurface : IDisposable
    {
        public CpuCompositeSurface(int width, int height)
        {
            Width = width;
            Height = height;
            int count = checked(width * height);
            PremultipliedRed = new float[count];
            PremultipliedGreen = new float[count];
            PremultipliedBlue = new float[count];
            Coverage = new float[count];
            DestinationAlpha = new float[count];
            Rgba = new byte[checked(count * 4)];
            Bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormats.Rgba8888,
                AlphaFormat.Unpremul);
        }

        public int Width { get; }

        public int Height { get; }

        public float[] PremultipliedRed { get; }

        public float[] PremultipliedGreen { get; }

        public float[] PremultipliedBlue { get; }

        public float[] Coverage { get; }

        public float[] DestinationAlpha { get; }

        public byte[] Rgba { get; }

        public WriteableBitmap Bitmap { get; }

        public void Clear()
        {
            Array.Clear(PremultipliedRed);
            Array.Clear(PremultipliedGreen);
            Array.Clear(PremultipliedBlue);
            Array.Clear(Coverage);
            Array.Fill(DestinationAlpha, 1f);
        }

        public void Upload()
        {
            for (int index = 0; index < Coverage.Length; index++)
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

            using ILockedFramebuffer framebuffer = Bitmap.Lock();
            int sourceStride = checked(Width * 4);
            if (framebuffer.RowBytes < sourceStride)
            {
                throw new InvalidDataException(
                    "The Menu CPU compositor framebuffer row is too small.");
            }
            for (int row = 0; row < Height; row++)
            {
                Marshal.Copy(
                    Rgba,
                    checked(row * sourceStride),
                    IntPtr.Add(
                        framebuffer.Address,
                        checked(row * framebuffer.RowBytes)),
                    sourceStride);
            }
        }

        public void Dispose() => Bitmap.Dispose();

        private static byte ToByte(float value) =>
            (byte)Math.Round(Math.Clamp(value, 0f, 1f) * 255f);
    }
}
