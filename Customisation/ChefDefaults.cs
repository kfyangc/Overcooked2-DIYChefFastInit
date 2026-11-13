using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Team17.Online;

namespace OC2DIYChef
{
    public static partial class DIYChefCustomisation
    {
        static uint initialChefID = 127U;
        static byte initialDIYChefID = 255;
        static readonly MethodInfo selectChefAvatar = AccessTools.Method("ClientMessenger:ChefAvatar");

        // Resolve after registration, when invalid custom resources have been removed
        // and resource folder names have been converted to their actual HeadName.
        internal static void ConfigureChefDefaults(MetaGameProgress progress, bool hasPreferences)
        {
            defaultChefID = initialChefID = 127U;
            initialDIYChefID = 255;
            if (!hasPreferences) return;

            var all = progress.AvatarDirectory.Avatars;
            var unlocked = progress.GetUnlockedAvatars();
            foreach (var pair in preferredChefs)
            {
                int id = Array.FindIndex(all, x => x.HeadName == pair.Key && unlocked.Contains(x));
                if (id < 0) continue;
                defaultChefID = (uint)id;
                break;
            }

            if (defaultChefID == 127U)
            {
                int snowman = Array.FindIndex(all, x => x.HeadName == "Chef_Snowman");
                if (snowman < 0) throw new InvalidOperationException("Game avatar directory has no Chef_Snowman.");
                defaultChefID = (uint)snowman;
                ShowWarningDialog(dialogText["DefaultInvalid"]);
            }

            initialChefID = defaultChefID;
            foreach (var pair in preferredChefs)
            {
                var custom = diyChefs.Find(x => x.HeadName == pair.Key);
                if (custom != null)
                {
                    initialDIYChefID = custom.id;
                    return;
                }
                int id = Array.FindIndex(all, x => x.HeadName == pair.Key && unlocked.Contains(x));
                if (id < 0) continue;
                initialChefID = (uint)id;
                return;
            }

            // A configured list with no selectable entries still needs one entry for
            // SwitchChef. A valid custom-only list keeps its original order and size.
            preferredChefs = preferredChefs.Concat(new[] {
                new KeyValuePair<string, string>(all[defaultChefID].HeadName, "")
            }).ToArray();
        }

        internal static void ApplyDefaultChefSelection()
        {
            if (!Enabled) return;
            var users = ClientUserSystem.m_Users;
            for (int i = 0; i < users.Count; i++)
            {
                var user = users._items[i];
                if (!user.IsLocal || user.SelectedChefAvatar != 127U) continue;
                // Install the custom choice before the ordinary avatar message can
                // trigger a lobby refresh. Both IDs use the existing network paths.
                userDIYChefID[i] = initialDIYChefID;
                selectChefAvatar.Invoke(null, new object[] { initialChefID, user });
                SendClientDIYChefMessage(initialDIYChefID, user);
            }
        }
    }
}
