using HarmonyLib;
using System.Reflection;

namespace OC2DIYChef
{
    internal static class ChefGridPlayerContext
    {
        static readonly FieldInfo state = AccessTools.Field(typeof(ClientLobbyFlowController), "m_state");
        static readonly FieldInfo pausePlayers = AccessTools.Field(typeof(InGamePauseMenu), "m_uiPlayers");

        internal static UIPlayerRootMenu PausePlayers(InGamePauseMenu menu)
        {
            return menu == null ? null : (UIPlayerRootMenu)pausePlayers.GetValue(menu);
        }

        internal static UIPlayerRootMenu CurrentPlayers()
        {
            // Arcade sessions can also use their lobby as TypeSettings.WorldMapScene.
            // Resolve the active lobby first, including the setup states that already
            // display the native theme-selection UI.
            var lobby = LobbyFlowController.Instance;
            var client = ClientLobbyFlowController.Instance;
            if (lobby != null && client != null)
            {
                var value = (LobbyFlowController.LobbyState)state.GetValue(client);
                if (value == LobbyFlowController.LobbyState.LocalSetup || value == LobbyFlowController.LobbyState.OnlineSetup ||
                    value == LobbyFlowController.LobbyState.LocalThemeSelection || value == LobbyFlowController.LobbyState.OnlineThemeSelection ||
                    value == LobbyFlowController.LobbyState.Matchmake) return lobby.m_uiPlayerRoot;
            }
            // Map selection uses the native pause menu's Change Chef action.
            // Only arcade players receive an additional name-menu entry.
            return null;
        }
    }
}
