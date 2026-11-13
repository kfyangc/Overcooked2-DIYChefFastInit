using System;
using OC2DIYChef;
using UnityEngine;
using UnityEngine.UI;

static class ChefGridPlayerMenuRegression
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    static UIPlayerMenuBehaviour Card(UIPlayerRootMenu root)
    {
        var card = new GameObject("Player").AddComponent<UIPlayerMenuBehaviour>();
        card.Initialize(root);
        MenuHooks.Call("Start", card);
        return card;
    }

    static T17Button Chef(UIPlayerMenuBehaviour card) { return card.Options[1].m_button; }

    static void Main()
    {
        var root = ChefGridLobby.Root = new UIPlayerRootMenu();
        var card = Card(root);
        card.ShowMenu();
        var chef = Chef(card);
        Check(card.Options.Count == 2 && chef.gameObject.name == "DIYChef_ChangeChef", "A native player card gets one chef action");
        Check(chef.GetComponent<T17Text>().text == "切换厨师" && !chef.m_bLocalizeTooltip,
            "The cloned native action has the exact label and tooltip");
        Check(chef.Domain == root.CachedEventSystem && card.NavigationCalls > 0, "The action uses the player's own native event domain and navigation");
        chef.Click();
        Check(ChefGridLobby.Opens == 1 && card.ProfileClicks == 0, "Cloning clears the template's original profile callback");
        chef.OnButtonSelect(chef);
        Check(card.Selections == 1, "The new option binds selection to its actual player card");
        for (int i = 0; i < 5; i++) { ChefGridPlayerMenu.Refresh(root); card.ShowMenu(); }
        Check(card.Options.Count == 2, "Repeated native structure refreshes do not duplicate the action");
        Check(((RectTransform)chef.transform).localPosition.y == -54, "The action is placed one native row after the profile");

        ChefGridLobby.Blocking = true;
        ChefGridPlayerMenu.Refresh(root);
        Check(!chef.interactable, "Avatar updates while the grid is open can temporarily disable the underlying action");
        root.CachedEventSystem.Current = chef.gameObject;
        root.CachedEventSystem.Pending = chef.gameObject;
        int navigationBeforeHide = card.NavigationCalls;
        ChefGridPlayerMenu.HideAll(root);
        Check(!card.Menu.activeSelf && card.HideRoutine == null, "Closing the owner menus is synchronous and cancels delayed native hide routines");
        Check(root.CachedEventSystem.Current == null && root.CachedEventSystem.Pending == null,
            "Closing clears both current focus and the native end-of-frame focus request");
        Check(card.NavigationCalls > navigationBeforeHide, "Synchronous close rebuilds native navigation after hiding its children");
        card.ShowMenu(); // Native cached-avatar focus restoration during the modal.
        Check(!card.Menu.activeSelf, "Avatar focus restoration cannot reopen the submenu while the grid owns it");
        chef.Click();
        Check(ChefGridLobby.Opens == 1, "A disabled menu action cannot re-enter the current selection");
        ChefGridLobby.Blocking = false;
        ChefGridPlayerMenu.Refresh(root);
        Check(chef.interactable && card.MenuButton.interactable, "Releasing modal ownership restores action and name-bar interaction");
        card.ShowMenu();
        chef.Click();
        Check(card.Menu.activeSelf && ChefGridLobby.Opens == 2, "A fresh name-menu click works again after confirmation");
        ChefGridPlayerMenu.HideAll(root);
        ChefGridPlayerMenu.Refresh(root);
        Check(!card.Menu.activeSelf, "Refreshing button availability never opens a closed submenu");

        ChefGridLobby.Visible = false;
        var arcadeRoot = new UIPlayerRootMenu();
        var arcade = Card(arcadeRoot);
        Check(!Chef(arcade).gameObject.activeSelf, "A card created before its lobby is ready starts with a hidden action");
        LobbyFlowController.Instance = new LobbyFlowController { m_uiPlayerRoot = arcadeRoot };
        ChefGridLobby.Root = arcadeRoot;
        ChefGridLobby.Visible = true;
        MenuHooks.Call("LobbyChanged");
        Check(Chef(arcade).gameObject.activeSelf && Chef(arcade).interactable,
            "Entering native arcade Setup refreshes cards that started in PreSetup");
        arcade.ShowMenu();
        Check(arcade.Options.Count == 2 && Chef(arcade).GetComponent<T17Text>().text == chef.GetComponent<T17Text>().text,
            "Arcade entries retain the shared native row label");

        ChefGridLobby.Visible = false;
        var mapRoot = new UIPlayerRootMenu();
        var map = Card(mapRoot);
        Check(!Chef(map).gameObject.activeSelf, "Map cards can be initialized while the pause menu is hidden");
        MenuHooks.Call("PauseShown", new InGamePauseMenu { Players = mapRoot }, true);
        Check(!Chef(map).gameObject.activeSelf,
            "Map pause has no extra name action; its native Change Chef action is used instead");
        ChefGridLobby.Visible = true;
        ChefGridPlayerMenu.Refresh(mapRoot);
        Check(!Chef(map).gameObject.activeSelf,
            "An active arcade context cannot inject its action into an unrelated pause root");

        var remote = Card(mapRoot);
        remote.UserInfo.IsLocal = false;
        MenuHooks.Call("Changed", remote);
        remote.ShowMenu();
        Check(!Chef(remote).gameObject.activeSelf, "The action stays hidden in a remote player's menu");
        card.gameObject.SetActive(false);
        var hides = card.HideCalls;
        ChefGridPlayerMenu.HideAll(root);
        ChefGridPlayerMenu.Refresh(root);
        Check(card.HideCalls == hides, "Inactive pause roots are cleaned without starting a native coroutine on an inactive card");
        card.gameObject.SetActive(true);
        ChefGridLobby.Root = root;
        Localization.Language = SupportedLanguages.English;
        ChefGridPlayerMenu.Refresh(root);
        Check(chef.GetComponent<T17Text>().text == "CHANGE CHEF", "Both contexts retain the same localized native label path");
        Console.WriteLine("PASS: " + checks + " production player-menu creation, refresh, closure and reopen assertions; no Unity/game process used.");
    }
}
