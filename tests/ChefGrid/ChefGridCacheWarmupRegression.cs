using System;
using System.Collections.Generic;
using OC2DIYChef;

static class ChefGridCacheWarmupRegression
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    internal static int Run()
    {
        var cache = new ChefGridCacheWarmup<int>();
        var ready = new HashSet<int> { 0, 1 };
        var files = new HashSet<int> { 2, 3, 5 };
        var calls = new List<int>();
        Func<int, bool> load = entry => {
            calls.Add(entry);
            if (!files.Contains(entry)) return false;
            ready.Add(entry);
            return true;
        };
        cache.Warm(new[] { 0, 1, 2, 3, 4, 5 }, ready.Contains, load, () => true);
        Check(calls.Count == 4 && ready.Contains(2) && ready.Contains(3) && ready.Contains(5),
            "multiple cached thumbnails are restored in one pass without waiting for a model");
        Check(cache.Missing(4) && !cache.Missing(3) && !cache.Missing(6),
            "only proven file misses may enter the render queue");
        cache.Warm(new[] { 0, 1, 2, 3, 4, 5 }, ready.Contains, load, () => true);
        Check(calls.Count == 4, "resident images and known misses do not repeat disk IO each frame");
        cache.Available(4); // A generated image can later be evicted from memory.
        files.Add(4);
        cache.Warm(new[] { 4 }, ready.Contains, load, () => true);
        Check(calls.Count == 5 && ready.Contains(4) && !cache.Missing(4),
            "generated/evicted thumbnails can be restored from disk on a later page visit");

        var sliced = new ChefGridCacheWarmup<int>();
        var loaded = new HashSet<int>();
        int reads = 0;
        Func<int, bool> read = entry => { reads++; loaded.Add(entry); return true; };
        sliced.Warm(new[] { 0, 1, 2, 3, 4 }, loaded.Contains, read, () => reads < 2);
        Check(reads == 2 && loaded.Count == 2 && !sliced.Missing(2),
            "the frame budget ends a batch without incorrectly marking unprobed entries missing");
        reads = 0;
        sliced.Warm(new[] { 0, 1, 2, 3, 4 }, loaded.Contains, read, () => reads < 2);
        Check(reads == 2 && loaded.Count == 4, "the next frame resumes after the already restored images");
        reads = 0;
        sliced.Warm(new[] { 0, 1, 2, 3, 4 }, loaded.Contains, read, () => false);
        Check(reads == 1 && loaded.Count == 5, "a tight budget still makes bounded forward progress");

        var detail = new ChefGridCacheWarmup<int>();
        int attempts = 0;
        Func<int, bool> absent = entry => { attempts++; return false; };
        Check(!detail.TryLoad(9, absent) && !detail.TryLoad(9, absent) && attempts == 1,
            "an uncached detail portrait is checked once while its model is being prepared");
        detail.Available(9);
        Check(detail.TryLoad(9, entry => { attempts++; return true; }) && attempts == 2,
            "a completed detail portrait can be loaded again after changing selection");
        return checks;
    }
}
