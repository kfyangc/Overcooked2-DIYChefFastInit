using System.Collections.Generic;

namespace OC2DIYChef
{
    // Use input slot + split status, like the game's avatar cache, rather than player indices.
    // Player indices can change when someone joins or leaves while the menu is open.
    internal sealed class ChefSelectionSnapshot
    {
        readonly Dictionary<long, byte> choices = new Dictionary<long, byte>();

        static long Key(int slot, int split)
        {
            return ((long)(uint)slot << 32) | (uint)split;
        }

        internal void Record(int slot, int split, byte chef)
        {
            choices[Key(slot, split)] = chef;
        }

        internal bool TryGet(int slot, int split, out byte chef)
        {
            return choices.TryGetValue(Key(slot, split), out chef);
        }
    }
}
