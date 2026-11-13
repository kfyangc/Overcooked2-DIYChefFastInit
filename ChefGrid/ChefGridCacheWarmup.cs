using System;
using System.Collections.Generic;

namespace OC2DIYChef
{
    // Batch cache hits independently of model preparation; avoid repeating missing-file IO
    // while a cold portrait is being generated. The caller supplies its frame time budget.
    internal sealed class ChefGridCacheWarmup<T>
    {
        readonly HashSet<T> missing = new HashSet<T>();
        internal bool Missing(T entry) { return missing.Contains(entry); }

        internal bool TryLoad(T entry, Func<T, bool> load)
        {
            if (missing.Contains(entry)) return false;
            if (load(entry)) return true;
            missing.Add(entry);
            return false;
        }

        internal void Warm(IList<T> entries, Func<T, bool> ready, Func<T, bool> load, Func<bool> timeRemaining)
        {
            foreach (var entry in entries)
            {
                if (ready(entry) || missing.Contains(entry)) continue;
                TryLoad(entry, load);
                if (!timeRemaining()) break;
            }
        }

        internal void Available(T entry) { missing.Remove(entry); }
    }
}
