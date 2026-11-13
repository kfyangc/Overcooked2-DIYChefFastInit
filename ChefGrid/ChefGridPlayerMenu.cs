using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
    [HarmonyPatch]
    internal static class ChefGridPlayerMenu
    {
        const UIPlayerMenuBehaviour.UIPlayerMenuOptions Option = (UIPlayerMenuBehaviour.UIPlayerMenuOptions)0x4449;
        const string ButtonName = "DIYChef_ChangeChef";
        static readonly FieldInfo menuField = AccessTools.Field(typeof(UIPlayerMenuBehaviour), "m_menu");
        static readonly FieldInfo optionsField = AccessTools.Field(typeof(UIPlayerMenuBehaviour), "m_menuOptions");
        static readonly FieldInfo hideField = AccessTools.Field(typeof(UIPlayerMenuBehaviour), "m_hideMenu");
        static readonly MethodInfo selected = AccessTools.Method(typeof(UIPlayerMenuBehaviour), "OnSelected");
        static readonly MethodInfo deselected = AccessTools.Method(typeof(UIPlayerMenuBehaviour), "OnDeselected");
        static readonly MethodInfo navigation = AccessTools.Method(typeof(UIPlayerMenuBehaviour), "UpdateNavigation");
        static readonly HashSet<UIPlayerMenuBehaviour> started = new HashSet<UIPlayerMenuBehaviour>();
        static readonly Dictionary<UIPlayerMenuBehaviour, T17Button> buttons = new Dictionary<UIPlayerMenuBehaviour, T17Button>();
        static string Label { get { return Localization.GetLanguage() == SupportedLanguages.Chinese ? "切换厨师" : "CHANGE CHEF"; } }

        internal static void Hide(UIPlayerMenuBehaviour card)
        {
            if (card.isActiveAndEnabled) card.HideMenu(true);
            var menu = (GameObject)menuField.GetValue(card);
            if (menu != null) menu.SetActive(false);
            var routine = (Coroutine)hideField.GetValue(card);
            if (routine != null) card.StopCoroutine(routine);
            hideField.SetValue(card, null);
            if (card.isActiveAndEnabled) navigation.Invoke(card, null);
        }

        internal static void HideAll(UIPlayerRootMenu players)
        {
            if (players == null) return;
            foreach (var card in players.UIPlayers) if (card != null) Hide(card);
            if (players.CachedEventSystem != null)
            {
                players.CachedEventSystem.ForceDeselectSelectionObject();
                players.CachedEventSystem.SetSelectedGameObject(null);
            }
        }

        internal static void Refresh(UIPlayerRootMenu players)
        {
            if (players == null) return;
            foreach (var card in players.UIPlayers)
            {
                if (card == null) continue;
                Prepare(card);
                if (card.MenuButton != null) card.MenuButton.interactable = card.CanBeInteractedWith();
            }
        }

        static void SetLabel(T17Button button)
        {
            if (button == null) return;
            foreach (var text in button.GetComponentsInChildren<Text>(true))
            {
                var localized = text as T17Text;
                if (localized != null)
                {
                    localized.m_NonLocalizedKeyboardText = "";
                    localized.SetNonLocalizedText(Label);
                }
                else text.text = Label;
            }
            button.m_bLocalizeTooltip = false;
            button.m_TooltipTag = Label;
        }

        // Always use the game's own template, layout and menu navigation.
        static void Prepare(UIPlayerMenuBehaviour card)
        {
            if (!started.Contains(card)) return;
            var menu = (GameObject)menuField.GetValue(card);
            var options = (List<UIPlayerMenuBehaviour.MenuOption>)optionsField.GetValue(card);
            if (menu == null || options == null || options.Count == 0) return;
            T17Button button;
            if (!buttons.TryGetValue(card, out button) || button == null)
            {
                var template = options.Find(x => x.m_button != null).m_button;
                if (template == null) return;
                var item = Object.Instantiate(template.gameObject, menu.transform);
                item.name = ButtonName;
                item.SetActive(false);
                button = item.GetComponent<T17Button>();
                button.OnButtonSelect = b => selected.Invoke(card, new object[] { b });
                button.OnButtonDeselect = b => deselected.Invoke(card, new object[] { b });
                button.OnButtonMove = null;
                button.OnButtonPointerEnter = null;
                button.OnButtonPointerExit = null;
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => ChefGridLobby.OpenFor(card));
                var layout = item.GetComponent<LayoutElement>() ?? item.AddComponent<LayoutElement>();
                layout.ignoreLayout = menu.GetComponent<LayoutGroup>() == null;
                options.Add(new UIPlayerMenuBehaviour.MenuOption { m_button = button, m_type = Option });
                buttons[card] = button;
            }
            bool visible = ChefGridLobby.CanShowFor(card.UserInfo);
            int index = options.FindIndex(x => x.m_button == button);
            var option = options[index];
            option.m_platformsActiveOn = visible ? int.MaxValue : 0;
            options[index] = option;
            button.gameObject.SetActive(visible);
            button.interactable = ChefGridLobby.CanOpenFor(card.UserInfo);
            if (card.m_rootMenu != null && card.m_rootMenu.CachedEventSystem != null)
                button.SetEventSystem(card.m_rootMenu.CachedEventSystem);
            SetLabel(button);
        }

        static void Arrange(UIPlayerMenuBehaviour card)
        {
            T17Button button;
            if (!buttons.TryGetValue(card, out button) || button == null || !button.gameObject.activeSelf) return;
            var menu = (GameObject)menuField.GetValue(card);
            if (menu.GetComponent<LayoutGroup>() != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)menu.transform);
                return;
            }
            RectTransform first = null, last = null;
            foreach (var option in (List<UIPlayerMenuBehaviour.MenuOption>)optionsField.GetValue(card))
            {
                var other = option.m_button;
                if (other == null || other == button || !other.gameObject.activeSelf) continue;
                if (first == null) first = (RectTransform)other.transform;
                last = (RectTransform)other.transform;
            }
            if (last != null)
            {
                var rect = (RectTransform)button.transform;
                rect.sizeDelta = last.sizeDelta;
                float direction = first != last && last.localPosition.y > first.localPosition.y ? 1 : -1;
                float height = Mathf.Max(last.rect.height, Mathf.Abs(last.sizeDelta.y));
                rect.localPosition = last.localPosition + new Vector3(0, direction * (height + 4), 0);
            }
            navigation.Invoke(card, null);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "Start")]
        static void Start(UIPlayerMenuBehaviour __instance)
        {
            started.Add(__instance);
            Prepare(__instance);
            if (__instance.MenuButton != null) __instance.MenuButton.interactable = __instance.CanBeInteractedWith();
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "UpdateMenuStructure")]
        static void BeforeStructure(UIPlayerMenuBehaviour __instance) { Prepare(__instance); }

        [HarmonyPostfix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "UpdateMenuStructure")]
        static void AfterStructure(UIPlayerMenuBehaviour __instance)
        {
            Prepare(__instance);
            Arrange(__instance);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "HasMenuOptions")]
        static void HasOptions(UIPlayerMenuBehaviour __instance, ref bool __result)
        {
            __result |= ChefGridLobby.CanShowFor(__instance.UserInfo);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "set_UserInfo")]
        static void Changed(UIPlayerMenuBehaviour __instance)
        {
            if (buttons.ContainsKey(__instance)) Prepare(__instance);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "ShowMenu")]
        static bool ShowMenu(UIPlayerMenuBehaviour __instance)
        {
            // Native avatar refreshes cache and restore submenu focus. Suppress that
            // restoration while the grid owns the root, including its closing keys.
            return !ChefGridLobby.BlocksMenus(__instance.m_rootMenu);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(InGamePauseMenu), "Show")]
        static void PauseShown(InGamePauseMenu __instance, bool __result)
        {
            if (!__result) return;
            // Cards may have started while the pause menu was hidden. Refresh their
            // available actions and name-bar interaction when the world map opens it.
            Refresh(ChefGridPlayerContext.PausePlayers(__instance));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ClientLobbyFlowController), "SetState")]
        static void LobbyChanged()
        {
            var lobby = LobbyFlowController.Instance;
            if (lobby != null) Refresh(lobby.m_uiPlayerRoot);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "OnDestroy")]
        static void Destroy(UIPlayerMenuBehaviour __instance) { started.Remove(__instance); buttons.Remove(__instance); }
    }
}
