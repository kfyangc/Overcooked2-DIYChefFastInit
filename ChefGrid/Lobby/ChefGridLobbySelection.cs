using HarmonyLib;
using System.Reflection;
using Team17.Online;

namespace OC2DIYChef
{
    // Editing and cancellation follow the clicked user's stable local identity.
    internal sealed class ChefGridLobbySelection
    {
        static readonly FieldInfo replacer = AccessTools.Field(typeof(FrontendChefCustomisation), "m_chef");
        readonly int engagement, split;
        readonly uint official;
        readonly byte custom;

        ChefGridLobbySelection(User user, int index)
        {
            engagement = (int)user.Engagement;
            split = (int)user.Split;
            official = user.SelectedChefAvatar;
            custom = DIYChefCustomisation.userDIYChefID[index];
        }

        internal static int FindLocal(User requested)
        {
            if (requested == null || !requested.IsLocal) return -1;
            var users = ClientUserSystem.m_Users;
            for (int i = 0; i < users.Count && i < DIYChefCustomisation.userDIYChefID.Length; i++)
            {
                var user = users._items[i];
                if (user.IsLocal && user.Engagement == requested.Engagement && user.Split == requested.Split) return i;
            }
            return -1;
        }

        internal static ChefGridLobbySelection Capture(User user)
        {
            int index = FindLocal(user);
            return index < 0 ? null : new ChefGridLobbySelection(ClientUserSystem.m_Users._items[index], index);
        }

        internal bool Owns(User user)
        {
            return user != null && user.IsLocal && (int)user.Engagement == engagement && (int)user.Split == split;
        }

        internal int Find()
        {
            var users = ClientUserSystem.m_Users;
            for (int i = 0; i < users.Count && i < DIYChefCustomisation.userDIYChefID.Length; i++)
                if (Owns(users._items[i])) return i;
            return -1;
        }

        internal void Restore(FrontendChefCustomisation[] views)
        {
            int index = Find();
            if (index < 0 || index >= views.Length || views[index] == null || !views[index].IsActive()) return;
            ChefLoading.Cancel((ChefMeshReplacer)replacer.GetValue(views[index]));
            ChefGridRoster.Select(new ChefGridEntry { OfficialID = official, CustomID = custom }, index, views[index]);
        }
    }
}
