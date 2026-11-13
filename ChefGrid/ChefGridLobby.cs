using HarmonyLib;
using OC2DIYChef.Extension;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Team17.Online;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
    public sealed class ChefGridLobby : MonoBehaviour
    {
        sealed class OpenControl
        {
            internal User User;
            internal PlayerGameInput Input;
            internal ILogicalButton Button;
            internal bool Down;
        }

        static readonly FieldInfo state = AccessTools.Field(typeof(ClientLobbyFlowController), "m_state");
        static readonly FieldInfo leave = AccessTools.Field(typeof(ClientLobbyFlowController), "m_leaveButton");
        static readonly FieldInfo localInputs = AccessTools.Field(typeof(ClientLobbyFlowController), "m_localUserInput");
        static readonly FieldInfo changeTeam = AccessTools.Field(
            AccessTools.Inner(typeof(ClientLobbyFlowController), "UserInput"), "m_changeTeamButton");
        static ChefGridLobby current;
        readonly ChefGridLobbySession session = new ChefGridLobbySession();
        readonly List<OpenControl> controls = new List<OpenControl>();
        readonly List<ILogicalButton> watched = new List<ILogicalButton>();
        readonly List<Suppressor> suppressors = new List<Suppressor>();
        readonly HashSet<T17EventSystem> eventSystems = new HashSet<T17EventSystem>();
        ChefGridLobbyMenu menu;
        GameObject canvasObject;
        Button button;
        Text caption, key;
        Image icon;
        Font font;
        float refreshAt;
        bool failed;
        internal LobbyFlowController Flow { get; private set; }
        internal static bool Blocking { get { return current != null && current.session.Blocking; } }
        bool Chinese { get { return Localization.GetLanguage() == SupportedLanguages.Chinese; } }

        internal static void Attach(LobbyFlowController flow)
        {
            if (flow.GetComponent<ChefGridLobby>() != null) return;
            var entry = flow.gameObject.AddComponent<ChefGridLobby>();
            entry.Flow = flow;
            current = entry;
        }

        bool Available()
        {
            if (!ChefGridMenu.Enabled || Flow == null || Flow.m_uiPlayerRoot == null ||
                ClientLobbyFlowController.Instance == null || controls.Count == 0) return false;
            var value = (LobbyFlowController.LobbyState)state.GetValue(ClientLobbyFlowController.Instance);
            return value == LobbyFlowController.LobbyState.LocalThemeSelection ||
                value == LobbyFlowController.LobbyState.OnlineThemeSelection || value == LobbyFlowController.LobbyState.Matchmake;
        }

        void Update()
        {
            if (failed || Flow == null) return;
            try
            {
                SyncControls();
                bool available = Available();
                bool interactive = available && !session.Blocking && ChefGridMenu.Active == null &&
                    Application.isFocused && !T17DialogBoxManager.HasAnyOpenDialogs();
                if (canvasObject == null && available) BuildButton();
                if (canvasObject != null)
                {
                    canvasObject.SetActive(available && !session.Open);
                    button.interactable = interactive;
                }
                OpenControl pressed = null;
                foreach (var control in controls)
                {
                    bool down = control.Button.IsDown();
                    if (down && !control.Down && !KeyboardUtils.IsKeyboard(control.Input)) pressed = control;
                    control.Down = down;
                }
                if (interactive && (Input.GetKeyDown(KeyCode.F2) || pressed != null)) Open(pressed);
                if (canvasObject != null && controls.Count > 0 && Time.unscaledTime >= refreshAt)
                {
                    refreshAt = Time.unscaledTime + 0.5f;
                    caption.text = Chinese ? "切换厨师" : "CHANGE CHEF";
                    var control = controls[0];
                    var prompt = KeyboardUtils.IsKeyboard(control.Input) ? new ChefGridButtonPrompt { Key = "F2" } :
                        ChefGridButtonPrompts.Button(control.Input, control.Button, ControlPadInput.Button.Y);
                    icon.sprite = prompt.Icon;
                    icon.enabled = prompt.Icon != null;
                    key.text = prompt.Key;
                    key.enabled = prompt.Icon == null;
                }
            }
            catch (Exception ex)
            {
                failed = true;
                Shutdown();
                DIYChefPlugin.Warn("Arcade chef entry unavailable: " + ex);
            }
        }

        void SyncControls()
        {
            var users = ClientUserSystem.m_Users;
            controls.RemoveAll(x => !users.Contains(x.User) || !x.User.IsLocal);
            var manager = GameUtils.RequireManager<PlayerManager>();
            for (int i = 0; i < users.Count; i++)
            {
                var user = users._items[i];
                if (!user.IsLocal) continue;
                var existing = controls.Find(x => x.User == user);
                if (existing != null && existing.Input.Pad == (ControlPadInput.PadNum)user.Engagement &&
                    existing.Input.Side == user.PadSide) continue;
                if (existing != null) controls.Remove(existing);
                var mapping = user.PadSide == PadSide.Both ? manager.UnsidedAmbiMapping : manager.SidedAmbiMapping;
                var input = new PlayerGameInput((ControlPadInput.PadNum)user.Engagement, user.PadSide, mapping);
                var logical = PlayerInputLookup.GetFixedButton(PlayerInputLookup.LogicalButtonID.UIMultiChefMenu, input);
                controls.Add(new OpenControl { User = user, Input = input, Button = logical, Down = logical.IsDown() });
            }
            controls.Sort((a, b) => Array.IndexOf(users._items, a.User).CompareTo(Array.IndexOf(users._items, b.User)));
        }

        void Open(OpenControl control)
        {
            if (!Available() || session.Blocking || ChefGridMenu.Active != null ||
                T17DialogBoxManager.HasAnyOpenDialogs()) return;
            if (menu == null)
            {
                var host = new GameObject("DIYChef arcade selection");
                host.SetActive(false);
                host.transform.SetParent(transform, false);
                menu = host.AddComponent<ChefGridLobbyMenu>();
            }
            var gamer = (control ?? controls[0]).User.GamepadUser;
            if (gamer != null && T17EventSystemsManager.Instance.GetEventSystemForGamepadUser(gamer) == null)
                T17EventSystemsManager.Instance.AssignFreeEventSystemToGamepadUser(gamer);
            Flow.m_uiPlayerRoot.CloseAllPlayerMenus();
            if (menu.Open(this, gamer)) DIYChefPlugin.Log("Chef grid opened from arcade lobby");
        }

        internal void BeginInput(FrontendChefCustomisation[] views)
        {
            session.Begin();
            watched.Clear();
            foreach (var view in views)
            {
                var input = view.GetAssignedInput();
                if (input == null) continue;
                foreach (var id in new[] { PlayerInputLookup.LogicalButtonID.UILeftPlayerSpecific,
                    PlayerInputLookup.LogicalButtonID.UIRightPlayerSpecific, PlayerInputLookup.LogicalButtonID.UIUp,
                    PlayerInputLookup.LogicalButtonID.UIDown, PlayerInputLookup.LogicalButtonID.UISelect,
                    PlayerInputLookup.LogicalButtonID.UICancel, PlayerInputLookup.LogicalButtonID.UIMultiChefMenu,
                    PlayerInputLookup.LogicalButtonID.Curse, PlayerInputLookup.LogicalButtonID.WorkstationInteract,
                    PlayerInputLookup.LogicalButtonID.PlayerSwitch })
                    watched.Add(PlayerInputLookup.GetFixedButton(id, input));
            }
            foreach (var control in controls)
            {
                var gamer = control.User.GamepadUser;
                var events = gamer == null ? null : T17EventSystemsManager.Instance.GetEventSystemForGamepadUser(gamer);
                if (events != null && eventSystems.Add(events)) suppressors.Add(events.Disable(this));
            }
            // Stop native events for the opening button before another Update can use it.
            ConsumeLobbyButtons(ClientLobbyFlowController.Instance);
        }

        internal void EndInput() { session.End(Time.frameCount); }

        internal static void BeforeLevelLoad()
        {
            // Restore the saved lobby mapping before the native load handler builds
            // gameplay input. Scene destruction must never overwrite that new mapping.
            if (current != null && current.menu != null) current.menu.Hide(false);
        }

        void LateUpdate()
        {
            if (!session.Blocking) return;
            bool held = Input.GetMouseButton(0);
            foreach (var code in new[] { KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Escape, KeyCode.Space,
                KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.F2 }) held |= Input.GetKey(code);
            foreach (var logical in watched) if (logical != null) held |= logical.IsDown();
            session.Advance(Time.frameCount, held);
            if (!session.Blocking) ReleaseInput();
        }

        static void Claim(ILogicalButton logical)
        {
            if (logical == null) return;
            logical.HasUnclaimedPressEvent(); logical.ClaimPressEvent();
            logical.HasUnclaimedReleaseEvent(); logical.ClaimReleaseEvent();
        }

        internal static void ConsumeLobbyButtons(ClientLobbyFlowController client)
        {
            if (!Blocking || client == null) return;
            Claim((ILogicalButton)leave.GetValue(client));
            var inputs = (IEnumerable)localInputs.GetValue(client);
            if (inputs != null) foreach (var input in inputs) Claim((ILogicalButton)changeTeam.GetValue(input));
        }

        void ReleaseInput()
        {
            foreach (var suppression in suppressors) if (suppression != null) suppression.Release();
            suppressors.Clear(); eventSystems.Clear(); watched.Clear();
        }

        void BuildButton()
        {
            font = Font.CreateDynamicFontFromOSFont(Chinese ? new[] { "Microsoft YaHei", "Arial" } : new[] { "Segoe UI", "Arial" }, 24);
            canvasObject = new GameObject("DIYChef arcade button", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var target = Element("Change chef", canvasObject.transform, 1016, 20, 240, 48);
            var image = target.gameObject.AddComponent<Image>();
            image.color = new Color(0.96f, 0.9f, 0.91f);
            button = target.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => Open(null));
            caption = Label(target, "Caption", 8, 0, 172, 48, 22);
            key = Label(target, "Key", 184, 0, 48, 48, 19);
            icon = Element("Key icon", target, 190, 8, 32, 32).gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;
        }

        static RectTransform Element(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        Text Label(Transform parent, string name, float x, float y, float width, float height, int size)
        {
            var text = Element(name, parent, x, y, width, height).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 14;
            text.resizeTextMaxSize = size;
            text.color = new Color(0.16f, 0.28f, 0.36f);
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }

        internal void Shutdown()
        {
            if (menu != null) { menu.Hide(false); Object.Destroy(menu.gameObject); menu = null; }
            session.Reset(); ReleaseInput();
            if (canvasObject != null) { Object.Destroy(canvasObject); canvasObject = null; }
            if (font != null) { Object.Destroy(font); font = null; }
            if (current == this) current = null;
        }

        void OnDestroy() { Shutdown(); }
    }

    [HarmonyPatch]
    internal static class ChefGridLobbyPatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(LobbyFlowController), "Start")]
        static void Start(LobbyFlowController __instance) { ChefGridLobby.Attach(__instance); }

        [HarmonyPrefix, HarmonyPatch(typeof(ClientLobbyFlowController), "Update")]
        static void Update(ClientLobbyFlowController __instance) { ChefGridLobby.ConsumeLobbyButtons(__instance); }

        [HarmonyPrefix, HarmonyPatch(typeof(ClientLobbyFlowController), "ShowLeaveDialog")]
        static bool Leave() { return !ChefGridLobby.Blocking; }

        [HarmonyPrefix, HarmonyPatch(typeof(ClientLobbyFlowController), "OnLoadLevel")]
        static void LevelLoad() { ChefGridLobby.BeforeLevelLoad(); }

        [HarmonyPrefix, HarmonyPatch(typeof(ClientEmoteWheel), "Update")]
        static bool Emotes(ClientEmoteWheel __instance)
        {
            return !ChefGridLobby.Blocking || !__instance.get_m_emoteWheel().ForUI;
        }
    }
}
