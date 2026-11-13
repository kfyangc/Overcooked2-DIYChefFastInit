using System;
using OC2DIYChef;
using Team17.Online;
using UnityEngine;

static class ChefGridLobbyBindingRegression
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
        var savedUsers = ClientUserSystem.m_Users;
        var savedIds = DIYChefCustomisation.userDIYChefID;
        var savedEvents = ClientUserSystem.usersChanged;
        var savedFlow = T17FrontendFlow.Instance;
        var dlc = new DlcData { m_DLCID = 99 };
        var dlcChef = GameUtils.Directory.Avatars[7];
        var savedDlc = dlcChef.ForDlc;
        var savedMessages = ClientMessenger.AvatarMessages;
        var savedLastAvatar = ClientMessenger.LastAvatar;
        int savedSelections = ChefGridRoster.Selected.Count;
        int savedCancellations = ChefLoading.Cancelled.Count;
        try
        {
            dlcChef.ForDlc = dlc;
            var target = User.Create(true, 0, PadSide.Both, 3);
            var neighbour = User.Create(true, 1, PadSide.Left, 8);
            ClientUserSystem.m_Users = new UserList(target, neighbour);
            DIYChefCustomisation.userDIYChefID = new byte[] { 52, 63, 255, 255 };
            var original = target.SelectedChefData = new GameSession.SelectedChefData(
                GameUtils.Directory.Avatars[3], new ChefColourData());
            var flow = T17FrontendFlow.Instance = new T17FrontendFlow { AutoOpenFrontendDlcData = dlc };
            var probe = new GameObject("Native activation comparison").AddComponent<FrontendChefCustomisation>();
            var input = new PlayerGameInput(ControlPadInput.PadNum.One, PadSide.Both, new AmbiControlsMappingData());
            int sent = ClientMessenger.AvatarMessages;
            probe.Activate(input, original);
            Check(ClientMessenger.AvatarMessages == sent + 1 && ClientMessenger.LastAvatar == 7 && original.Character == dlcChef,
                "The native Activate DLC branch mutates the shared selection and sends an avatar before any grid input");
            probe.Deactivate();
            Check(flow.AutoOpenFrontendDlcData == null && flow.ClearAutoOpenCalls == 1,
                "The native Deactivate path changes global frontend auto-open state");

            target.SelectedChefAvatar = 3;
            original.Character = GameUtils.Directory.Avatars[3];
            flow.AutoOpenFrontendDlcData = dlc;
            var lobby = new ChefGridLobby();
            lobby.Flow.m_uiPlayerRoot.Add(target);
            lobby.Flow.m_uiPlayerRoot.Add(neighbour);
            var host = new GameObject("Read-only lobby binding");
            host.SetActive(false);
            var menu = host.AddComponent<ChefGridLobbyMenu>();
            int activates = FrontendChefCustomisation.ActivateCalls;
            int deactivates = FrontendChefCustomisation.DeactivateCalls;
            sent = ClientMessenger.AvatarMessages;
            Check(menu.Open(lobby, new GamepadUser(), ChefGridLobbySelection.Capture(target)),
                "A lobby grid opens while a frontend DLC auto-selection request still exists");
            var grid = host.GetComponent<ChefGridMenu>();
            var view = grid.Views[0];
            Check(ClientMessenger.AvatarMessages == sent && ChefGridRoster.Selected.Count == savedSelections &&
                target.SelectedChefAvatar == 3 && original.Character == GameUtils.Directory.Avatars[3] &&
                DIYChefCustomisation.userDIYChefID[0] == 52,
                "Opening the grid sends zero avatar messages and preserves official, custom and shared chef selection");
            Check(view.IsActive() && !view.enabled && FrontendChefCustomisation.ActivateCalls == activates,
                "Synthetic views expose active grid input without enabling the native carousel or calling Activate");
            Check(view.GetCurrentSelection() != original && view.GetCurrentSelection().Character == original.Character &&
                view.GetCurrentSelection().Colour == original.Colour,
                "The synthetic view owns an independent copy of the selected data with the original character and colour");
            view.GetCurrentSelection().Character = dlcChef;
            Check(original.Character == GameUtils.Directory.Avatars[3],
                "Changing view-local data cannot mutate the User's shared selection during preparation");
            var assigned = view.GetAssignedInput();
            ClientUserSystem.usersChanged();
            Check(view.GetAssignedInput() == assigned && !view.enabled && ClientMessenger.AvatarMessages == sent,
                "Avatar refresh preserves Pad/Side bindings and cannot trigger native DLC selection");
            menu.Hide(false);
            Check(!view.IsActive() && !view.enabled && FrontendChefCustomisation.DeactivateCalls == deactivates &&
                flow.AutoOpenFrontendDlcData == dlc && flow.ClearAutoOpenCalls == 1 && ClientMessenger.AvatarMessages == sent,
                "Stopping a synthetic view sends no messages and does not clear global frontend flow state");

            Check(menu.Open(lobby, new GamepadUser(), ChefGridLobbySelection.Capture(target)), "A read-only bound view can reopen");
            target.SelectedChefAvatar = 9;
            DIYChefCustomisation.userDIYChefID[0] = 77;
            var replaced = target;
            assigned = grid.Views[0].GetAssignedInput();
            var reboundView = grid.Views[0];
            target = User.Create(true, 0, PadSide.Both, 9);
            target.SelectedChefData = new GameSession.SelectedChefData(GameUtils.Directory.Avatars[9], original.Colour);
            ClientUserSystem.m_Users = new UserList(target, neighbour);
            lobby.Flow.m_uiPlayerRoot.Add(target);
            ClientUserSystem.usersChanged();
            Check(reboundView.GetAssignedInput() == assigned && !reboundView.enabled &&
                ClientMessenger.AvatarMessages == sent && ChefGridRoster.Selected.Count == savedSelections &&
                FrontendChefCustomisation.ActivateCalls == activates && FrontendChefCustomisation.DeactivateCalls == deactivates,
                "Rebuilding the same identity preserves the existing input and disabled native carousel without avatar messages");
            Check(target.SelectedChefAvatar == 9 && target.SelectedChefData.Character == GameUtils.Directory.Avatars[9] &&
                DIYChefCustomisation.userDIYChefID[0] == 77 && neighbour.SelectedChefAvatar == 8,
                "Identity-based rebinding reads the replacement user without changing its current choice or the neighbour");
            menu.Close();
            Check(target.SelectedChefAvatar == 3 && DIYChefCustomisation.userDIYChefID[0] == 52 &&
                neighbour.SelectedChefAvatar == 8 && DIYChefCustomisation.userDIYChefID[1] == 63 &&
                ChefGridRoster.Selected.Count == savedSelections + 1,
                "Explicit cancel still restores the target snapshot once and leaves the neighbour untouched");
            Check(replaced.SelectedChefAvatar == 9 && target.SelectedChefAvatar == 3,
                "Cancel restores the current same-identity User object rather than the obsolete captured object");
            Check(flow.AutoOpenFrontendDlcData == dlc && FrontendChefCustomisation.ActivateCalls == activates &&
                FrontendChefCustomisation.DeactivateCalls == deactivates,
                "Cancellation never enters the frontend carousel lifecycle");
        }
        finally
        {
            ClientUserSystem.m_Users = savedUsers;
            DIYChefCustomisation.userDIYChefID = savedIds;
            ClientUserSystem.usersChanged = savedEvents;
            T17FrontendFlow.Instance = savedFlow;
            dlcChef.ForDlc = savedDlc;
            ClientMessenger.AvatarMessages = savedMessages;
            ClientMessenger.LastAvatar = savedLastAvatar;
            ChefGridRoster.Selected.RemoveRange(savedSelections, ChefGridRoster.Selected.Count - savedSelections);
            ChefLoading.Cancelled.RemoveRange(savedCancellations, ChefLoading.Cancelled.Count - savedCancellations);
        }
        return checks;
    }
}
