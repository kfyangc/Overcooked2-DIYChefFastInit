using System;
using System.Runtime.InteropServices;

namespace OC2DIYChef.Research
{
    // Native CPU decoder proxy only. This does not initialize Unity or exercise its PNG decoder.
    internal sealed class GdiPngDecoder : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Startup
        {
            internal uint Version;
            internal IntPtr Callback;
            internal int NoThread, NoCodecs;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct Rect { internal int X, Y, Width, Height; }
        [StructLayout(LayoutKind.Sequential)]
        struct Bits
        {
            internal uint Width, Height;
            internal int Stride, Format;
            internal IntPtr Data, Reserved;
        }

        [DllImport("gdiplus.dll", ExactSpelling = true)]
        static extern int GdiplusStartup(out IntPtr token, ref Startup input, IntPtr output);
        [DllImport("gdiplus.dll", ExactSpelling = true)]
        static extern void GdiplusShutdown(IntPtr token);
        [DllImport("gdiplus.dll", ExactSpelling = true)]
        static extern int GdipCreateBitmapFromStream(IntPtr stream, out IntPtr image);
        [DllImport("gdiplus.dll", ExactSpelling = true)]
        static extern int GdipGetImageWidth(IntPtr image, out uint width);
        [DllImport("gdiplus.dll", ExactSpelling = true)]
        static extern int GdipGetImageHeight(IntPtr image, out uint height);
        [DllImport("gdiplus.dll", ExactSpelling = true)]
        static extern int GdipBitmapLockBits(IntPtr image, ref Rect rect, uint flags, int format, ref Bits locked);
        [DllImport("gdiplus.dll", ExactSpelling = true)]
        static extern int GdipBitmapUnlockBits(IntPtr image, ref Bits locked);
        [DllImport("gdiplus.dll", ExactSpelling = true)]
        static extern int GdipDisposeImage(IntPtr image);
        [DllImport("shlwapi.dll", ExactSpelling = true)]
        static extern IntPtr SHCreateMemStream(byte[] data, uint size);

        IntPtr token;
        internal GdiPngDecoder()
        {
            var startup = new Startup { Version = 1 };
            Check(GdiplusStartup(out token, ref startup, IntPtr.Zero));
        }
        static void Check(int code)
        {
            if (code != 0) throw new InvalidOperationException("GDI+ status=" + code);
        }
        internal byte[] Decode(byte[] png, out int width, out int height)
        {
            IntPtr stream = SHCreateMemStream(png, (uint)png.Length);
            if (stream == IntPtr.Zero) throw new InvalidOperationException("SHCreateMemStream failed");
            try
            {
                IntPtr image;
                Check(GdipCreateBitmapFromStream(stream, out image));
                try
                {
                    uint w, h;
                    Check(GdipGetImageWidth(image, out w));
                    Check(GdipGetImageHeight(image, out h));
                    width = checked((int)w); height = checked((int)h);
                    var rect = new Rect { Width = width, Height = height };
                    var bits = new Bits();
                    // PixelFormat32bppARGB is straight-alpha BGRA byte order on Windows.
                    Check(GdipBitmapLockBits(image, ref rect, 1, 0x26200A, ref bits));
                    try
                    {
                        var bytes = new byte[checked(width * height * 4)];
                        for (int y = 0; y < height; y++)
                            Marshal.Copy(NativePixelBuffer.Offset(bits.Data, y * bits.Stride), bytes, y * width * 4, width * 4);
                        return bytes;
                    }
                    finally { Check(GdipBitmapUnlockBits(image, ref bits)); }
                }
                finally { Check(GdipDisposeImage(image)); }
            }
            finally { Marshal.Release(stream); }
        }
        public void Dispose()
        {
            if (token == IntPtr.Zero) return;
            GdiplusShutdown(token); token = IntPtr.Zero;
        }
    }
}
