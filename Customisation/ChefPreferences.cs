using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace OC2DIYChef
{
    public static partial class DIYChefCustomisation
    {
        static ChefPreferenceFile preferenceFile;
        internal static int PreferenceRevision { get; private set; }

        static void LoadPreferredChefs()
        {
            preferenceFile = new ChefPreferenceFile(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
            preferredChefs = new KeyValuePair<string, string>[0];
            enableLobbySwitchChef = false;
            try
            {
                ChefPreferenceDocument document;
                if (preferenceFile.TryRead(Time.realtimeSinceStartup, out document))
                {
                    preferredChefs = document.Chefs;
                    enableLobbySwitchChef = document.LobbySwitch;
                }
            }
            catch (IOException error) { DIYChefPlugin.Warn("prefer.txt read will retry: " + error.Message); }
        }

        internal static void UpdatePreferences()
        {
            if (!registered || preferenceFile == null) return;
            var progress = GameUtils.GetMetaGameProgress();
            if (progress == null) return;
            var previous = preferredChefs;
            bool previousLobby = enableLobbySwitchChef;
            uint previousDefault = defaultChefID, previousInitial = initialChefID;
            byte previousDIY = initialDIYChefID;
            try
            {
                ChefPreferenceDocument document;
                if (!preferenceFile.TryRead(Time.realtimeSinceStartup, out document)) return;
                preferredChefs = document.Chefs;
                enableLobbySwitchChef = document.LobbySwitch;
                var hats = LoadAllDIYHat(true);
                while (hats.MoveNext()) { }
                var chefs = LoadAllDIYChef(progress, true);
                while (chefs.MoveNext()) { }
                ConfigureChefDefaults(progress, document.Chefs.Length != 0);
                PreferenceRevision++;
                DIYChefPlugin.Log("Reloaded prefer.txt entries=" + preferredChefs.Length + " (existing chefs and resource caches preserved)");
            }
            catch (Exception error)
            {
                preferredChefs = previous; enableLobbySwitchChef = previousLobby;
                defaultChefID = previousDefault; initialChefID = previousInitial; initialDIYChefID = previousDIY;
                preferenceFile.RetryNextCheck();
                DIYChefPlugin.Warn("prefer.txt refresh kept the previous configuration: " + error.Message);
            }
        }
    }
}
