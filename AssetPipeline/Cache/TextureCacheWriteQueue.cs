using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OC2DIYChef
{
    // A successful enqueue transfers texture ownership until write/discard completes.
    // PendingBytes includes the active request, not just requests waiting in the list.
    internal sealed class TextureCacheWriteQueue
    {
        sealed class Request
        {
            internal string Path;
            internal CachedTexture Texture;
            internal int Bytes;
        }

        static readonly byte[] GeneratedSourceHash = new byte[32];
        readonly object gate = new object();
        readonly long byteLimit;
        readonly LinkedList<Request> pending = new LinkedList<Request>();
        readonly HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long pendingBytes;
        bool stopping;
        Thread worker;

        internal TextureCacheWriteQueue(long byteLimit)
        {
            if (byteLimit < 1) throw new ArgumentOutOfRangeException("byteLimit");
            this.byteLimit = byteLimit;
        }

        internal long PendingBytes { get { lock (gate) return pendingBytes; } }
        internal bool Busy { get { lock (gate) return pendingBytes != 0; } }

        internal bool TryEnqueue(string path, CachedTexture texture)
        {
            if (texture == null || string.IsNullOrEmpty(path) || !AssetDiskCache.Enabled) return false;
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (texture.Pixels != null && texture.NativePixels != null) return false;
                int bytes = texture.PixelLength;
                if (bytes < 1 || bytes > byteLimit) return false;
                var node = new LinkedListNode<Request>(new Request { Path = fullPath, Texture = texture, Bytes = bytes });
                lock (gate)
                {
                    if (stopping || bytes > byteLimit - pendingBytes || paths.Contains(fullPath)) return false;
                    bool accounted = false;
                    try
                    {
                        paths.Add(fullPath);
                        pending.AddLast(node);
                        pendingBytes += bytes;
                        accounted = true;
                        if (worker == null)
                        {
                            worker = new Thread(WritePending)
                            { IsBackground = true, Name = "DIYChef portrait cache", Priority = ThreadPriority.BelowNormal };
                            try { worker.Start(); }
                            catch { worker = null; throw; }
                        }
                        Monitor.PulseAll(gate);
                        return true;
                    }
                    catch
                    {
                        if (node.List != null) pending.Remove(node);
                        paths.Remove(fullPath);
                        if (accounted) pendingBytes -= bytes;
                        throw;
                    }
                }
            }
            catch (Exception error)
            {
                Warning(path, error);
                return false;
            }
        }

        void WritePending()
        {
            try
            {
                while (true)
                {
                    Request request;
                    lock (gate)
                    {
                        while (!stopping && pending.Count == 0) Monitor.Wait(gate);
                        if (stopping) return;
                        request = pending.First.Value;
                        pending.RemoveFirst();
                    }
                    try
                    {
                        bool write;
                        lock (gate)
                        {
                            // Model imports retain the original singleton write slot.
                            while (!stopping && AssetDiskCache.WriterBusy) Monitor.Wait(gate, 10);
                            write = !stopping;
                        }
                        if (write) AssetDiskCache.WriteTextureFile(request.Path, GeneratedSourceHash, request.Texture);
                    }
                    catch (Exception error) { Warning(request.Path, error); }
                    finally { Complete(request); }
                }
            }
            catch (Exception error) { Warning("worker", error); }
            finally
            {
                Stop();
                lock (gate) worker = null;
            }
        }

        void Complete(Request request)
        {
            try { request.Texture.Dispose(); }
            catch (Exception error) { Warning(request.Path, error); }
            finally
            {
                // The idle worker can retain its last Request across Monitor.Wait.
                // Drop managed pixels before reporting that the byte budget is free.
                request.Texture = null;
                lock (gate)
                {
                    pendingBytes -= request.Bytes;
                    paths.Remove(request.Path);
                    Monitor.PulseAll(gate);
                }
            }
        }

        internal void Stop()
        {
            lock (gate)
            {
                stopping = true;
                Monitor.PulseAll(gate);
            }
            // The worker owns its active request. Only queued requests are discarded;
            // an atomic write already in progress can finish without disposed pixels.
            while (true)
            {
                Request request;
                lock (gate)
                {
                    if (pending.Count == 0) return;
                    request = pending.First.Value;
                    pending.RemoveFirst();
                }
                Complete(request);
            }
        }

        static void Warning(string path, Exception error)
        {
            try
            {
                var log = AssetDiskCache.Log;
                if (log != null) log("Portrait cache write failed " + path + ": " + error.Message);
            }
            catch { } // A logging callback must not terminate a background thread.
        }
    }
}
