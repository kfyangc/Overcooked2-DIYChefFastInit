using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
    // Keep the v8 image key/composition. The storage extension alone changes;
    // an old PNG or an author's preview is migrated after its first decode.
    internal static class ChefGridPortraitCache
    {
        static readonly TextureCacheWriteQueue writer = new TextureCacheWriteQueue(8 * 1024 * 1024);
        internal static bool WriterBusy { get { return writer.Busy; } }
        internal static long PendingWriteBytes { get { return writer.PendingBytes; } }

        internal static Texture2D Load(string legacyPath, string supplied, int size, Func<Texture2D, Texture2D> downsample)
        {
            var binary = LoadBinary(Path.ChangeExtension(legacyPath, ".texture"), size);
            if (binary != null) return binary;
            // An author's preview takes precedence over a previously rendered image.
            var png = supplied == null ? null : LoadPng(supplied, size, downsample);
            if (png == null) png = LoadPng(legacyPath, size, downsample);
            if (png == null) return null;
            try
            {
                Save(legacyPath, png);
                png.Apply(false, true);
                return png;
            }
            catch { Object.Destroy(png); throw; }
        }

        static Texture2D LoadBinary(string path, int size)
        {
            Texture2D texture = null;
            try
            {
                using (var data = AssetDiskCache.ReadTextureFile(path, null, true, null, size))
                {
                    if (data == null || data.Width != size || data.Height != size || data.MipCount != 1) return null;
                    texture = new Texture2D(data.Width, data.Height, (TextureFormat)data.Format, false);
                    texture.LoadRawTextureData(data.NativePixels.Pointer, data.PixelLength);
                    texture.Apply(false, true);
                    return texture;
                }
            }
            catch (Exception error)
            {
                if (texture != null) Object.Destroy(texture);
                DIYChefPlugin.Warn("Portrait pixels read: " + error.Message);
                return null;
            }
        }

        static Texture2D LoadPng(string path, int size, Func<Texture2D, Texture2D> downsample)
        {
            Texture2D texture = null;
            try
            {
                if (!File.Exists(path)) return null;
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(File.ReadAllBytes(path)) || texture.width < size || texture.height < size ||
                    texture.width > 512 || texture.height > 512)
                {
                    Object.Destroy(texture);
                    return null;
                }
                if (size == 128 && (texture.width != size || texture.height != size))
                {
                    var original = texture;
                    texture = null;
                    try { texture = downsample(original); }
                    finally { Object.Destroy(original); }
                }
                return texture;
            }
            catch (Exception error)
            {
                if (texture != null) Object.Destroy(texture);
                DIYChefPlugin.Warn("Portrait PNG read: " + error.Message);
                return null;
            }
        }

        internal static void Save(string legacyPath, Texture2D texture)
        {
            if (!AssetDiskCache.Enabled) return;
            try
            {
                var data = new CachedTexture { Width = texture.width, Height = texture.height,
                    Format = (int)texture.format, MipCount = texture.mipmapCount, Pixels = texture.GetRawTextureData() };
                if (!writer.TryEnqueue(Path.ChangeExtension(legacyPath, ".texture"), data)) data.Dispose();
            }
            catch (Exception error) { DIYChefPlugin.Warn("Portrait cache write: " + error.Message); }
        }

        internal static void Shutdown() { writer.Stop(); }
    }
}
