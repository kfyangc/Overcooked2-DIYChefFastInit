using HarmonyLib;
using System.Reflection;
using UnityEngine.SceneManagement;

namespace OC2DIYChef
{
    internal static class ChefGridPlayerContext
    {
        static readonly FieldInfo state = AccessTools.Field(typeof(ClientLobbyFlowController), "m_state");
        static readonly FieldInfo pauseMenu = AccessTools.Field(typeof(T17InGameFlow), "m_pauseMenu");
        static readonly FieldInfo pausePlayers = AccessTools.Field(typeof(InGamePauseMenu), "m_uiPlayers");

        internal static UIPlayerRootMenu PausePlayers(InGamePauseMenu menu)
        {
            return menu == null ? null : (UIPlayerRootMenu)pausePlayers.GetValue(menu);
        }

        internal static UIPlayerRootMenu CurrentPlayers()
        {
            var session = GameUtils.GetGameSession();
            if (session != null && session.TypeSettings != null &&
                SceneManager.GetActiveScene().name == session.TypeSettings.WorldMapScene)
            {
                var flow = T17InGameFlow.Instance;
                var pause = flow == null ? null : (InGamePauseMenu)pauseMenu.GetValue(flow);
                return pause != null && pause.isActiveAndEnabled ? PausePlayers(pause) : null;
            }
            var lobby = LobbyFlowController.Instance;
            var client = ClientLobbyFlowController.Instance;
            if (lobby == null || client == null) return null;
            var value = (LobbyFlowController.LobbyState)state.GetValue(client);
            if (value != LobbyFlowController.LobbyState.LocalThemeSelection &&
                value != LobbyFlowController.LobbyState.OnlineThemeSelection && value != LobbyFlowController.LobbyState.Matchmake) return null;
            return lobby.m_uiPlayerRoot;
        }
    }
}
