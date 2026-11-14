using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using OC2DIYChef;
using UnityEngine;

static class TextureCacheWriteQueueRegression
{
    const int MiB = 1024 * 1024;

    static CachedTexture Texture(int width, int height, byte value, bool native)
    {
        byte[] pixels = Enumerable.Repeat(value, checked(width * height * 4)).ToArray();
        var texture = new CachedTexture { Width = width, Height = height,
            Format = (int)TextureFormat.RGBA32, MipCount = 1 };
        if (native)
        {
            texture.NativePixels = new NativePixelBuffer(pixels.Length);
            Marshal.Copy(pixels, 0, texture.NativePixels.Pointer, pixels.Length);
        }
        else texture.Pixels = pixels;
        return texture;
    }

    static bool Alive(CachedTexture texture)
    {
        try { return texture.NativePixels.Pointer != IntPtr.Zero; }
        catch (ObjectDisposedException) { return false; }
    }

    static void Drain(TextureCacheWriteQueue queue, Action<bool, string> check)
    {
        var timer = Stopwatch.StartNew();
        while (queue.Busy && timer.ElapsedMilliseconds < 5000) Thread.Sleep(1);
        check(!queue.Busy && queue.PendingBytes == 0, "portrait queue drains all active/queued byte ownership");
    }

    internal static void Run(string workspace, Action<bool, string> check)
    {
        string folder = Path.Combine(workspace, "portrait-queue");
        Directory.CreateDirectory(folder);
        long nativeBefore = NativePixelBuffer.AllocatedBytes;
        FiftyWrites(folder, check);
        LimitsDuplicatesAndStop(folder, check);
        ModelWriterPriority(folder, check);
        FailureAndOwnership(folder, check);
        check(NativePixelBuffer.AllocatedBytes == nativeBefore, "portrait queue tests retain no native pixel buffers");
    }

    static void FiftyWrites(string folder, Action<bool, string> check)
    {
        var queue = new TextureCacheWriteQueue(8 * MiB);
        try
        {
            for (int i = 0; i < 50; i++)
            {
                var texture = Texture(128, 128, (byte)i, i % 2 == 0);
                bool accepted = queue.TryEnqueue(Path.Combine(folder, "portrait-" + i + ".texture"), texture);
                if (!accepted) texture.Dispose();
                check(accepted, "all 50 portraits fit the bounded queue, including portrait " + i);
            }
            Drain(queue, check);
            for (int i = 0; i < 50; i++)
                using (var stored = AssetDiskCache.ReadTextureFile(Path.Combine(folder, "portrait-" + i + ".texture"), new byte[32]))
                    check(stored != null && stored.Width == 128 && stored.Height == 128 &&
                        stored.Pixels.Length == 128 * 128 * 4 && stored.Pixels.All(value => value == (byte)i),
                        "real queued portrait roundtrip preserves every pixel: " + i);
        }
        finally { queue.Stop(); }
    }

    static void LimitsDuplicatesAndStop(string folder, Action<bool, string> check)
    {
        string blocker = Path.Combine(folder, "active-stop-blocker");
        File.WriteAllText(blocker, "a file cannot contain a texture cache");
        string activePath = Path.Combine(blocker, "active.texture");
        string pendingPath = Path.Combine(folder, "discarded.texture");
        var entered = new ManualResetEvent(false);
        var release = new ManualResetEvent(false);
        var queue = new TextureCacheWriteQueue(8 * MiB);
        var oldLog = AssetDiskCache.Log;
        var active = Texture(1024, 1024, 1, true);
        var pending = Texture(1024, 1024, 2, true);
        bool ownsActive = false, ownsPending = false;
        AssetDiskCache.Log = delegate(string message)
        {
            if (message.Contains("active-stop-blocker"))
            {
                entered.Set();
                release.WaitOne(10000);
            }
            else if (oldLog != null) oldLog(message);
        };
        try
        {
            ownsActive = queue.TryEnqueue(activePath, active);
            check(ownsActive && entered.WaitOne(5000), "real failed file write holds an active queue request");
            ownsPending = queue.TryEnqueue(pendingPath, pending);
            check(ownsPending && queue.PendingBytes == 8 * MiB && queue.Busy,
                "8 MiB budget includes both 4 MiB active and 4 MiB pending pixels");
            using (var rejected = Texture(1, 1, 3, true))
            {
                check(!queue.TryEnqueue(Path.Combine(folder, "over-budget.texture"), rejected) && Alive(rejected),
                    "byte-limit rejection leaves pixel ownership with the caller");
            }
            queue.Stop();
            check(!Alive(pending) && Alive(active) && queue.PendingBytes == 4 * MiB && queue.Busy && !File.Exists(pendingPath),
                "stop disposes pending pixels immediately and preserves active write ownership");
            using (var rejected = Texture(1, 1, 4, true))
                check(!queue.TryEnqueue(Path.Combine(folder, "after-stop.texture"), rejected) && Alive(rejected),
                    "stopped queue rejects new work without taking ownership");
            release.Set();
            Drain(queue, check);
            check(!Alive(active) && !File.Exists(activePath), "active failed write finishes safely and releases its pixels after stop");
            queue.Stop();
            check(!queue.Busy && queue.PendingBytes == 0, "stopping a drained queue twice is harmless");
        }
        finally
        {
            release.Set();
            queue.Stop();
            var timer = Stopwatch.StartNew();
            while (queue.Busy && timer.ElapsedMilliseconds < 5000) Thread.Sleep(1);
            AssetDiskCache.Log = oldLog;
            if (!ownsActive) active.Dispose();
            if (!ownsPending) pending.Dispose();
            if (!queue.Busy) { entered.Close(); release.Close(); }
        }
    }

    static void ModelWriterPriority(string folder, Action<bool, string> check)
    {
        string blocker = Path.Combine(folder, "model-priority-blocker");
        File.WriteAllText(blocker, "a file cannot contain a cache");
        string modelRoot = Path.Combine(blocker, "model");
        string portraitPath = Path.Combine(blocker, "portrait.texture");
        var enteredModel = new ManualResetEvent(false);
        var enteredPortrait = new ManualResetEvent(false);
        var releaseModel = new ManualResetEvent(false);
        var queue = new TextureCacheWriteQueue(1024);
        string oldRoot = AssetDiskCache.Root;
        var oldLog = AssetDiskCache.Log;
        AssetDiskCache.Log = delegate(string message)
        {
            if (message.Contains(modelRoot))
            {
                enteredModel.Set();
                releaseModel.WaitOne(10000);
            }
            else if (message.Contains("model-priority-blocker")) enteredPortrait.Set();
            else if (oldLog != null) oldLog(message);
        };
        try
        {
            AssetDiskCache.Root = modelRoot;
            AssetDiskCache.QueueTextureWrite("model.png", new byte[32], Texture(1, 1, 1, false));
            check(enteredModel.WaitOne(5000) && AssetDiskCache.WriterBusy, "original model writer is held in a real file failure");
            var texture = Texture(1, 1, 2, true);
            bool accepted = queue.TryEnqueue(portraitPath, texture);
            if (!accepted) texture.Dispose();
            using (var duplicate = Texture(1, 1, 3, true))
            {
                check(!queue.TryEnqueue(portraitPath.ToUpperInvariant(), duplicate) && Alive(duplicate),
                    "case-insensitive active path is deduplicated even with room in the byte budget");
                string pendingPath = Path.Combine(blocker, "pending.texture");
                var pending = Texture(1, 1, 4, true);
                bool pendingAccepted = queue.TryEnqueue(pendingPath, pending);
                if (!pendingAccepted) pending.Dispose();
                check(pendingAccepted && !queue.TryEnqueue(pendingPath, duplicate) && Alive(duplicate),
                    "pending path is deduplicated without disposing rejected caller pixels");
            }
            check(accepted && !enteredPortrait.WaitOne(100) && queue.Busy,
                "portrait writer yields while the model-resource write slot is busy");
            releaseModel.Set();
            check(enteredPortrait.WaitOne(5000), "portrait write proceeds after the model-resource writer completes");
            Drain(queue, check);
            check(!Alive(texture), "priority wait still releases accepted native pixels after failure");
        }
        finally
        {
            releaseModel.Set();
            queue.Stop();
            var timer = Stopwatch.StartNew();
            while ((queue.Busy || AssetDiskCache.WriterBusy) && timer.ElapsedMilliseconds < 5000) Thread.Sleep(1);
            AssetDiskCache.Root = oldRoot;
            AssetDiskCache.Log = oldLog;
            if (!queue.Busy && !AssetDiskCache.WriterBusy)
            { enteredModel.Close(); enteredPortrait.Close(); releaseModel.Close(); }
        }
    }

    static void FailureAndOwnership(string folder, Action<bool, string> check)
    {
        var queue = new TextureCacheWriteQueue(1024);
        var oldLog = AssetDiskCache.Log;
        string blocker = Path.Combine(folder, "throwing-log-blocker");
        File.WriteAllText(blocker, "not a directory");
        try
        {
            using (var texture = Texture(1, 1, 1, true))
            {
                AssetDiskCache.Enabled = false;
                check(!queue.TryEnqueue(Path.Combine(folder, "disabled.texture"), texture) && Alive(texture),
                    "disabled cache rejects work without accepting ownership");
                AssetDiskCache.Enabled = true;
                check(!queue.TryEnqueue(null, texture) && !queue.TryEnqueue("\0invalid", texture) && Alive(texture),
                    "invalid paths cannot transfer caller pixel ownership");
            }
            using (var oversized = Texture(17, 17, 1, true))
                check(!queue.TryEnqueue(Path.Combine(folder, "oversized.texture"), oversized) && Alive(oversized),
                    "one texture larger than the queue budget is rejected without disposing it");
            AssetDiskCache.Log = delegate { throw new InvalidOperationException("logging failure"); };
            var broken = Texture(1, 1, 9, true);
            bool accepted = queue.TryEnqueue(Path.Combine(blocker, "failed.texture"), broken);
            if (!accepted) broken.Dispose();
            check(accepted, "real unwritable-file request is accepted for background handling");
            var valid = Texture(1, 1, 10, true);
            string validPath = Path.Combine(folder, "after-failure.texture");
            accepted = queue.TryEnqueue(validPath, valid);
            if (!accepted) valid.Dispose();
            check(accepted, "queue accepts another texture after an earlier I/O failure");
            Drain(queue, check);
            using (var stored = AssetDiskCache.ReadTextureFile(validPath))
                check(!Alive(broken) && !Alive(valid) && stored != null && stored.Pixels.SequenceEqual(new byte[] { 10, 10, 10, 10 }),
                    "I/O plus logging exceptions do not kill the worker or leak pixels; later real writes survive");
        }
        finally
        {
            AssetDiskCache.Enabled = true;
            AssetDiskCache.Log = oldLog;
            queue.Stop();
        }
    }
}
