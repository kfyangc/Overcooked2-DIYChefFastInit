using System;
using OC2DIYChef;
using UnityEngine;
using UnityEngine.SceneManagement;

static class ChefGridMapRegression
{
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }

    internal static int Run()
    {
        checks = 0;
        GameUtils.Session = null;
        LobbyFlowController.Instance = null;
        ClientLobbyFlowController.Instance = null;
        T17InGameFlow.Instance = null;
        Check(ChefGridPlayerContext.CurrentPlayers() == null, "Loading/title screens expose no player selection context");
        var arcade = LobbyFlowController.Instance = new LobbyFlowController();
        foreach (var state in new[] { LobbyFlowController.LobbyState.Matchmake, LobbyFlowController.LobbyState.LocalSetup,
            LobbyFlowController.LobbyState.OnlineSetup,
            LobbyFlowController.LobbyState.LocalThemeSelection, LobbyFlowController.LobbyState.OnlineThemeSelection })
        {
            ClientLobbyFlowController.Instance = new ClientLobbyFlowController(state);
            Check(ChefGridPlayerContext.CurrentPlayers() == arcade.m_uiPlayerRoot,
                "Local, online and matchmaking lobbies resolve their own native player root");
        }
        GameUtils.Session = new GameSession { TypeSettings = new GameSession.Settings { WorldMapScene = "Lobbies" } };
        SceneManager.Name = "Lobbies";
        ClientLobbyFlowController.Instance = new ClientLobbyFlowController(LobbyFlowController.LobbyState.LocalSetup);
        Check(ChefGridPlayerContext.CurrentPlayers() == arcade.m_uiPlayerRoot,
            "An arcade session whose WorldMapScene is Lobbies must not be mistaken for a map pause");
        ClientLobbyFlowController.Instance = new ClientLobbyFlowController(LobbyFlowController.LobbyState.LocalThemeSelected);
        Check(ChefGridPlayerContext.CurrentPlayers() == null, "Level selection completion no longer exposes an arcade action");
        GameUtils.Session = new GameSession { TypeSettings = new GameSession.Settings { WorldMapScene = "DLCWorldMap" } };
        SceneManager.Name = "DLCWorldMap";
        Check(ChefGridPlayerContext.CurrentPlayers() == null, "World map waits for its native pause UI");
        var pauseObject = new GameObject("world map pause");
        var pause = pauseObject.AddComponent<InGamePauseMenu>();
        var mapPlayers = new UIPlayerRootMenu();
        pause.SetPlayers(mapPlayers);
        T17InGameFlow.Instance = new T17InGameFlow(pause);
        Check(ChefGridPlayerContext.CurrentPlayers() == null && ChefGridPlayerContext.PausePlayers(pause) == mapPlayers,
            "A paused DLC world map retains the native root but adds no name-menu entry");
        Check(ChefGridPlayerContext.CurrentPlayers() != arcade.m_uiPlayerRoot,
            "An old arcade root cannot redirect a world-map name click");
        pauseObject.SetActive(false);
        Check(ChefGridPlayerContext.CurrentPlayers() == null, "Unpausing hides the world-map action");
        pauseObject.SetActive(true);
        pause.enabled = false;
        Check(ChefGridPlayerContext.CurrentPlayers() == null, "A disabled pause component exposes no world-map action");
        pause.enabled = true;
        SceneManager.Name = "Kitchen";
        Check(ChefGridPlayerContext.CurrentPlayers() == null, "In-level pause cannot use the map-only player entry");
        GameUtils.Session.TypeSettings = null;
        Check(ChefGridPlayerContext.CurrentPlayers() == null, "Missing session settings are handled during loading");
        Check(ChefGridPlayerContext.PausePlayers(null) == null, "A destroyed pause menu has no root");

        var buttons = new[] { new PauseButton(), new PauseButton(), new PauseButton(), new PauseButton(), new PauseButton() };
        var manager = new PauseMenuManager(buttons);
        foreach (var button in buttons) button.Down = true;
        ChefGridPauseGuard.BeforeUpdate(manager, true);
        foreach (var button in buttons) Check(!button.HasUnclaimedPressEvent(), "All native pause press events are claimed while the grid is open");
        foreach (var button in buttons) button.Down = false;
        ChefGridPauseGuard.BeforeUpdate(manager, true);
        foreach (var button in buttons) Check(!button.HasUnclaimedReleaseEvent(), "Native Esc/B release cannot dismiss the underlying pause menu");
        foreach (var button in buttons) button.Down = true;
        ChefGridPauseGuard.BeforeUpdate(manager, false);
        foreach (var button in buttons) Check(button.HasUnclaimedPressEvent(), "Fresh input returns to the native pause after the modal releases ownership");
        var empty = new PauseMenuManager(null);
        ChefGridPauseGuard.BeforeUpdate(empty, true);
        ChefGridPauseGuard.BeforeUpdate(null, true);
        Check(true, "Opening/closing native pause with missing button arrays is safe");
        GameUtils.Session = null;
        SceneManager.Name = "";
        T17InGameFlow.Instance = null;
        LobbyFlowController.Instance = null;
        ClientLobbyFlowController.Instance = null;
        return checks;
    }
}

public interface ILogicalButton
{
    bool HasUnclaimedPressEvent();
    bool HasUnclaimedReleaseEvent();
    void ClaimPressEvent();
    void ClaimReleaseEvent();
}
public sealed class PauseButton : ILogicalButton
{
    public bool Down;
    bool pressClaimed = true, releaseClaimed = true;
    void Update() { pressClaimed &= Down; releaseClaimed &= !Down; }
    public bool HasUnclaimedPressEvent() { Update(); return Down && !pressClaimed; }
    public bool HasUnclaimedReleaseEvent() { Update(); return !Down && !releaseClaimed; }
    public void ClaimPressEvent() { pressClaimed = true; }
    public void ClaimReleaseEvent() { releaseClaimed = true; }
}
public sealed class PauseMenuManager
{
    sealed class SessionData
    {
        public ILogicalButton m_uiUpButton, m_uiDownButton, m_uiSelectButton, m_uiCancelButton;
    }
#pragma warning disable CS0414 // Accessed through the production native-field adapter.
    readonly ILogicalButton[] m_startButtons;
    readonly SessionData m_sessionData;
#pragma warning restore CS0414
    public PauseMenuManager(PauseButton[] buttons)
    {
        if (buttons == null) return;
        m_startButtons = new ILogicalButton[] { buttons[0] };
        m_sessionData = new SessionData { m_uiUpButton = buttons[1], m_uiDownButton = buttons[2],
            m_uiSelectButton = buttons[3], m_uiCancelButton = buttons[4] };
    }
}
public sealed class InGamePauseMenu : MonoBehaviour
{
#pragma warning disable CS0414
    UIPlayerRootMenu m_uiPlayers;
#pragma warning restore CS0414
    public void SetPlayers(UIPlayerRootMenu players) { m_uiPlayers = players; }
}
public sealed class T17InGameFlow
{
    public static T17InGameFlow Instance;
    public int CloseRequests;
#pragma warning disable CS0414
    readonly InGamePauseMenu m_pauseMenu;
#pragma warning restore CS0414
    public T17InGameFlow(InGamePauseMenu pause) { m_pauseMenu = pause; }
    public void ClosePauseMenu() { CloseRequests++; m_pauseMenu.gameObject.SetActive(false); }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public static class SceneManager
    {
        public static string Name;
        public static Scene GetActiveScene() { return new Scene { name = Name }; }
    }
}
