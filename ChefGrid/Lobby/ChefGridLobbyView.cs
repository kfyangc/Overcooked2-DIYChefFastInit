using HarmonyLib;
using System;
using System.Reflection;

namespace OC2DIYChef
{
    // Synthetic lobby views supply the grid's native data API, but never run the
    // frontend carousel lifecycle: Activate may auto-select a DLC chef and send
    // an avatar message; Deactivate changes frontend flow state.
    internal static class ChefGridLobbyView
    {
        static readonly FieldInfo assignedInput = AccessTools.Field(typeof(FrontendChefCustomisation), "m_assignedInput");
        static readonly FieldInfo chefData = AccessTools.Field(typeof(FrontendChefCustomisation), "m_chefData");
        static readonly FieldInfo directory = AccessTools.Field(typeof(FrontendChefCustomisation), "m_avatarDirectory");
        static readonly FieldInfo unlocked = AccessTools.Field(typeof(FrontendChefCustomisation), "m_unlockedAvatars");
        static readonly FieldInfo selection = AccessTools.Field(typeof(FrontendChefCustomisation), "m_chefSelection");
        static readonly FieldInfo left = AccessTools.Field(typeof(FrontendChefCustomisation), "m_leftButton");
        static readonly FieldInfo right = AccessTools.Field(typeof(FrontendChefCustomisation), "m_rightButton");

        internal static void Bind(FrontendChefCustomisation view, PlayerGameInput input, GameSession.SelectedChefData current)
        {
            view.enabled = false;
            var avatars = GameUtils.GetAvatarDirectoryData().Avatars;
            var available = GameUtils.GetMetaGameProgress().GetUnlockedAvatars();
            directory.SetValue(view, avatars);
            unlocked.SetValue(view, available);
            selection.SetValue(view, Array.IndexOf(available, current.Character));
            // Preparing a view must not mutate the User's shared selection object.
            chefData.SetValue(view, new GameSession.SelectedChefData(current.Character, current.Colour));
            assignedInput.SetValue(view, input);
            left.SetValue(view, null);
            right.SetValue(view, null);
        }

        internal static void Unbind(FrontendChefCustomisation view)
        {
            view.enabled = false;
            assignedInput.SetValue(view, null);
            chefData.SetValue(view, null);
            left.SetValue(view, null);
            right.SetValue(view, null);
        }
    }
}
