using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace OC2DIYChef
{
    internal static partial class AssetDiskCache
    {
        internal static CachedTexture ReadTexture(string source, byte[] fingerprint, bool nativePixels = false,
            Func<bool> cancelled = null)
        {
            using (var input = Open(source, fingerprint, TextureKind))
            {
                if (input == null) return null;
                try { return ReadTextureData(input, nativePixels, cancelled); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Warning(source, ex); return null; }
            }
        }

        static CachedTexture ReadTextureData(CacheReader input, bool nativePixels, Func<bool> cancelled)
        {
            byte[] metadata = input.ReadCheckedBytes(20);
            var texture = new CachedTexture { Width = BitConverter.ToInt32(metadata, 0), Height = BitConverter.ToInt32(metadata, 4),
                Format = BitConverter.ToInt32(metadata, 8), MipCount = BitConverter.ToInt32(metadata, 12), StorageSchema = input.Schema };
            int length = BitConverter.ToInt32(metadata, 16);
            if (texture.Width < 1 || texture.Height < 1 || texture.MipCount < 1 || length < 0)
                throw new FormatException("invalid texture dimensions or payload");
            int channels = texture.Format == (int)TextureFormat.RGB24 ? 3 :
                texture.Format == (int)TextureFormat.RGBA32 || texture.Format == (int)TextureFormat.ARGB32 ||
                texture.Format == (int)TextureFormat.BGRA32 ? 4 : 0;
            long expected = 0;
            int width = texture.Width, height = texture.Height, mips = 0;
            do
            {
                expected = checked(expected + checked((long)width * height * channels));
                mips++;
                if (width == 1 && height == 1) break;
                width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
            } while (texture.MipCount > 1);
            if (channels == 0 || mips != texture.MipCount || expected != length)
                throw new FormatException("invalid texture format or mip chain");
            int codec = 0, storedLength = length;
            if (input.Schema == CompressedTextureSchema)
            {
                codec = input.ReadInt32(); storedLength = input.ReadInt32();
                if ((codec != 0 && codec != 1) || storedLength < 1 || codec == 0 && storedLength != length ||
                    codec == 1 && storedLength > (long)length + length / 255 + 16)
                    throw new FormatException("invalid texture compression header");
            }
            if (input.BaseStream.Length - input.BaseStream.Position != storedLength)
                throw new FormatException("invalid stored texture length");
            texture.Compressed = codec == 1;
            try
            {
                if (codec == 1)
                {
                    var pixels = input.ReadCompressedPixels(storedLength, length, cancelled);
                    if (nativePixels) texture.NativePixels = pixels;
                    else
                    {
                        try
                        {
                            texture.Pixels = new byte[length];
                            Marshal.Copy(pixels.Pointer, texture.Pixels, 0, length);
                        }
                        finally { pixels.Dispose(); }
                    }
                }
                else if (nativePixels) texture.NativePixels = input.ReadCheckedPixels(length, cancelled);
                else texture.Pixels = input.ReadCheckedBytes(length);
                input.Verify();
                return texture;
            }
            catch { texture.Dispose(); throw; }
        }

        internal static void WriteTexture(string source, byte[] fingerprint, CachedTexture texture)
        {
            Write(source, fingerprint, TextureKind, delegate(BinaryWriter output)
            {
                output.Write(texture.Width); output.Write(texture.Height); output.Write(texture.Format);
                output.Write(texture.MipCount); output.Write(texture.PixelLength);
                if (texture.Pixels != null) output.Write(texture.Pixels);
                else texture.NativePixels.WriteTo(output);
            });
            if (BackgroundCompressionEnabled && Lz4Codec.Available) QueueTextureCompression(CachePath(source, TextureKind));
        }

        internal static void QueueTextureWrite(string source, byte[] fingerprint, CachedTexture texture)
        {
            if (!Enabled || WriterBusy) return;
            var request = new TextureWrite { Bytes = texture.PixelLength };
            writer = request;
            new Thread(delegate()
            {
                try { WriteTexture(source, fingerprint, texture); }
                finally { request.Completed = true; }
            }) { IsBackground = true, Name = "DIYChef disk cache" }.Start();
        }

    }
}
