using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Team17.Online;

namespace OC2DIYChef
{
    internal sealed class ChefGridEntry
    {
        internal ChefAvatarData Avatar;
        internal uint OfficialID;
        internal byte CustomID = 255;
        internal string Hat;
        string label;
        internal string Label { get { return label ?? (label = Avatar.HeadName.Replace("Chef_", "").Replace('_', ' ')); } }
    }

    internal static class ChefGridRoster
    {
        static readonly MethodInfo sendAvatar = AccessTools.Method("ClientMessenger:ChefAvatar");
        static readonly FieldInfo selection = AccessTools.Field(typeof(FrontendChefCustomisation), "m_chefSelection");
        static readonly FieldInfo unlockedField = AccessTools.Field(typeof(FrontendChefCustomisation), "m_unlockedAvatars");

        internal static List<ChefGridEntry> Build(ChefAvatarData[] all, ChefAvatarData[] unlocked)
        {
            var result = new List<ChefGridEntry>();
            if (!DIYChefCustomisation.Enabled)
            {
                foreach (var avatar in unlocked)
                {
                    int id = Array.IndexOf(all, avatar);
                    if (id >= 0) result.Add(new ChefGridEntry { Avatar = avatar, OfficialID = (uint)id, Hat = "" });
                }
                return result;
            }
            // Use the same filtered order and unlock policy as the existing switcher.
            foreach (var pair in DIYChefCustomisation.preferredChefs)
            {
                var custom = DIYChefCustomisation.diyChefs.Find(x => x.HeadName == pair.Key);
                if (custom != null)
                    result.Add(new ChefGridEntry { Avatar = custom, OfficialID = DIYChefCustomisation.defaultChefID,
                        CustomID = custom.id, Hat = pair.Value });
                else
                {
                    int id = Array.FindIndex(all, x => x.HeadName == pair.Key && Array.IndexOf(unlocked, x) >= 0);
                    if (id >= 0) result.Add(new ChefGridEntry { Avatar = all[id], OfficialID = (uint)id, Hat = pair.Value });
                }
            }
            return result;
        }

        internal static int Find(List<ChefGridEntry> entries, int player)
        {
            var user = ClientUserSystem.m_Users._items[player];
            byte custom = DIYChefCustomisation.userDIYChefID[player];
            return entries.FindIndex(x => custom != 255 ? x.CustomID == custom :
                x.CustomID == 255 && x.OfficialID == user.SelectedChefAvatar);
        }

        internal static bool Select(ChefGridEntry entry, int player, FrontendChefCustomisation view)
        {
            var users = ClientUserSystem.m_Users;
            if (player < 0 || player >= users.Count || player >= DIYChefCustomisation.userDIYChefID.Length ||
                !users._items[player].IsLocal || !view.IsActive()) return false;
            var all = GameUtils.GetAvatarDirectoryData().Avatars;
            if (entry.OfficialID >= all.Length) return false;
            var user = users._items[player];
            // Install custom ID first: sending the official avatar can refresh the lobby immediately.
            DIYChefCustomisation.userDIYChefID[player] = entry.CustomID;
            // Update the user only at an explicit selection, as the native action
            // does. ChefAvatar sends a message; view data is an isolated copy.
            user.SelectedChefAvatar = entry.OfficialID;
            var data = view.GetCurrentSelection();
            if (data != null) data.Character = all[entry.OfficialID];
            selection.SetValue(view, Array.IndexOf((ChefAvatarData[])unlockedField.GetValue(view), all[entry.OfficialID]));
            sendAvatar.Invoke(null, new object[] { entry.OfficialID, user });
            if (DIYChefCustomisation.Enabled && DIYChefCustomisation.localClient != null)
                DIYChefCustomisation.SendClientDIYChefMessage(entry.CustomID, user);
            if (ClientUserSystem.usersChanged != null) ClientUserSystem.usersChanged();
            return true;
        }
    }
}
