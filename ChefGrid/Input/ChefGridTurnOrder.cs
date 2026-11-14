using System.Collections.Generic;

namespace OC2DIYChef
{
    // Local participants are supplied in player order; readiness follows their stable input identities.
    internal sealed class ChefGridTurnOrder
    {
        readonly List<long> players = new List<long>();
        readonly HashSet<long> confirmed = new HashSet<long>();
        internal long? Current { get; private set; }
        internal bool Complete { get { return players.Count != 0 && !Current.HasValue; } }

        internal void Sync(IList<long> orderedPlayers)
        {
            players.Clear();
            foreach (long player in orderedPlayers)
                if (!players.Contains(player)) players.Add(player);
            confirmed.RemoveWhere(key => !players.Contains(key));
            Advance();
        }

        internal bool IsConfirmed(long player) { return confirmed.Contains(player); }

        internal bool Confirm(long player)
        {
            if (Current != player) return false;
            confirmed.Add(player);
            Advance();
            return true;
        }

        internal bool Reopen(long player)
        {
            int index = players.IndexOf(player);
            if (index < 0 || !confirmed.Contains(player)) return false;
            // Editing an earlier player restarts the remaining turns in the same order.
            for (int i = index; i < players.Count; i++) confirmed.Remove(players[i]);
            Advance();
            return true;
        }

        void Advance()
        {
            Current = null;
            foreach (long player in players)
                if (!confirmed.Contains(player)) { Current = player; break; }
        }
    }
}
