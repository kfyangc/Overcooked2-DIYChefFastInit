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
    static UIPlayerMenuBehaviour Card(UIPlayerRootMenu root, User user = null)
    {
        var card = new GameObject("Player").AddComponent<UIPlayerMenuBehaviour>();
        card.Initialize(root);
        if (user != null) card.UserInfo = user;
        MenuHooks.Call("Start", card);
        return card;
    }
    static T17Button Chef(UIPlayerMenuBehaviour card)
    {
        foreach (var button in card.Menu.GetComponentsInChildren<T17Button>(true))
            if (button.gameObject.name == "DIYChef_ChangeChef") return button;
        return null;
    }
    static UIPlayerRootMenu Root()
    {
        ChefGridLobby.Visible = true; ChefGridLobby.Blocking = false;
        return ChefGridLobby.Root = new UIPlayerRootMenu();
    }
    static float RowY(T17Button button) { return button.transform.parent.localPosition.y; }
    static void NoSideEffect(UIPlayerMenuBehaviour card, int opens, uint avatar, string reason)
    {
        Check(ChefGridLobby.Opens == opens && card.ProfileClicks == 0 &&
            card.UserInfo.SelectedChefAvatar == avatar && ChefGridLobby.AvatarMessages == 0, reason);
    }

    static void NameAndAction()
    {
        var root = Root();
        var card = Card(root);
        var original = card.Options[0];
        var chef = Chef(card);
        int opens = ChefGridLobby.Opens;
        uint avatar = card.UserInfo.SelectedChefAvatar;
        Check(chef != null && !chef.gameObject.activeInHierarchy, "A hidden name submenu owns one inactive action");
        Check(card.Options.Count == 1 && card.Options[0].m_button == original.m_button &&
            card.Options[0].m_type == original.m_type && card.Options[0].m_platformsActiveOn == original.m_platformsActiveOn,
            "The extension leaves the native options list and its entries unchanged");
        Check(chef.transform.parent != card.Menu.transform && chef.transform.parent.GetComponent<T17Button>() == null,
            "The standalone action is nested in a plain row, outside native-button anchor scans");
        Check(chef.GetComponent<T17Text>().text == "切换厨师" && chef.m_bShowTooltip && !chef.m_bLocalizeTooltip,
            "The template supplies a properly labelled localized action and tooltip");
        Time.frameCount++;
        card.MenuButton.Enter();
        card.MenuButton.Click();
        Check(card.Menu.activeSelf && card.NameClicks == 1 && chef.gameObject.activeInHierarchy,
            "The original name click opens the submenu and exposes the action");
        chef.Click();
        NoSideEffect(card, opens, avatar, "The opening click cannot also activate the new action in the same frame");
        chef.Enter(); chef.Focus(); chef.Exit();
        for (int i = 0; i < 5; i++) { card.Structure(); ChefGridPlayerMenu.Refresh(root); ChefGridPlayerMenu.UpdateFrame(); }
        NoSideEffect(card, opens, avatar, "Hover, selection, native structure and late refresh never open the grid or change a chef");
        Check(card.Options.Count == 1 && card.Menu.GetComponentsInChildren<T17Button>(true).Length == 2,
            "Repeated refresh never appends native entries or duplicates the action");
        Check(chef.Domain == root.CachedEventSystem && RowY(chef) == -54,
            "The action binds the native root domain and fits the next row after the profile");
        Time.frameCount++;
        chef.Click();
        Check(ChefGridLobby.Opens == opens + 1 && ChefGridLobby.OpenedFor == card &&
            card.ProfileClicks == 0 && card.UserInfo.SelectedChefAvatar == avatar,
            "Only a later explicit action click opens the target grid, without invoking the template profile or changing the chef");
        card.MenuButton.Click();
        Check(!card.Menu.activeSelf && card.NameClicks == 2,
            "A second original name click closes the menu without requiring another pointer movement");
        Time.frameCount++;
        card.MenuButton.Click();
        chef.Click();
        Check(ChefGridLobby.Opens == opens + 1,
            "Reopening resets the same-frame click guard instead of inheriting an old opening timestamp");
        ChefGridPlayerMenu.HideAll(root);
        Time.frameCount++;
        chef.onClick.Invoke();
        Check(ChefGridLobby.Opens == opens + 1, "An inactive action rejects a stale callback even when invoked directly");
        Localization.Language = SupportedLanguages.English;
        ChefGridPlayerMenu.Refresh(root);
        Check(chef.GetComponent<T17Text>().text == "CHANGE CHEF", "The independent action follows language refresh");
        Localization.Language = SupportedLanguages.Chinese;
    }

    static void PointerAndSelection()
    {
        var root = Root();
        var card = Card(root);
        Time.frameCount++;
        card.ShowMenu();
        var chef = Chef(card);
        var events = root.CachedEventSystem;
        events.Current = new GameObject("Outside");
        events.Pending = null;
        chef.Enter();
        card.HideMenu();
        var held = card.HideRoutine;
        for (int i = 0; i < 5; i++) Check(held.Step() && card.Menu.activeSelf,
            "The production coroutine wrapper keeps the submenu while its action is hovered");
        chef.Exit(); held.Step(); held.Step();
        Check(!card.Menu.activeSelf, "Leaving the action lets the original hide routine finish");
        card.ShowMenu();
        card.MenuButton.Enter();
        card.HideMenu();
        held = card.HideRoutine;
        Check(held.Step() && card.Menu.activeSelf, "Name-bar hover also holds the menu through native delayed hiding");
        card.HideMenu(true);
        Check(!card.Menu.activeSelf, "Force-hide bypasses pointer protection and closes immediately");
        card.ShowMenu();
        chef.Enter();
        chef.interactable = false;
        card.HideMenu();
        held = card.HideRoutine; held.Step(); held.Step();
        Check(!card.Menu.activeSelf, "A disabled action cannot keep an old hover alive and block native hiding");
        card.ShowMenu();
        chef.Enter();
        ChefGridLobby.Visible = false; ChefGridPlayerMenu.Refresh(root);
        card.HideMenu();
        held = card.HideRoutine; held.Step(); held.Step();
        Check(!card.Menu.activeSelf, "A hidden action stops protecting its previous pointer hover without waiting for PointerExit");
        ChefGridLobby.Visible = true;
        card.ShowMenu();
        card.HideMenu();
        held = card.HideRoutine;
        chef.Focus();
        Check(held.Stopped && card.HideRoutine == null, "Selecting the action stops and clears the native hide coroutine");
        events.Pending = chef.gameObject;
        Check(card.IsSelected(), "A queued action selection keeps the submenu before end-of-frame focus changes");
        events.Pending = null; events.Current = chef.gameObject;
        Check(card.IsSelected(), "A current action selection is recognized inside its nested row");
        chef.gameObject.SetActive(false);
        Check(!card.IsSelected(), "Inactive descendants cannot keep a menu selected");
        ChefGridPlayerMenu.Refresh(root);
        Check(ChefGridLobby.AvatarMessages == 0, "Pointer and selection maintenance send no chef messages");
        ChefGridPlayerMenu.HideAll(root);
    }

    // Models HU's relevant layout contract: only direct native buttons anchor
    // its extra normal button and nested chef-arrow row. No HU DLL is loaded.
    sealed class ExtensionRows
    {
        internal readonly T17Button Extra, Left, Center, Right;
        internal readonly GameObject Arrows;
        internal ExtensionRows(UIPlayerMenuBehaviour card)
        {
            Extra = Button(card.Menu.transform, "External action");
            Arrows = new GameObject("External chef row"); Arrows.transform.SetParent(card.Menu.transform);
            Left = Button(Arrows.transform, "Previous chef");
            Center = Button(Arrows.transform, "Chef label"); Center.interactable = false;
            Right = Button(Arrows.transform, "Next chef");
            Left.onClick.AddListener(() => ChefGridLobby.ExternalChange(card.UserInfo));
            Right.onClick.AddListener(() => ChefGridLobby.ExternalChange(card.UserInfo));
            Right.transform.localPosition = new Vector3(100, 0, 0);
        }
        static T17Button Button(Transform parent, string name)
        {
            var value = new GameObject(name).AddComponent<T17Button>();
            value.transform.SetParent(parent);
            return value;
        }
        internal void Refresh(UIPlayerMenuBehaviour card)
        {
            Transform last = null;
            foreach (Transform child in card.Menu.transform)
                if (child.gameObject.activeSelf && child.GetComponent<T17Button>() != null && child != Extra.transform) last = child;
            if (last == null) throw new Exception("The native layout anchor was lost");
            Extra.transform.SetAsLastSibling();
            Arrows.transform.SetAsLastSibling();
            Extra.transform.localPosition = last.localPosition + new Vector3(0, -54, 0);
            Arrows.transform.localPosition = Extra.transform.localPosition + new Vector3(0, -54, 0);
            // An extension can also overwrite navigation after our postfix.
            var nav = card.MenuButton.navigation; nav.selectOnUp = Right; card.MenuButton.navigation = nav;
        }
    }

    static void CoexistingLayouts()
    {
        foreach (bool after in new[] { false, true })
        {
            var root = Root();
            var card = Card(root);
            var extra = new ExtensionRows(card);
            card.OtherRunsAfter = after;
            card.OtherStructure = extra.Refresh;
            Time.frameCount++;
            card.ShowMenu();
            var chef = Chef(card);
            for (int refresh = 0; refresh < 40; refresh++)
            {
                card.Structure(); ChefGridPlayerMenu.UpdateFrame();
                Check(extra.Extra.transform.localPosition.y == -54 && extra.Arrows.transform.localPosition.y == -108 && RowY(chef) == -162,
                    "Repeated native and extension refreshes settle to distinct rows without cumulative drift, regardless of postfix order");
                Check(card.MenuButton.navigation.selectOnUp == chef && chef.navigation.selectOnUp == extra.Left &&
                    extra.Right.navigation.selectOnDown == chef && extra.Left.navigation.selectOnRight == extra.Right &&
                    extra.Right.navigation.selectOnLeft == extra.Left && extra.Left.navigation.selectOnUp == extra.Extra,
                    "Late navigation connects native, normal-extension, nested-arrow and grid-action rows");
                Check(card.Options.Count == 1 && card.Options[0].m_button.transform.localPosition.y == 0,
                    "Other extensions still see the unchanged native option and stable original layout anchor");
            }
            int opens = ChefGridLobby.Opens;
            uint avatar = card.UserInfo.SelectedChefAvatar;
            card.MenuButton.Click(); Time.frameCount++; card.MenuButton.Click();
            NoSideEffect(card, opens, avatar, "Names open and close safely alongside another extension's live chef arrow callbacks");
            ChefGridPlayerMenu.HideAll(root);
        }
    }

    static void FocusRestore()
    {
        var root = Root();
        var card = Card(root);
        Time.frameCount++; card.ShowMenu();
        var chef = Chef(card);
        var events = root.CachedEventSystem;
        events.Current = new GameObject("Old current"); events.Pending = chef.gameObject;
        root.NotifyUsers(() => { events.Pending = card.MenuButton.gameObject; events.Current = card.MenuButton.gameObject; });
        Check(events.Pending == chef.gameObject, "A pending action selection survives the native users refresh to the name bar");
        events.Pending = null; events.Current = chef.gameObject;
        root.NotifyUsers(() => { events.Pending = card.MenuButton.gameObject; events.Current = null; });
        Check(events.Pending == chef.gameObject, "Current action focus is restored after native selection refresh");
        var elsewhere = new GameObject("Another player choice");
        events.Pending = chef.gameObject;
        root.NotifyUsers(() => { events.Pending = elsewhere; events.Current = card.MenuButton.gameObject; });
        Check(events.Pending == elsewhere, "A new pending selection made by another caller is never stolen");
        events.Pending = chef.gameObject; events.Current = chef.gameObject;
        root.NotifyUsers(() => { events.Pending = null; events.Current = elsewhere; });
        Check(events.Current == elsewhere && events.Pending == null, "A newly selected outside object is not overwritten");
        events.Pending = chef.gameObject; events.Current = chef.gameObject;
        root.NotifyUsers(() => { card.UserInfo = new User { Engagement = 3, Split = 1 }; events.Pending = card.MenuButton.gameObject; });
        Check(events.Pending == card.MenuButton.gameObject, "Replacing the card with a different player identity does not restore the old player's focus");
        events.Pending = chef.gameObject;
        var old = card.UserInfo;
        root.NotifyUsers(() => { card.UserInfo = new User { Engagement = old.Engagement, Split = old.Split }; events.Pending = card.MenuButton.gameObject; });
        Check(events.Pending == chef.gameObject, "A refreshed user object with the same stable local identity retains action focus");
        events.Pending = chef.gameObject;
        root.NotifyUsers(() => { events.Disabled = true; events.Pending = null; });
        Check(events.Pending == null, "A disabled event domain cannot receive restored action focus");
        events.Disabled = false;
        events.Pending = chef.gameObject;
        root.NotifyUsers(() => { root.CachedEventSystem = new T17EventSystem(); });
        Check(root.CachedEventSystem.Pending == null, "Changing event domains does not restore a stale-domain selection");
        Check(ChefGridLobby.AvatarMessages == 0, "User refresh and stable-identity focus restoration never send chef messages");
        ChefGridPlayerMenu.HideAll(root);
    }

    static void ParentLayouts()
    {
        for (int mode = 0; mode < 3; mode++)
        {
            var root = Root();
            var card = new GameObject("Layout-managed player").AddComponent<UIPlayerMenuBehaviour>();
            card.Initialize(root);
            var native = (RectTransform)card.Options[0].m_button.transform;
            Vector2 expected;
            if (mode == 0)
            {
                card.Menu.AddComponent<LayoutGroup>();
                expected = native.sizeDelta = new Vector2(270, 62);
            }
            else
            {
                // Hidden Grid menus may not have assigned a rectangle to their
                // original template yet. The configured grid cell still exists.
                native.sizeDelta = Vector2.zero;
                expected = new Vector2(310, 68);
                if (mode == 1) card.Menu.AddComponent<GridLayoutGroup>().cellSize = expected;
                else card.Menu.AddComponent<T17GridLayoutGroup>().cellSize = expected;
            }
            MenuHooks.Call("Start", card);
            var chef = Chef(card);
            var row = (RectTransform)chef.transform.parent;
            int rebuilds = LayoutRebuilder.Rebuilds;
            Time.frameCount++; card.ShowMenu(); ChefGridPlayerMenu.UpdateFrame();
            Check(LayoutRebuilder.Rebuilds > rebuilds && !row.GetComponent<LayoutElement>().ignoreLayout,
                "A parent layout owns the new row instead of competing with manual positioning");
            var bounds = ((RectTransform)chef.transform).rect;
            Check(row.rect.width == expected.x && row.rect.height == expected.y &&
                bounds.width == expected.x && bounds.height == expected.y,
                "The action fills its allotted native row, including unmeasured standard/native Grid templates");
            if (mode > 0)
            {
                expected = new Vector2(380, 81);
                if (mode == 1) card.Menu.GetComponent<GridLayoutGroup>().cellSize = expected;
                else card.Menu.GetComponent<T17GridLayoutGroup>().cellSize = expected;
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)card.Menu.transform);
            }
            else
            {
                row.GetComponent<LayoutElement>().preferredWidth = expected.x = 380;
                row.GetComponent<LayoutElement>().preferredHeight = expected.y = 81;
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)card.Menu.transform);
            }
            bounds = ((RectTransform)chef.transform).rect;
            Check(bounds.width == expected.x && bounds.height == expected.y,
                "A later parent layout resize resizes the action rather than preserving a shrunken template rectangle");
            ChefGridPlayerMenu.HideAll(root);
        }
    }

    static void VisibilityAndOwnership()
    {
        var root = Root();
        var remote = Card(root, new User { IsLocal = false });
        remote.ShowMenu();
        Check(Chef(remote) == null && remote.Options.Count == 1, "Remote players never allocate a grid action or modify native options");
        var mapRoot = new UIPlayerRootMenu();
        var map = Card(mapRoot);
        MenuHooks.Call("PauseShown", new InGamePauseMenu { Players = mapRoot }, true);
        map.ShowMenu();
        Check(Chef(map) == null, "Map names never allocate the removed action, including when the native pause opens");
        ChefGridLobby.Visible = false;
        var pending = Card(root);
        Check(Chef(pending) == null, "A card created before the arcade is ready allocates no action");
        ChefGridLobby.Visible = true;
        LobbyFlowController.Instance = new LobbyFlowController { m_uiPlayerRoot = root };
        MenuHooks.Call("LobbyChanged");
        Check(Chef(pending) != null && !Chef(pending).gameObject.activeInHierarchy,
            "Arcade setup refresh creates the independent action while keeping the menu closed");
        Time.frameCount++; pending.ShowMenu();
        var chef = Chef(pending);
        var events = root.CachedEventSystem;
        events.Current = events.Pending = chef.gameObject;
        ChefGridLobby.Blocking = true;
        ChefGridPlayerMenu.Refresh(root);
        Check(!chef.interactable, "Modal input ownership disables the underlying action");
        root.NotifyUsers(() => { events.Pending = null; });
        Check(events.Pending == null, "Avatar refresh cannot restore underlying action focus during modal ownership");
        ChefGridPlayerMenu.HideAll(root);
        Check(!pending.Menu.activeSelf && pending.HideRoutine == null && events.Current == null && events.Pending == null,
            "Modal close synchronously hides submenus and clears current, queued focus and hide coroutine");
        pending.ShowMenu();
        Check(!pending.Menu.activeSelf, "Native cached focus cannot reopen a submenu while the closing keys remain owned");
        ChefGridLobby.Blocking = false; ChefGridPlayerMenu.Refresh(root);
        Check(!pending.Menu.activeSelf && chef.interactable, "Input release restores the action without reopening the menu");
        Time.frameCount++; pending.MenuButton.Click(); Time.frameCount++; int opens = ChefGridLobby.Opens; chef.Click();
        Check(ChefGridLobby.Opens == opens + 1 && ChefGridLobby.OpenedFor == pending,
            "After key release a fresh name/action interaction opens the correct player again");
        pending.gameObject.SetActive(false);
        int hides = pending.HideCalls;
        ChefGridPlayerMenu.HideAll(root);
        Check(pending.HideCalls == hides, "Cleaning an inactive card starts no coroutine on its inactive object");
        pending.gameObject.SetActive(true);
        int handlers = pending.MenuButton.OnButtonPointerEnter.GetInvocationList().Length;
        MenuHooks.Call("Destroy", pending);
        Check(pending.MenuButton.OnButtonPointerEnter.GetInvocationList().Length == handlers - 1,
            "Destroy removes only this action's toggle-hover handler");
        pending.MenuButton.Enter();
        Check(pending.NativePointerEnters > 0, "Destroy preserves unrelated native toggle handlers");
    }

    static void Main()
    {
        NameAndAction(); PointerAndSelection(); CoexistingLayouts(); FocusRestore(); ParentLayouts(); VisibilityAndOwnership();
        Console.WriteLine("PASS: " + checks + " production standalone-menu event, pointer, focus, layout-order and modal-ownership assertions; no Unity/game process used.");
    }
}
