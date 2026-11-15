using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OC2DIYChef
{
    // Lives on the original customization dropdown, so visibility and layout
    // continue to follow the native tab rather than a separate overlay.
    internal sealed class ChefGridToggle : MonoBehaviour
    {
        T17Button choose, toggle;
        BaseMenuBehaviour menu;

        internal static void Install(FrontendRootMenu root)
        {
            foreach (var button in root.GetComponentsInChildren<T17Button>(true))
            {
                // Serialized name in the frontend bundle; independent of language.
                if (button.name != "CustomChef") continue;
                var parent = button.transform.parent;
                if (parent.GetComponent<ChefGridToggle>() != null) continue;
                var control = parent.gameObject.AddComponent<ChefGridToggle>();
                control.choose = button;
                control.menu = parent.GetComponent<BaseMenuBehaviour>();
                control.Build();
            }
        }

        void Build()
        {
            var clone = Object.Instantiate(choose.gameObject, choose.transform.parent);
            clone.name = "DIYChef_GridToggle";
            clone.transform.SetSiblingIndex(choose.transform.GetSiblingIndex() + 1);
            toggle = clone.GetComponent<T17Button>();
            // Replacing the event also removes the serialized OpenFrontendMenu
            // listener. RemoveAllListeners alone would leave that call intact.
            toggle.onClick = new Button.ButtonClickedEvent();
            toggle.onClick.AddListener(Toggle);
            toggle.OnButtonSelect = null;
            toggle.OnButtonDeselect = null;
            toggle.OnButtonMove = null;
            toggle.OnButtonPointerEnter = null;
            toggle.OnButtonPointerExit = null;
            clone.SetActive(true);
            var layout = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
            layout.preferredHeight = ((RectTransform)choose.transform).rect.height;
            if (menu != null) menu.AddAllowedSelectables(toggle);
            Refresh();
        }

        void Toggle()
        {
            ChefGridMenu.SetEnabled(!ChefGridMenu.Enabled);
            Refresh();
        }

        void LateUpdate() { Refresh(); }

        void Refresh()
        {
            if (toggle == null) return;
            bool chinese = Localization.GetLanguage() == SupportedLanguages.Chinese;
            string label = chinese ? "网格菜单：" + (ChefGridMenu.Enabled ? "开" : "关")
                : "CHEF GRID: " + (ChefGridMenu.Enabled ? "ON" : "OFF");
            foreach (var text in toggle.GetComponentsInChildren<Text>(true))
            {
                var localized = text as T17Text;
                if (localized != null)
                {
                    localized.m_NonLocalizedKeyboardText = "";
                    localized.SetNonLocalizedText(label);
                }
                else text.text = label;
                text.resizeTextForBestFit = true;
                text.resizeTextMaxSize = choose.GetComponent<Text>().fontSize;
                text.resizeTextMinSize = 14;
            }
            toggle.SetEventSystem(menu == null ? null : menu.CachedEventSystem);
            toggle.interactable = choose.interactable;
            // The tab panel may rebuild boundary links on Show. Repair only the
            // vertical pair, preserving native navigation to neighboring tabs.
            var original = choose.navigation;
            var navigation = original;
            original.mode = navigation.mode = Navigation.Mode.Explicit;
            original.selectOnDown = toggle;
            navigation.selectOnUp = choose;
            navigation.selectOnDown = null;
            choose.navigation = original;
            toggle.navigation = navigation;
            if (menu != null)
            {
                var border = menu.m_BorderSelectables;
                border.selectOnDown = toggle;
                menu.m_BorderSelectables = border;
            }
        }
    }

    [HarmonyPatch]
    internal static class ChefGridTogglePatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(T17FrontendFlow), "Start")]
        static void Start(T17FrontendFlow __instance)
        {
            if (__instance.m_Rootmenu != null) ChefGridToggle.Install(__instance.m_Rootmenu);
        }
    }
}
