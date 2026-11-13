using System;
using Team17.Online;
using UnityEngine;

namespace OC2DIYChef
{
    // The arcade scene has UI chefs rather than FrontendChefMenu. Bind the same
    // native customisation views and avatar snapshot helper to those existing chefs.
    public sealed class ChefGridLobbyMenu : InGameMenuBehaviour
    {
        readonly ChefCustomiser customiser = new ChefCustomiser();
        readonly FrontendChefCustomisation[] views = new FrontendChefCustomisation[4];
        readonly User[] boundUsers = new User[4];
        GameInputConfig initialInput;
        ChefGridLobby lobby;
        bool showing;

        internal bool Open(ChefGridLobby source, GamepadUser gamer)
        {
            lobby = source;
            if (views[0] == null)
            {
                for (int i = 0; i < views.Length; i++)
                {
                    var child = new GameObject("Lobby chef input " + (i + 1));
                    child.transform.SetParent(transform, false);
                    views[i] = child.AddComponent<FrontendChefCustomisation>();
                    views[i].enabled = false;
                    views[i].m_actualPlayer = (PlayerInputLookup.Player)i;
                }
                customiser.SetChefs(views);
            }
            if (!base.Show(gamer, null, null, false)) return false;
            showing = true;
            initialInput = PlayerInputLookup.GetInputConfig();
            try
            {
                UserSystemUtils.BuildGameInputConfig();
                BindPlayers();
                customiser.CacheCurrentAvatars();
                ClientUserSystem.usersChanged += BindPlayers;
                lobby.BeginInput(views);
                ChefGridMenu.Show(this, views, Close);
                var grid = GetComponent<ChefGridMenu>();
                if (grid == null || !grid.IsOpen) { Hide(); return false; }
                return true;
            }
            catch
            {
                Hide();
                throw;
            }
        }

        void BindPlayers()
        {
            if (!showing || lobby == null || lobby.Flow.m_uiPlayerRoot == null) return;
            var users = ClientUserSystem.m_Users;
            var manager = GameUtils.RequireManager<PlayerManager>();
            for (int i = 0; i < views.Length; i++)
            {
                var view = views[i];
                var user = i < users.Count ? users._items[i] : null;
                var player = user == null ? null : lobby.Flow.m_uiPlayerRoot.GetUIPlayerForUser(user);
                var replacer = player == null ? null : player.GetComponent<ChefMeshReplacer>();
                view.SetChefMeshReplacer(replacer);
                view.SetChefAnimator(replacer == null || replacer.ChefModel == null ? null :
                    replacer.ChefModel.GetComponent<Animator>());
                if (user == null || !user.IsLocal || user.SelectedChefData == null || replacer == null)
                {
                    if (view.IsActive()) view.Deactivate();
                    boundUsers[i] = null;
                    continue;
                }
                if (boundUsers[i] != user && view.IsActive()) view.Deactivate();
                boundUsers[i] = user;
                var previous = view.GetAssignedInput();
                if (previous != null && previous.Pad == (ControlPadInput.PadNum)user.Engagement &&
                    previous.Side == user.PadSide) continue;
                var mapping = user.PadSide == PadSide.Both ? manager.UnsidedAmbiMapping : manager.SidedAmbiMapping;
                var input = new PlayerGameInput((ControlPadInput.PadNum)user.Engagement, user.PadSide, mapping);
                if (view.IsActive()) view.RebindInput(input);
                else view.Activate(input, user.SelectedChefData);
            }
        }

        protected override void Update()
        {
            base.Update();
            var grid = GetComponent<ChefGridMenu>();
            if (showing && grid != null && grid.IsOpen) grid.Tick();
            if (showing && (grid == null || !grid.IsOpen)) Hide();
        }

        public override void Close()
        {
            if (!showing) return;
            customiser.RevertAvatars();
            Hide();
        }

        public override bool Hide(bool restoreInvokerState = true, bool isTabSwitch = false)
        {
            if (showing)
            {
                showing = false;
                ClientUserSystem.usersChanged -= BindPlayers;
                var grid = GetComponent<ChefGridMenu>();
                if (grid != null) grid.CloseGrid();
                foreach (var view in views) if (view != null && view.IsActive()) view.Deactivate();
                if (initialInput != null) PlayerInputLookup.SetInputConfig(initialInput);
                initialInput = null;
                ChefSelectionMenu.Release(customiser);
                if (lobby != null) lobby.EndInput();
            }
            return base.Hide(restoreInvokerState, isTabSwitch);
        }

        protected override void OnDestroy()
        {
            if (showing) Hide(false);
            ChefSelectionMenu.Release(customiser);
            base.OnDestroy();
        }
    }
}
