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
            Check(session.Blocking, "Held menu keys retain ownership after closure");
            session.Advance(frame + 2, false);
            Check(!session.Blocking, "Repeated cleanup does not reset the release frame");
        }
        session.Begin(); session.End(100); session.Begin(); session.Advance(101, false);
        Check(session.Open && session.Blocking, "Reopening before release cannot release the new modal");
        session.Reset();
        Check(!session.Open && !session.Blocking, "Scene cleanup releases ownership");

        var local = User.Create(true, 0, PadSide.Both, 3);
        var remote = User.Create(false, 1, PadSide.Left, 8); // Same pad key as a local, different machine.
        var left = User.Create(true, 1, PadSide.Left, 11);
        var right = User.Create(true, 1, PadSide.Right, 14);
        ClientUserSystem.m_Users = new UserList(local, remote, left, right);
        DIYChefCustomisation.userDIYChefID = new byte[] { 31, 44, 52, 67 };
        Check(ChefGridLobbySelection.Capture(null) == null && ChefGridLobbySelection.Capture(remote) == null &&
            ChefGridLobbySelection.Capture(User.Create(true, 2, PadSide.Both, 0)) == null,
            "Null, remote and missing users cannot open a targeted selection");
        var selection = ChefGridLobbySelection.Capture(left);
        Check(selection.Find() == 2 && selection.Owns(left) && !selection.Owns(right) && !selection.Owns(remote),
            "Target identity distinguishes split sides and excludes remote users even with the same pad key");

        checks += ChefGridMapRegression.Run();
        checks += ChefGridWorldMapRegression.Run();
        checks += ChefGridLobbyBindingRegression.Run();

        var lobby = new ChefGridLobby();
        foreach (var user in ClientUserSystem.m_Users._items) lobby.Flow.m_uiPlayerRoot.Add(user);
        var host = new GameObject("test lobby menu");
        host.SetActive(false);
        var menu = host.AddComponent<ChefGridLobbyMenu>();
        var initial = PlayerInputLookup.Config = new GameInputConfig();
        Check(menu.Open(lobby, new GamepadUser(), selection), "Clicking P3 opens without a frontend flow or camera");
        var grid = host.GetComponent<ChefGridMenu>();
        var views = grid.Views;
        Check(grid.IsOpen && views[0].IsActive() && !views[1].IsActive() && views[2].IsActive() && views[3].IsActive(),
            "Clicking any local name shows every local participant but never the remote user");
        int nativeUpdates = InGameMenuBehaviour.Updates;
        Tick(menu);
        Check(grid.IsOpen && InGameMenuBehaviour.Updates == nativeUpdates,
            "The active grid is the sole input owner; native unsided cancel polling cannot bypass P1-only back");
        Check(views[2].GetAssignedInput().Side == PadSide.Left && views[2].m_actualPlayer == (PlayerInputLookup.Player)2,
            "The selected split player retains its actual input side and global avatar-message index");
        Check(lobby.Begins == 1 && lobby.Session.Blocking && PlayerInputLookup.Config != initial,
            "The modal owns lobby input while editing all local players");
        var input = views[2].GetAssignedInput();
        ClientUserSystem.usersChanged();
        Check(views[2].GetAssignedInput() == input, "Avatar refresh does not recreate button bindings");
        var oldRenderer = views[2].Replacer;
        lobby.Flow.m_uiPlayerRoot.Add(left);
        ClientUserSystem.usersChanged();
        Check(views[2].Replacer != oldRenderer && views[2].GetAssignedInput() == input,
            "Recreated lobby cards rebind the renderer while retaining the selected player's controls");
        Check(!menu.Open(lobby, new GamepadUser(), ChefGridLobbySelection.Capture(right)) && lobby.Begins == 1,
            "A second opening cannot replace the active target or its original snapshot");

        local.SelectedChefAvatar = 23; remote.SelectedChefAvatar = 24;
        left.SelectedChefAvatar = 25; right.SelectedChefAvatar = 26;
        DIYChefCustomisation.userDIYChefID = new byte[] { 90, 99, 91, 92 };
        grid.Cancel();
        Check(!grid.IsOpen && !host.activeSelf && lobby.Ends == 1 && PlayerInputLookup.Config == initial,
            "Cancel closes the grid and restores the exact lobby mapping");
        Check(left.SelectedChefAvatar == 11 && DIYChefCustomisation.userDIYChefID[2] == 52,
            "Cancel restores the clicked player's official and custom chef");
        Check(local.SelectedChefAvatar == 3 && remote.SelectedChefAvatar == 24 && right.SelectedChefAvatar == 14 &&
            DIYChefCustomisation.userDIYChefID[0] == 31 && DIYChefCustomisation.userDIYChefID[1] == 99 &&
            DIYChefCustomisation.userDIYChefID[3] == 67, "Cancel restores every local player and leaves the remote choice untouched");
        Check(ChefLoading.Cancelled.Count == 3 && ChefLoading.Cancelled.Contains(views[0].Replacer) &&
            ChefLoading.Cancelled.Contains(views[2].Replacer) && ChefLoading.Cancelled.Contains(views[3].Replacer) &&
            ChefGridRoster.Selected.Count == 3 && !ChefGridRoster.Selected.Contains(1),
            "Restoration cancels all participating renderer requests and sends only local avatar updates");
        Check(ClientUserSystem.usersChanged == null, "Closing unsubscribes temporary player bindings");

        Check(menu.Open(lobby, new GamepadUser(), ChefGridLobbySelection.Capture(left)), "The same name menu can reopen the grid");
        left.SelectedChefAvatar = 27; DIYChefCustomisation.userDIYChefID[2] = 88;
        grid.Accept();
        Check(left.SelectedChefAvatar == 27 && DIYChefCustomisation.userDIYChefID[2] == 88 && PlayerInputLookup.Config == initial,
            "One player's confirmation retains the choice and returns directly to the lobby");
        Check(lobby.Commits == 1, "Confirmation reaches the shared parent-menu completion callback");
        grid.Accept();
        Check(lobby.Commits == 1, "A repeated confirmation cannot close the parent twice");
        Check(menu.Open(lobby, new GamepadUser(), ChefGridLobbySelection.Capture(left)), "Accepted choices can be edited again");
        left.SelectedChefAvatar = 32; DIYChefCustomisation.userDIYChefID[2] = 101;
        menu.Close();
        Check(left.SelectedChefAvatar == 27 && DIYChefCustomisation.userDIYChefID[2] == 88,
            "Reopened cancellation returns to the last accepted choice");

        Check(menu.Open(lobby, new GamepadUser(), ChefGridLobbySelection.Capture(left)), "Open before player reordering");
        ClientUserSystem.m_Users = new UserList(remote, left, right, local);
        DIYChefCustomisation.userDIYChefID = new byte[] { 99, 88, 67, 31 };
        ClientUserSystem.usersChanged();
        Check(!views[0].IsActive() && views[1].IsActive() && views[2].IsActive() && views[3].IsActive() &&
            views[1].GetCurrentSelection() != left.SelectedChefData &&
            views[1].GetCurrentSelection().Character == left.SelectedChefData.Character,
            "All local participants follow their global indices without sharing mutable selection data");
        left.SelectedChefAvatar = 40; DIYChefCustomisation.userDIYChefID[1] = 100;
        grid.Cancel();
        Check(left.SelectedChefAvatar == 27 && DIYChefCustomisation.userDIYChefID[1] == 88 &&
            right.SelectedChefAvatar == 14 && local.SelectedChefAvatar == 3,
            "Cancel restores each local identity at its new index while preserving the remote user");
        var ends = lobby.Ends;
        menu.Hide();
        Check(lobby.Ends == ends && PlayerInputLookup.Config == initial, "Repeated Hide releases owned resources only once");

        Check(menu.Open(lobby, new GamepadUser(), ChefGridLobbySelection.Capture(left)), "Open before the selected player leaves");
        ClientUserSystem.m_Users = new UserList(remote, right, local);
        DIYChefCustomisation.userDIYChefID = new byte[] { 99, 67, 31, 255 };
        ClientUserSystem.usersChanged();
        Tick(menu);
        Check(grid.IsOpen && host.activeSelf && views[1].IsActive() && views[2].IsActive(),
            "The invoker leaving does not close the independent panels of remaining locals");
        var joined = User.Create(true, 2, PadSide.Both, 15);
        ClientUserSystem.m_Users = new UserList(remote, right, local, joined);
        DIYChefCustomisation.userDIYChefID = new byte[] { 99, 67, 31, 123 };
        lobby.Flow.m_uiPlayerRoot.Add(joined);
        ClientUserSystem.usersChanged();
        Check(views[3].IsActive() && views[3].GetCurrentSelection() != joined.SelectedChefData,
            "A local player joining an open panel receives its own read-only input binding");
        right.SelectedChefAvatar = 45; local.SelectedChefAvatar = 46; joined.SelectedChefAvatar = 50;
        DIYChefCustomisation.userDIYChefID = new byte[] { 99, 101, 102, 144 };
        grid.Cancel();
        Check(!grid.IsOpen && !host.activeSelf && right.SelectedChefAvatar == 14 && local.SelectedChefAvatar == 3 &&
            joined.SelectedChefAvatar == 15 && DIYChefCustomisation.userDIYChefID[3] == 123 &&
            remote.SelectedChefAvatar == 24 && DIYChefCustomisation.userDIYChefID[0] == 99,
            "Cancel restores remaining and newly joined locals from their own first-participation snapshots, never the remote");
        ClientUserSystem.m_Users = new UserList(remote, left, right, local);
        DIYChefCustomisation.userDIYChefID = new byte[] { 99, 88, 67, 31 };

        lobby.Flow.m_uiPlayerRoot.Players.Clear();
        Check(!menu.Open(lobby, new GamepadUser(), ChefGridLobbySelection.Capture(left)) &&
            PlayerInputLookup.Config == initial && ClientUserSystem.usersChanged == null,
            "Missing lobby models close cleanly without an empty modal");
        foreach (var user in ClientUserSystem.m_Users._items) lobby.Flow.m_uiPlayerRoot.Add(user);
        ChefGridMenu.FailOpen = true;
        Check(!menu.Open(lobby, new GamepadUser(), ChefGridLobbySelection.Capture(left)) && !host.activeSelf &&
            PlayerInputLookup.Config == initial && ClientUserSystem.usersChanged == null,
            "Unavailable grid cleans up bindings and input ownership");
        ChefGridMenu.FailOpen = false;
        ChefGridMenu.ThrowOpen = true;
        try { menu.Open(lobby, new GamepadUser(), ChefGridLobbySelection.Capture(left)); throw new Exception("Expected open failure"); }
        catch (InvalidOperationException) { }
        Check(!host.activeSelf && PlayerInputLookup.Config == initial && ClientUserSystem.usersChanged == null,
            "Opening exceptions clean up native mappings and subscriptions");
        ChefGridMenu.ThrowOpen = false;
        Check(menu.Open(lobby, new GamepadUser(), ChefGridLobbySelection.Capture(left)), "Open before a rendering failure");
        grid.FailTick = true;
        Tick(menu);
        Check(!host.activeSelf && PlayerInputLookup.Config == initial && ClientUserSystem.usersChanged == null,
            "Grid failure releases the lobby in the same update");
        Check(menu.Open(lobby, new GamepadUser(), ChefGridLobbySelection.Capture(left)), "Open before level loading");
        menu.Hide(false);
        var gameplay = PlayerInputLookup.Config = new GameInputConfig();
        typeof(ChefGridLobbyMenu).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
        Check(PlayerInputLookup.Config == gameplay && ClientUserSystem.usersChanged == null,
            "Old scene destruction cannot overwrite newly built gameplay input");
        var mapHost = new GameObject("new map menu");
        mapHost.SetActive(false);
        var mapMenu = mapHost.AddComponent<ChefGridLobbyMenu>();
        Check(mapMenu.Open(lobby, new GamepadUser(), ChefGridLobbySelection.Capture(left)), "Open before a direct scene mapping change");
        var newGameplay = PlayerInputLookup.Config = new GameInputConfig();
        typeof(ChefGridLobbyMenu).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(mapMenu, null);
        Check(PlayerInputLookup.Config == newGameplay && ClientUserSystem.usersChanged == null,
            "Scene destruction without an early Hide preserves mappings already installed by the new scene");
        Console.WriteLine("PASS: " + checks + " standalone player-menu context, selection and input assertions; no Unity/game process used.");
    }

    static void Tick(ChefGridLobbyMenu menu)
    {
        typeof(ChefGridLobbyMenu).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
    }
}
