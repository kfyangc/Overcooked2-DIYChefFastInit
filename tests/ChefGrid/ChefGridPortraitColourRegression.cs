using System;
using OC2DIYChef;

static class ChefGridPortraitColourRegression
{
    internal static int Run()
    {
        var grey = new ChefColourData { name = "No_Team" };
        var playerColours = new[] { new ChefColourData { name = "Blue" }, new ChefColourData { name = "Red" },
            new ChefColourData { name = "Green" }, new ChefColourData { name = "Yellow" } };
        var directory = GameUtils.Directory;
        int checks = 0;
        try
        {
            // Reordering/extra colours must not change the preview or multiply its
            // colour-name cache suffix when another local player opens the grid.
            for (int player = 0; player < 4; player++)
            {
                GameUtils.Directory = new AvatarDirectoryData { Colours = new[] {
                    playerColours[player], new ChefColourData { name = "No_Chef" }, grey } };
                if (ChefGridPortraitColour.Get() != grey || playerColours[player].name == "No_Team")
                    throw new Exception("Every player uses the same native grey asset without recolouring their actual selection");
                checks++;
            }
            GameUtils.Directory = new AvatarDirectoryData { Colours = playerColours };
            UnityEngine.Resources.Colours = new[] { playerColours[0], grey };
            if (ChefGridPortraitColour.Get() != grey) throw new Exception("Legacy four-colour DLC uses the loaded native No_Team asset");
            checks++;
            GameUtils.Directory = null;
            if (ChefGridPortraitColour.Get() != grey) throw new Exception("A scene transition can resolve the same loaded grey colour");
            checks++;
            UnityEngine.Resources.Colours = playerColours;
            bool rejected = false;
            try { ChefGridPortraitColour.Get(); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Missing neutral assets must not silently create a player-coloured cache");
            checks++;
        }
        finally { GameUtils.Directory = directory; UnityEngine.Resources.Colours = null; }
        return checks;
    }
}

sealed class ChefColourData { public string name; }
namespace UnityEngine
{
    static class Resources
    {
        internal static ChefColourData[] Colours;
        internal static T[] FindObjectsOfTypeAll<T>() { return Colours as T[]; }
    }
}
