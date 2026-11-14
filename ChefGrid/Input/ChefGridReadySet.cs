using System.Collections.Generic;
namespace OC2DIYChef
{
    internal sealed class ChefGridReadySet
    {
        readonly HashSet<long> players = new HashSet<long>(), ready = new HashSet<long>();
        internal bool Complete { get { return players.Count > 0 && ready.Count == players.Count; } }
        internal void Sync(IList<long> keys) { players.Clear(); foreach (long key in keys) players.Add(key); ready.RemoveWhere(key => !players.Contains(key)); }
        internal bool IsConfirmed(long key) { return ready.Contains(key); }
        internal bool Confirm(long key) { return players.Contains(key) && ready.Add(key); }
        internal bool Reopen(long key) { return ready.Remove(key); }
    }
}
