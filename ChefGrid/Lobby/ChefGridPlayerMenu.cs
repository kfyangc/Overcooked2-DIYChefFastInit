using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
    // Independent native-template action and interaction, following the same
    // approach as HostUtilities without registering with or calling that mod.
    [HarmonyPatch]
    internal static class ChefGridPlayerMenu
    {
        const string ButtonName = "DIYChef_ChangeChef";
        static readonly FieldInfo menuField = AccessTools.Field(typeof(UIPlayerMenuBehaviour), "m_menu");
        static readonly FieldInfo optionsField = AccessTools.Field(typeof(UIPlayerMenuBehaviour), "m_menuOptions");
        static readonly FieldInfo hideField = AccessTools.Field(typeof(UIPlayerMenuBehaviour), "m_hideMenu");
        static readonly MethodInfo navigation = AccessTools.Method(typeof(UIPlayerMenuBehaviour), "UpdateNavigation");
        static readonly HashSet<UIPlayerMenuBehaviour> started = new HashSet<UIPlayerMenuBehaviour>();
        static readonly Dictionary<UIPlayerMenuBehaviour, Item> items = new Dictionary<UIPlayerMenuBehaviour, Item>();
        static string Label { get { return Localization.GetLanguage() == SupportedLanguages.Chinese ? "切换厨师" : "CHANGE CHEF"; } }

        sealed class Item
        {
            internal UIPlayerMenuBehaviour Card;
            internal GameObject Menu;
            internal RectTransform Row;
            internal T17Button Button, Toggle;
            internal bool Hovered, ToggleHovered;
            internal int OpenedFrame;
            internal T17EventSystem Domain { get { return Card.m_rootMenu == null ? null : Card.m_rootMenu.CachedEventSystem; } }
            internal void Select() { Button.SetEventSystem(Domain); StopHide(Card); RefreshNavigation(this); }
            internal void Click()
            {
                // Opening, hovering, focusing and refreshing never change a chef.
                // The click that opened the name menu cannot also open the grid.
                if (!Menu.activeInHierarchy || !Selectable(Button) || Time.frameCount <= OpenedFrame ||
                    Card.m_rootMenu != ChefGridPlayerContext.CurrentPlayers() || !ChefGridLobby.CanOpenFor(Card.UserInfo)) return;
                StopHide(Card);
                ChefGridLobby.OpenFor(Card);
            }
            internal void EnterToggle(T17Button _) { ToggleHovered = true; }
            internal void ExitToggle(T17Button _) { ToggleHovered = false; }
            internal bool HoldPointer
            {
                get { return Menu.activeInHierarchy && ((Hovered && Selectable(Button)) || (ToggleHovered && Selectable(Toggle))); }
            }
            internal void Detach()
            {
                if (Toggle == null) return;
                Toggle.OnButtonPointerEnter -= EnterToggle;
                Toggle.OnButtonPointerExit -= ExitToggle;
            }
        }

        sealed class Focus
        {
            internal Item Item;
            internal T17EventSystem Domain;
            internal GameObject Current;
            internal int Engagement, Split;
        }

        static void StopHide(UIPlayerMenuBehaviour card)
        {
            var routine = (Coroutine)hideField.GetValue(card);
            if (routine != null) card.StopCoroutine(routine);
            hideField.SetValue(card, null);
        }

        internal static void Hide(UIPlayerMenuBehaviour card)
        {
            if (card.isActiveAndEnabled) card.HideMenu(true);
            var menu = (GameObject)menuField.GetValue(card);
            if (menu != null) menu.SetActive(false);
            StopHide(card);
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

        internal static void UpdateFrame()
        {
            // Complete layout after all menu extensions, independent of postfix
            // registration order. This runs before the visible frame is rendered.
            foreach (var item in items.Values)
                if (item.Card != null && item.Menu != null && item.Menu.activeInHierarchy)
                {
                    Prepare(item.Card);
                    Arrange(item);
                    RefreshNavigation(item);
                }
        }

        static bool Visible(UIPlayerMenuBehaviour card)
        {
            return card.m_rootMenu == ChefGridPlayerContext.CurrentPlayers() && ChefGridLobby.CanShowFor(card.UserInfo);
        }

        static void SetLabel(T17Button button)
        {
            foreach (var text in button.GetComponentsInChildren<Text>(true))
            {
                var localized = text as T17Text;
                if (localized != null) { localized.m_NonLocalizedKeyboardText = ""; localized.SetNonLocalizedText(Label); }
                else text.text = Label;
            }
            button.m_bShowTooltip = true;
            button.m_bLocalizeTooltip = false;
            button.m_TooltipTag = Label;
        }

        static void Prepare(UIPlayerMenuBehaviour card)
        {
            if (!started.Contains(card)) return;
            bool visible = Visible(card);
            Item item;
            if (!items.TryGetValue(card, out item))
            {
                if (!visible) return;
                var menu = (GameObject)menuField.GetValue(card);
                var options = (List<UIPlayerMenuBehaviour.MenuOption>)optionsField.GetValue(card);
                if (menu == null || options == null) return;
                var template = options.Find(x => x.m_button != null).m_button;
                if (template == null) return;
                // Keep the template button inside a plain row. Extensions that
                // use direct native buttons as layout anchors must not mistake
                // our independent action for an original menu option.
                var rowObject = new GameObject("DIYChef_ChangeChefRow", typeof(RectTransform));
                rowObject.SetActive(false);
                var row = (RectTransform)rowObject.transform;
                row.SetParent(menu.transform, false);
                var templateRect = (RectTransform)template.transform;
                row.anchorMin = templateRect.anchorMin;
                row.anchorMax = templateRect.anchorMax;
                row.pivot = templateRect.pivot;
                var size = TemplateSize(template);
                row.sizeDelta = size;
                row.localPosition = templateRect.localPosition;
                var clone = Object.Instantiate(template.gameObject, row);
                clone.name = ButtonName;
                var buttonRect = (RectTransform)clone.transform;
                buttonRect.anchorMin = Vector2.zero;
                buttonRect.anchorMax = Vector2.one;
                buttonRect.sizeDelta = Vector2.zero;
                buttonRect.anchoredPosition = Vector2.zero;
                buttonRect.localPosition = Vector3.zero;
                clone.SetActive(true);
                item = new Item { Card = card, Menu = menu, Row = row, Button = clone.GetComponent<T17Button>(),
                    Toggle = card.MenuButton as T17Button, OpenedFrame = Time.frameCount };
                items.Add(card, item);
                var button = item.Button;
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(item.Click);
                button.OnButtonSelect = _ => item.Select();
                button.OnButtonDeselect = _ => card.HideMenu();
                button.OnButtonMove = null;
                button.OnButtonPointerEnter = _ => { item.Hovered = true; button.SetEventSystem(item.Domain); StopHide(card); };
                button.OnButtonPointerExit = _ => { item.Hovered = false; };
                if (item.Toggle != null)
                {
                    item.Toggle.OnButtonPointerEnter += item.EnterToggle;
                    item.Toggle.OnButtonPointerExit += item.ExitToggle;
                }
                var layout = rowObject.AddComponent<LayoutElement>();
                layout.ignoreLayout = menu.GetComponent<LayoutGroup>() == null;
                layout.preferredWidth = size.x;
                layout.preferredHeight = size.y;
            }
            item.Row.gameObject.SetActive(visible && item.Menu.activeSelf);
            item.Button.interactable = visible && ChefGridLobby.CanOpenFor(card.UserInfo);
            item.Button.SetEventSystem(item.Domain);
            SetLabel(item.Button);
        }

        static Vector2 TemplateSize(T17Button template)
        {
            var rect = (RectTransform)template.transform;
            var size = rect.rect.size;
            var parent = rect.parent;
            if (parent == null) return size;
            var grid = parent.GetComponent<GridLayoutGroup>();
            var nativeGrid = parent.GetComponent<T17GridLayoutGroup>();
            var cell = grid != null ? grid.cellSize : nativeGrid != null ? nativeGrid.cellSize : Vector2.zero;
            return new Vector2(cell.x > 0 ? cell.x : size.x, cell.y > 0 ? cell.y : size.y);
        }

        static void Arrange(Item item)
        {
            if (!item.Button.gameObject.activeInHierarchy) return;
            item.Row.SetAsLastSibling();
            if (item.Menu.GetComponent<LayoutGroup>() != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)item.Menu.transform);
                return;
            }
            RectTransform first = null, last = null;
            foreach (Transform child in item.Menu.transform)
            {
                if (child == item.Row || !child.gameObject.activeSelf ||
                    child.GetComponentsInChildren<T17Button>(false).Length == 0) continue;
                if (first == null) first = (RectTransform)child;
                last = (RectTransform)child;
            }
            if (last == null) return;
            var rect = item.Row;
            var template = ((List<UIPlayerMenuBehaviour.MenuOption>)optionsField.GetValue(item.Card))
                .Find(x => x.m_button != null).m_button;
            var size = TemplateSize(template);
            rect.sizeDelta = size;
            float direction = first != last && last.localPosition.y > first.localPosition.y ? 1 : -1;
            rect.localPosition = last.localPosition + new Vector3(0, direction * (size.y + 4), 0);
        }

        static bool Selectable(T17Button button) { return button != null && button.isActiveAndEnabled && button.IsInteractable(); }
        static Selectable Adjacent(List<List<T17Button>> rows, int row, float column, Selectable fallback)
        {
            return row < 0 || row >= rows.Count ? fallback : rows[row][Mathf.RoundToInt(column * (rows[row].Count - 1))];
        }
        static void Navigate(Item item)
        {
            if (item.Card.MenuButton == null || !item.Menu.activeInHierarchy) return;
            var rows = new List<List<T17Button>>();
            foreach (Transform child in item.Menu.transform)
            {
                var row = new List<T17Button>();
                foreach (var button in child.GetComponentsInChildren<T17Button>(false)) if (Selectable(button)) row.Add(button);
                if (row.Count > 0) rows.Add(row);
            }
            if (rows.Count == 0) return;
            var name = item.Card.MenuButton;
            var outer = name.navigation;
            outer.selectOnUp = Adjacent(rows, rows.Count - 1, 0.5f, name);
            name.navigation = outer;
            for (int i = 0; i < rows.Count; i++)
                for (int j = 0; j < rows[i].Count; j++)
                {
                    var nav = outer;
                    nav.mode = Navigation.Mode.Explicit;
                    float column = rows[i].Count > 1 ? (float)j / (rows[i].Count - 1) : 0.5f;
                    // The top action is an endpoint. Linking it back to the
                    // name bar would loop, because the name enters the last row.
                    nav.selectOnUp = Adjacent(rows, i - 1, column, null);
                    nav.selectOnDown = Adjacent(rows, i + 1, column, name);
                    nav.selectOnLeft = j > 0 ? rows[i][j - 1] : outer.selectOnLeft;
                    nav.selectOnRight = j + 1 < rows[i].Count ? rows[i][j + 1] : outer.selectOnRight;
                    rows[i][j].navigation = nav;
                }
        }
        static void RefreshNavigation(Item item) { navigation.Invoke(item.Card, null); Navigate(item); }

        [HarmonyPostfix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "Start")]
        static void Start(UIPlayerMenuBehaviour __instance) { started.Add(__instance); Refresh(__instance.m_rootMenu); }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(UIPlayerMenuBehaviour), "UpdateMenuStructure")]
        static void Structure(UIPlayerMenuBehaviour __instance)
        {
            Prepare(__instance);
            Item item;
            if (items.TryGetValue(__instance, out item)) { Arrange(item); RefreshNavigation(item); }
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(UIPlayerMenuBehaviour), "UpdateNavigation")]
        static void NavigationChanged(UIPlayerMenuBehaviour __instance)
        {
            Item item;
            if (items.TryGetValue(__instance, out item)) Navigate(item);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "HasMenuOptions")]
        static void HasOptions(UIPlayerMenuBehaviour __instance, ref bool __result) { __result |= Visible(__instance); }

        [HarmonyPostfix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "set_UserInfo")]
        static void Changed(UIPlayerMenuBehaviour __instance) { Prepare(__instance); }

        [HarmonyPrefix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "ShowMenu")]
        static bool ShowMenu(UIPlayerMenuBehaviour __instance)
        {
            if (ChefGridLobby.BlocksMenus(__instance.m_rootMenu)) return false;
            Item item;
            if (items.TryGetValue(__instance, out item))
            {
                item.Hovered = item.ToggleHovered = false;
                item.OpenedFrame = Time.frameCount;
                StopHide(__instance);
            }
            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "ToggleMenu")]
        static bool Toggle(UIPlayerMenuBehaviour __instance)
        {
            Item item;
            if (!items.TryGetValue(__instance, out item) || !item.Menu.activeSelf) return true;
            __instance.HideMenu(true);
            return false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "HideMenu")]
        static void Hiding(UIPlayerMenuBehaviour __instance, bool _force)
        {
            Item item;
            if (_force && items.TryGetValue(__instance, out item)) item.Hovered = item.ToggleHovered = false;
        }

        static IEnumerator GuardPointer(Item item, IEnumerator routine)
        {
            while (true)
            {
                if (item.HoldPointer) { yield return null; continue; }
                if (!routine.MoveNext()) yield break;
                yield return routine.Current;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "HideMenuRoutine")]
        static void HidingRoutine(UIPlayerMenuBehaviour __instance, bool _force, ref IEnumerator __result)
        {
            Item item;
            if (!_force && items.TryGetValue(__instance, out item)) __result = GuardPointer(item, __result);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "IsSelected")]
        static void IsSelected(UIPlayerMenuBehaviour __instance, ref bool __result)
        {
            Item item;
            if (!items.TryGetValue(__instance, out item) || !item.Menu.activeInHierarchy || item.Domain == null) return;
            var selected = item.Domain.GetPendingSelectedGameObject() ?? item.Domain.currentSelectedGameObject;
            __result |= selected != null && selected.activeInHierarchy && selected.transform.IsChildOf(item.Menu.transform);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIPlayerRootMenu), "OnUsersChanged")]
        static void BeforeUsers(UIPlayerRootMenu __instance, out Focus __state)
        {
            __state = null;
            var domain = __instance.CachedEventSystem;
            if (domain == null || ChefGridLobby.BlocksMenus(__instance)) return;
            var selected = domain.GetPendingSelectedGameObject() ?? domain.currentSelectedGameObject;
            foreach (var card in __instance.UIPlayers)
            {
                Item item;
                if (card == null || card.UserInfo == null || !items.TryGetValue(card, out item) ||
                    !item.Menu.activeInHierarchy || item.Button.gameObject != selected) continue;
                __state = new Focus { Item = item, Domain = domain, Current = domain.currentSelectedGameObject,
                    Engagement = (int)card.UserInfo.Engagement, Split = (int)card.UserInfo.Split };
                return;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIPlayerRootMenu), "OnUsersChanged")]
        static void AfterUsers(UIPlayerRootMenu __instance, Focus __state)
        {
            Refresh(__instance);
            if (__state == null || __state.Domain != __instance.CachedEventSystem || ChefGridLobby.BlocksMenus(__instance)) return;
            var item = __state.Item;
            var user = item.Card.UserInfo;
            if (user == null || !user.IsLocal || (int)user.Engagement != __state.Engagement || (int)user.Split != __state.Split ||
                !item.Menu.activeInHierarchy || !Selectable(item.Button) || item.Domain.IsDisabled()) return;
            var pending = item.Domain.GetPendingSelectedGameObject();
            var current = item.Domain.currentSelectedGameObject;
            var name = item.Card.MenuButton.gameObject;
            if ((pending == null || pending == name || pending == item.Button.gameObject) &&
                (current == null || current == name || current == item.Button.gameObject || current == __state.Current))
            {
                item.Button.Select();
                StopHide(item.Card);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(InGamePauseMenu), "Show")]
        static void PauseShown(InGamePauseMenu __instance, bool __result)
        {
            if (__result) Refresh(ChefGridPlayerContext.PausePlayers(__instance));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ClientLobbyFlowController), "SetState")]
        static void LobbyChanged()
        {
            var lobby = LobbyFlowController.Instance;
            if (lobby != null) Refresh(lobby.m_uiPlayerRoot);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIPlayerMenuBehaviour), "OnDestroy")]
        static void Destroy(UIPlayerMenuBehaviour __instance)
        {
            Item item;
            if (items.TryGetValue(__instance, out item)) item.Detach();
            items.Remove(__instance);
            started.Remove(__instance);
        }
    }
}
