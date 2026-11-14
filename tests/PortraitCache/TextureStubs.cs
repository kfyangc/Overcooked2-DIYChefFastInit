using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

// These replace Unity object APIs only. The production file container, SHA-256,
// native pixel buffers, queue and LZ4 implementation are linked unchanged.
namespace UnityEngine
{
    public enum TextureFormat { RGB24 = 3, RGBA32 = 4, ARGB32 = 5, BGRA32 = 14 }

    public class Object
    {
        internal bool Destroyed;
        public static void Destroy(Object value) { if (value != null) value.Destroyed = true; }
    }

    public sealed class Texture2D : Object
    {
        internal const int FixtureMagic = 0x46524750;
        internal static int PngLoads, RawLoads, Applies;
        internal static bool FailNextRawLoad;
        internal static readonly List<Texture2D> Created = new List<Texture2D>();
        internal byte[] Pixels, UploadedPixels;
        internal bool Readable = true;
        public int width { get; private set; }
        public int height { get; private set; }
        public TextureFormat format { get; private set; }
        public int mipmapCount { get; private set; }

        public Texture2D(int width, int height, TextureFormat format, bool mipmaps)
        {
            this.width = width; this.height = height; this.format = format;
            mipmapCount = mipmaps ? MipCount(width, height) : 1;
            Pixels = new byte[PixelLength(width, height, format, mipmaps)];
            Created.Add(this);
        }

        internal static int Channels(TextureFormat format) { return format == TextureFormat.RGB24 ? 3 : 4; }
        static int MipCount(int width, int height)
        {
            int count = 1;
            while (width > 1 || height > 1) { width = Math.Max(1, width / 2); height = Math.Max(1, height / 2); count++; }
            return count;
        }
        static int PixelLength(int width, int height, TextureFormat format, bool mipmaps)
        {
            int length = width * height * Channels(format);
            if (mipmaps)
                while (width > 1 || height > 1)
                {
                    width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
                    length += width * height * Channels(format);
                }
            return length;
        }

        // A deterministic decoded-image fixture, deliberately not a PNG codec.
        // Actual PNG decoding costs are covered by the separate native probe.
        public bool LoadImage(byte[] bytes)
        {
            PngLoads++;
            using (var input = new BinaryReader(new MemoryStream(bytes, false)))
            {
                if (input.ReadInt32() != FixtureMagic) return false;
                width = input.ReadInt32(); height = input.ReadInt32(); format = (TextureFormat)input.ReadInt32();
                mipmapCount = 1;
                int length = input.ReadInt32();
                Pixels = input.ReadBytes(length);
                if (Pixels.Length != length || length != PixelLength(width, height, format, false)) return false;
                UploadedPixels = (byte[])Pixels.Clone(); // LoadImage uploads automatically.
                Readable = true;
                return true;
            }
        }

        public void LoadRawTextureData(IntPtr pointer, int length)
        {
            RawLoads++;
            if (FailNextRawLoad) { FailNextRawLoad = false; throw new InvalidOperationException("simulated texture upload failure"); }
            if (length != PixelLength(width, height, format, mipmapCount > 1)) throw new ArgumentException("texture layout mismatch");
            Pixels = new byte[length];
            Marshal.Copy(pointer, Pixels, 0, length);
        }

        public byte[] GetRawTextureData()
        {
            if (!Readable) throw new InvalidOperationException("texture is not readable");
            return (byte[])Pixels.Clone();
        }

        public void Apply(bool updateMipmaps, bool makeNoLongerReadable)
        {
            Applies++;
            UploadedPixels = (byte[])Pixels.Clone();
            if (makeNoLongerReadable) { Readable = false; Pixels = null; }
        }
    }
}

namespace OC2DIYChef
{
    internal static class DIYChefPlugin
    {
        internal static readonly List<string> Warnings = new List<string>();
        internal static void Warn(string message) { Warnings.Add(message); }
    }
}
