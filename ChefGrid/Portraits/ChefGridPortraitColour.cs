using System;
using UnityEngine;

namespace OC2DIYChef
{
    internal static class ChefGridPortraitColour
    {
        internal static ChefColourData Get()
        {
            var directory = GameUtils.GetAvatarDirectoryData();
            var colour = Find(directory == null ? null : directory.Colours);
            // Some DLC directories list only four player colours. Their UI still
            // references the same native No_Team asset used by versus lobbies.
            if (colour == null) colour = Find(Resources.FindObjectsOfTypeAll<ChefColourData>());
            if (colour == null) throw new InvalidOperationException("Native No_Team chef colour is not loaded");
            return colour;
        }

        static ChefColourData Find(ChefColourData[] colours)
        {
            if (colours != null)
                foreach (var colour in colours)
                    if (colour != null && string.Equals(colour.name, "No_Team", StringComparison.OrdinalIgnoreCase))
                        return colour;
            return null;
        }
    }
}
