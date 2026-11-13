using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using Team17.Online;

namespace OC2DIYChef
{
    internal static class ChefSelectionMenu
    {
        static readonly Dictionary<ChefCustomiser, ChefSelectionSnapshot> snapshots =
            new Dictionary<ChefCustomiser, ChefSelectionSnapshot>();
        static readonly FieldInfo customiserField = AccessTools.Field(typeof(FrontendChefMenu), "m_customiser");
        static readonly FieldInfo chefsField = AccessTools.Field(typeof(ChefCustomiser), "m_chefCustomisations");
        static readonly FieldInfo replacerField = AccessTools.Field(typeof(FrontendChefCustomisation), "m_chef");

        internal static void Capture(ChefCustomiser customiser)
        {
            if (!DIYChefCustomisation.Enabled) return;
            var snapshot = new ChefSelectionSnapshot();
            var users = ClientUserSystem.m_Users;
            for (int i = 0; i < users.Count && i < DIYChefCustomisation.userDIYChefID.Length; i++)
            {
                var user = users._items[i];
                if (user.IsLocal)
                    snapshot.Record((int)user.Engagement, (int)user.Split, DIYChefCustomisation.userDIYChefID[i]);
            }
            snapshots[customiser] = snapshot;
        }

        internal static bool RestoreBeforeAvatars(ChefCustomiser customiser)
        {
            ChefSelectionSnapshot snapshot;
            if (!snapshots.TryGetValue(customiser, out snapshot)) return false;
            var users = ClientUserSystem.m_Users;
            for (int i = 0; i < users.Count && i < DIYChefCustomisation.userDIYChefID.Length; i++)
            {
                var user = users._items[i];
                byte id;
                if (user.IsLocal && snapshot.TryGet((int)user.Engagement, (int)user.Split, out id))
                    DIYChefCustomisation.userDIYChefID[i] = id;
            }
            // Do this before the original avatar messages can trigger model refreshes.
            // A pending preview must not overwrite the restored choice on its completion.
            var chefs = (FrontendChefCustomisation[])chefsField.GetValue(customiser);
            if (chefs != null)
                for (int i = 0; i < chefs.Length && i < users.Count; i++)
                    if (chefs[i] != null && users._items[i].IsLocal)
                        ChefLoading.Cancel((ChefMeshReplacer)replacerField.GetValue(chefs[i]));
            return true;
        }

        internal static void SynchronizeRestoredChoices()
        {
            var users = ClientUserSystem.m_Users;
            if (DIYChefCustomisation.localClient != null)
                for (int i = 0; i < users.Count && i < DIYChefCustomisation.userDIYChefID.Length; i++)
                    if (users._items[i].IsLocal)
                        DIYChefCustomisation.SendClientDIYChefMessage(DIYChefCustomisation.userDIYChefID[i], users._items[i]);
            // Re-run the normal display path, which replaces stale requests in other views as well.
            if (ClientUserSystem.usersChanged != null) ClientUserSystem.usersChanged();
        }

        internal static void Release(FrontendChefMenu menu)
        {
            var customiser = (ChefCustomiser)customiserField.GetValue(menu);
            if (customiser != null) snapshots.Remove(customiser);
        }

        internal static void Clear() { snapshots.Clear(); }
    }
}
