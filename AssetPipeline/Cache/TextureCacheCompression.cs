using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OC2DIYChef
{
    internal static partial class AssetDiskCache
    {
        const int CompressedTextureSchema = 2;
        static readonly object publishGate = new object();
        static readonly object compressionGate = new object();
        static readonly Queue<string> compressionQueue = new Queue<string>();
        static readonly HashSet<string> compressionQueued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static Thread compressionThread;
        static volatile bool compressionStopping;
        internal static bool BackgroundCompressionEnabled = true;
        internal static bool CompressionBusy { get { lock (compressionGate) return compressionThread != null; } }

        internal static void StartTextureCompression()
        {
            if (!Enabled || !BackgroundCompressionEnabled || !Lz4Codec.Available) return;
            lock (compressionGate) compressionStopping = false;
            try
            {
                if (Directory.Exists(Root))
                    foreach (string path in Directory.GetFiles(Root, "*.texture")) QueueTextureCompression(path);
            }
            catch (Exception error) { CompressionLog("Texture cache scan failed: " + error.Message); }
        }

        static void QueueTextureCompression(string path)
        {
            if (!Enabled || !BackgroundCompressionEnabled || !Lz4Codec.Available) return;
            lock (compressionGate)
            {
                if (compressionStopping || !compressionQueued.Add(path)) return;
                compressionQueue.Enqueue(path);
                if (compressionThread != null) return;
                compressionThread = new Thread(CompressQueuedTextures)
                { IsBackground = true, Name = "DIYChef LZ4HC cache", Priority = ThreadPriority.BelowNormal };
                compressionThread.Start();
            }
        }

        static void CompressQueuedTextures()
        {
            while (true)
            {
                string path;
                lock (compressionGate)
                {
                    if (compressionStopping || compressionQueue.Count == 0)
                    { compressionThread = null; return; }
                    path = compressionQueue.Dequeue(); compressionQueued.Remove(path);
                }
                // A new PNG cache write gets priority. Compression uses a separate,
                // bounded native workspace and never makes the main thread wait.
                while (WriterBusy && !compressionStopping) Thread.Sleep(1);
                if (!compressionStopping) CompressTextureCache(path, delegate { return compressionStopping; });
            }
        }

        internal static void StopTextureCompression()
        {
            lock (compressionGate)
            {
                compressionStopping = true;
                compressionQueue.Clear(); compressionQueued.Clear();
            }
        }

        static void CompressionLog(string message)
        {
            // A finishing background job must not throw through plugin shutdown.
            try { if (Log != null) Log(message); } catch { }
        }

        // Schema 2 retains schema 1's SHA-256 of metadata + *decoded* pixels.
        // Codec and stored length are validated separately; codec 0 records an
        // incompressible texture so startup scans will not recompress it forever.
        internal static bool CompressTextureCache(string path, Func<bool> cancelled = null)
        {
            if (!Enabled || !Lz4Codec.Available) return false;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] sourceHash, checksum;
                int rawLength, codec, storedLength;
                using (AssetHash.BeginBatch())
                {
                    using (var input = OpenPath(path, null, TextureKind))
                    {
                        if (input == null || input.Schema != Schema) return false;
                        sourceHash = input.SourceHash; checksum = input.Expected;
                        using (var texture = ReadTextureData(input, true, cancelled))
                        {
                            rawLength = texture.PixelLength;
                            int capacity = Lz4Codec.CompressBound(rawLength);
                            if (capacity < 1) throw new FormatException("Texture too large for LZ4");
                            using (var encoded = new NativePixelBuffer(capacity))
                            {
                                CheckCompressionCancelled(cancelled);
                                storedLength = Lz4Codec.Compress(texture.NativePixels.Pointer, rawLength, encoded.Pointer, capacity);
                                CheckCompressionCancelled(cancelled);
                                codec = storedLength < rawLength ? 1 : 0;
                                if (codec == 0) storedLength = rawLength;
                                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                using (var output = new BinaryWriter(file))
                                {
                                    output.Write(Magic); output.Write(CompressedTextureSchema); output.Write(TextureKind);
                                    output.Write(sourceHash); output.Write(checksum);
                                    output.Write(texture.Width); output.Write(texture.Height); output.Write(texture.Format);
                                    output.Write(texture.MipCount); output.Write(rawLength);
                                    output.Write(codec); output.Write(storedLength);
                                    if (codec == 1) encoded.WriteTo(output, storedLength);
                                    else texture.NativePixels.WriteTo(output);
                                }
                            }
                        }
                    }
                    // Release the original and encoding workspace before validating
                    // the replacement. No Unity object or large managed array is used.
                    CheckCompressionCancelled(cancelled);
                    using (var input = OpenPath(temporary, sourceHash, TextureKind))
                    {
                        if (input == null) throw new FormatException("Compressed cache header");
                        using (var verified = ReadTextureData(input, true, cancelled))
                            if (verified.PixelLength != rawLength) throw new FormatException("Compressed cache size");
                    }
                    lock (publishGate)
                    {
                        CheckCompressionCancelled(cancelled);
                        // A fresh source/PNG write may have replaced this entry while
                        // HC was running. Never overwrite that newer cache version.
                        using (var current = OpenPath(path, sourceHash, TextureKind))
                            if (current == null || current.Schema != Schema || !Equal(current.Expected, checksum)) return false;
                        File.Replace(temporary, path, null);
                    }
                }
                CompressionLog("Compressed texture cache " + Path.GetFileName(path) + " codec=" +
                    (codec == 1 ? "LZ4HC" : "raw") + " pixelBytes=" + rawLength + " storedBytes=" + storedLength);
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception error) { CompressionLog("Texture compression kept existing cache " + path + ": " + error.Message); return false; }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception error) { CompressionLog("Texture compression temporary cleanup: " + error.Message); }
            }
        }

        static void CheckCompressionCancelled(Func<bool> cancelled)
        { if (cancelled != null && cancelled()) throw new OperationCanceledException(); }

        internal static bool ValidateTextureCache(string path)
        {
            using (AssetHash.BeginBatch())
            using (var input = OpenPath(path, null, TextureKind))
            {
                if (input == null) return false;
                using (var texture = ReadTextureData(input, true, null)) return texture.PixelLength > 0;
            }
        }
    }
}
