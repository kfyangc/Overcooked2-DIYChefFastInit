using System;
using System.Reflection;
using OC2DIYChef;
using Team17.Online;
using UnityEngine;

static class ChefGridLobbyRegression
{
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }

    static void Main()
    {
        var session = new ChefGridLobbySession();
        Check(!session.Open && !session.Blocking, "A new lobby does not block inputs");
        for (int frame = 0; frame < 20; frame++)
        {
            session.Begin();
            session.Advance(frame + 1, false);
            Check(session.Open && session.Blocking, "An open grid retains ownership even when every key is up");
            session.End(frame);
            session.Advance(frame, false);
            Check(!session.Open && session.Blocking, "The entire closing frame is protected");
            session.End(frame + 1);
            session.Advance(frame + 1, true);
            Check(session.Blocking, "Holding confirm/cancel/direction retains ownership after closure");
            session.Advance(frame + 2, false);
            Check(!session.Blocking, "Repeated cleanup does not reset the release frame");
        }
        session.Begin(); session.End(100); session.Begin(); session.Advance(101, false);
        Check(session.Open && session.Blocking, "Reopening before key release cannot release the new modal");
        session.Reset();
        Check(!session.Open && !session.Blocking, "Scene cleanup releases ownership");

        var local = User.Create(true, 0, PadSide.Both, 3);
        var remote = User.Create(false, 3, PadSide.Both, 8);
        var left = User.Create(true, 1, PadSide.Left, 11);
        var right = User.Create(true, 1, PadSide.Right, 14);
        ClientUserSystem.m_Users = new UserList(local, remote, left, right);
        DIYChefCustomisation.userDIYChefID = new byte[] { 31, 44, 52, 67 };
        var lobby = new ChefGridLobby();
        foreach (var user in ClientUserSystem.m_Users._items) lobby.Flow.m_uiPlayerRoot.Add(user);
        var host = new GameObject("test lobby menu");
        host.SetActive(false);
        var menu = host.AddComponent<ChefGridLobbyMenu>();
        var initial = PlayerInputLookup.Config = new GameInputConfig();
        Check(menu.Open(lobby, new GamepadUser()), "Arcade menu opens without a frontend flow or camera");
        var grid = host.GetComponent<ChefGridMenu>();
        var views = grid.Views;
        Check(grid.IsOpen && views.Length == 4 && views[0].IsActive() && !views[1].IsActive() &&
            views[2].IsActive() && views[3].IsActive(), "Only local users participate, preserving their global roster indices");
        Check(views[2].GetAssignedInput().Side == PadSide.Left && views[3].GetAssignedInput().Side == PadSide.Right &&
            views[2].GetAssignedInput().Pad == views[3].GetAssignedInput().Pad, "Split players bind independently");
        Check(views[2].m_actualPlayer == (PlayerInputLookup.Player)2 && views[3].m_actualPlayer == (PlayerInputLookup.Player)3,
            "Native avatar messages keep the correct player indices around remote users");
        Check(lobby.Begins == 1 && lobby.Session.Blocking && PlayerInputLookup.Config != initial,
            "The modal owns lobby input and builds participant input mappings");
        var input = views[0].GetAssignedInput();
        ClientUserSystem.usersChanged();
        Check(views[0].GetAssignedInput() == input, "An avatar refresh does not recreate the player's button bindings");
        var old = lobby.Flow.m_uiPlayerRoot.Players[local].GetComponent<ChefMeshReplacer>();
        lobby.Flow.m_uiPlayerRoot.Add(local);
        ClientUserSystem.usersChanged();
        Check(views[0].Replacer != old && views[0].Replacer == lobby.Flow.m_uiPlayerRoot.Players[local].GetComponent<ChefMeshReplacer>(),
            "A recreated lobby player card rebinds the renderer without losing its input");
        Check(!menu.Open(lobby, new GamepadUser()) && lobby.Begins == 1,
            "Repeated opening does not overwrite the original avatar snapshot");

        local.SelectedChefAvatar = 23; left.SelectedChefAvatar = 24; right.SelectedChefAvatar = 25;
        DIYChefCustomisation.userDIYChefID = new byte[] { 90, 99, 91, 92 };
        grid.Cancel();
        Check(!grid.IsOpen && !host.activeSelf && lobby.Ends == 1 && PlayerInputLookup.Config == initial,
            "Cancel closes the grid and restores the exact lobby input configuration");
        Check(local.SelectedChefAvatar == 3 && left.SelectedChefAvatar == 11 && right.SelectedChefAvatar == 14,
            "Native cancellation restores every local official avatar");
        Check(DIYChefCustomisation.userDIYChefID[0] == 31 && DIYChefCustomisation.userDIYChefID[2] == 52 &&
            DIYChefCustomisation.userDIYChefID[3] == 67 && DIYChefCustomisation.userDIYChefID[1] == 99,
            "The real snapshot bridge restores local DIY IDs while leaving remote users untouched");
        Check(ChefLoading.Cancelled.Count == 3 && ChefLoading.Cancelled.Contains(views[2].Replacer),
            "Cancel invalidates every local pending renderer request before avatar restoration");
        Check(ClientUserSystem.usersChanged == null, "Closing unsubscribes the temporary lobby bindings");

        Check(menu.Open(lobby, new GamepadUser()), "The same grid can be reopened");
        local.SelectedChefAvatar = 27; DIYChefCustomisation.userDIYChefID[0] = 88;
        grid.Accept();
        Check(local.SelectedChefAvatar == 27 && DIYChefCustomisation.userDIYChefID[0] == 88 && PlayerInputLookup.Config == initial,
            "Confirmation retains the chosen official/custom chef and restores lobby controls");
        Check(menu.Open(lobby, new GamepadUser()), "Accepted choices can be edited again");
        local.SelectedChefAvatar = 32; DIYChefCustomisation.userDIYChefID[0] = 101;
        menu.Close();
        Check(local.SelectedChefAvatar == 27 && DIYChefCustomisation.userDIYChefID[0] == 88,
            "Reopened cancellation returns to the last accepted choice");

        Check(menu.Open(lobby, new GamepadUser()), "Open before roster reordering");
        ClientUserSystem.m_Users = new UserList(remote, left, right, local);
        DIYChefCustomisation.userDIYChefID = new byte[] { 99, 52, 67, 88 };
        ClientUserSystem.usersChanged();
        Check(!views[0].IsActive() && views[1].GetAssignedInput().Side == PadSide.Left &&
            views[2].GetAssignedInput().Side == PadSide.Right && views[3].GetCurrentSelection() == local.SelectedChefData,
            "Roster reordering refreshes each view's player data rather than retaining a departed user's selection");
        menu.Hide();
        var ends = lobby.Ends;
        menu.Hide();
        Check(lobby.Ends == ends && PlayerInputLookup.Config == initial, "Repeated Hide releases each owned resource only once");

        lobby.Flow.m_uiPlayerRoot.Players.Clear();
        Check(!menu.Open(lobby, new GamepadUser()) && PlayerInputLookup.Config == initial &&
            ClientUserSystem.usersChanged == null, "Opening before lobby models exist exits cleanly without an empty modal");
        foreach (var user in ClientUserSystem.m_Users._items) lobby.Flow.m_uiPlayerRoot.Add(user);

        ChefGridMenu.FailOpen = true;
        Check(!menu.Open(lobby, new GamepadUser()) && !host.activeSelf && !grid.IsOpen &&
            PlayerInputLookup.Config == initial && ClientUserSystem.usersChanged == null,
            "Unavailable grid rolls back bindings, events and input ownership");
        ChefGridMenu.FailOpen = false;
        ChefGridMenu.ThrowOpen = true;
        try { menu.Open(lobby, new GamepadUser()); throw new Exception("Expected open failure"); }
        catch (InvalidOperationException) { }
        Check(!host.activeSelf && PlayerInputLookup.Config == initial && ClientUserSystem.usersChanged == null,
            "An exception while opening cleans up the native menu and mappings");
        ChefGridMenu.ThrowOpen = false;
        Check(menu.Open(lobby, new GamepadUser()), "Open before a rendering failure");
        grid.FailTick = true;
        typeof(ChefGridLobbyMenu).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
        Check(!host.activeSelf && PlayerInputLookup.Config == initial && ClientUserSystem.usersChanged == null,
            "A grid failure releases the lobby in the same update");
        Check(menu.Open(lobby, new GamepadUser()), "Open before a native level load");
        menu.Hide(false); // OnLoadLevel prefix, before the native mapping rebuild.
        var gameplay = PlayerInputLookup.Config = new GameInputConfig();
        typeof(ChefGridLobbyMenu).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
        Check(PlayerInputLookup.Config == gameplay && ClientUserSystem.usersChanged == null,
            "Destruction after level-load closure cannot overwrite newly built gameplay input");
        ChefSelectionMenu.Clear();
        Console.WriteLine("PASS: " + checks + " arcade menu lifecycle, rollback and input ownership assertions; no Unity/game process used.");
    }
}
