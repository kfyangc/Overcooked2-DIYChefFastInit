using System;
using System.Reflection;
using OC2DIYChef;
using UnityEngine;

static class ChefGridWorldMapRegression
{
    static int checks;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }

    internal static object Hook(string method, params object[] args)
    {
        return typeof(ChefGridWorldMap).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    }

    static InGameCustomisation Menu(UIPlayerRootMenu root, FrontendChefCustomisation[] views)
    {
        var host = new GameObject("Native map customisation");
        host.SetActive(false);
        var menu = host.AddComponent<InGameCustomisation>();
        menu.Players = root;
        menu.Views = views;
        return menu;
    }

    internal static int Run()
    {
        checks = 0;
        var root = new UIPlayerRootMenu();
        var pause = new GameObject("Native map pause").AddComponent<InGamePauseMenu>();
        // The native screens may use separate roots; completion must still close
        // the actual pause flow instead of relying on a root-identity coincidence.
        pause.SetPlayers(new UIPlayerRootMenu());
        var flow = T17InGameFlow.Instance = new T17InGameFlow(pause);
        var views = new FrontendChefCustomisation[4];
        for (int i = 0; i < views.Length; i++)
        {
            views[i] = new GameObject("Native chef").AddComponent<FrontendChefCustomisation>();
            var user = Team17.Online.ClientUserSystem.m_Users._items[i];
            if (user.IsLocal) views[i].Activate(new PlayerGameInput((ControlPadInput.PadNum)user.Engagement,
                user.PadSide, new AmbiControlsMappingData()), user.SelectedChefData);
        }
        var menu = Menu(root, views);
        Check(menu.Show(null, null, null, false), "Native Change Chef shows normally before attaching the grid");
        var grid = menu.GetComponent<ChefGridMenu>();
        var owner = ChefGridLobby.Attached;
        Check(grid.IsOpen && grid.Views == views && owner.Players == root && owner.Begins == 1,
            "The native entry uses its existing chef views and the shared grid/input owner");
        Check(views[0].IsActive() && !views[1].IsActive() && views[2].IsActive() && views[3].IsActive(),
            "Native map selection retains every local player and excludes the remote player");
        menu.TickNative();
        Check(grid.Ticks == 1 && menu.CarouselUpdates == 0,
            "The grid owns Update so native carousel confirmation cannot bypass sequential selection");
        grid.Cancel();
        Check(menu.Reverts == 1 && menu.HideCalls == 1 && !grid.IsOpen && !menu.gameObject.activeSelf,
            "Cancel invokes the native revert and hide transaction exactly once");
        Check(owner.Ends == 1 && owner.Commits == 0 && owner.Session.Blocking && flow.CloseRequests == 0,
            "Cancel keeps the pause and protects the closing input until release");
        Check(ChefSelectionMenu.Released == menu.Customiser, "Successful hide releases the native DIY selection snapshot");
        Hook("Destroy", menu, menu.Customiser);
        Check(owner.Ends == 1, "Destruction after Hide cannot end an already released transaction again");

        menu.Show(null, null, null, false);
        owner = ChefGridLobby.Attached;
        menu.RefuseHide = true;
        ChefGridWorldMap.Commit(menu);
        Check(grid.IsOpen && owner.Commits == 0 && owner.Ends == 0,
            "A failed native Hide cannot prematurely commit or release the grid");
        menu.RefuseHide = false;
        grid.Accept();
        Check(owner.Commits == 1 && owner.Ends == 1 && menu.Reverts == 1 && !grid.IsOpen &&
            flow.CloseRequests == 1 && !pause.gameObject.activeSelf,
            "Confirm commits through native Hide, closes the pause and never reverts the chosen chefs");
        menu.TickNative();
        Check(owner.Commits == 1 && owner.Ends == 1,
            "The finishing frame cannot commit or release twice");

        menu.Show(null, null, null, false);
        owner = ChefGridLobby.Attached;
        grid.FailTick = true;
        int hides = menu.HideCalls;
        menu.TickNative();
        Check(menu.HideCalls == hides + 1 && !menu.gameObject.activeSelf && owner.Ends == 1,
            "Loss of the active grid closes native customisation and releases its input ownership");

        ChefGridMenu.FailOpen = true;
        menu.Show(null, null, null, false);
        owner = ChefGridLobby.Attached;
        menu.TickNative();
        Check(!grid.IsOpen && menu.CarouselUpdates > 0 && owner.Begins == 1,
            "Grid initialization failure leaves the original carousel usable without taking new ownership");
        menu.Hide();
        ChefGridMenu.FailOpen = false;

        ChefGridMenu.Enabled = false;
        var disabled = Menu(root, views);
        disabled.Show(null, null, null, false);
        disabled.TickNative();
        Check(disabled.GetComponent<ChefGridMenu>() == null && disabled.CarouselUpdates == 1,
            "Disabling the grid preserves the original map Change Chef screen");
        disabled.Hide();
        ChefGridMenu.Enabled = true;
        Hook("Show", menu, false, root, views);
        Check(!grid.IsOpen, "A rejected native Show does not open a detached grid");

        menu.Show(null, null, null, false);
        owner = ChefGridLobby.Attached;
        Hook("Destroy", menu, menu.Customiser);
        Check(!grid.IsOpen && owner.Ends == 1,
            "Destroying a live native menu closes the grid and ends input ownership");
        T17InGameFlow.Instance = null;
        return checks;
    }
}

public sealed class ChefCustomiser { }
public sealed class InGameCustomisation : InGameMenuBehaviour
{
    public UIPlayerRootMenu Players;
    public FrontendChefCustomisation[] Views;
    public readonly ChefCustomiser Customiser = new ChefCustomiser();
    public int Reverts, HideCalls, CarouselUpdates;
    public bool RefuseHide;
    public override bool Show(GamepadUser gamer, BaseMenuBehaviour parent, GameObject invoker, bool hideInvoker)
    {
        bool shown = base.Show(gamer, parent, invoker, hideInvoker);
        ChefGridWorldMapRegression.Hook("Show", this, shown, Players, Views);
        return shown;
    }
    public override bool Hide(bool restoreInvokerState = true, bool isTabSwitch = false)
    {
        HideCalls++;
        bool hidden = !RefuseHide && base.Hide(restoreInvokerState, isTabSwitch);
        ChefGridWorldMapRegression.Hook("Hide", this, hidden, Customiser);
        return hidden;
    }
    public override void Close() { Reverts++; Hide(); }
    public void TickNative()
    {
        if ((bool)ChefGridWorldMapRegression.Hook("Update", this)) CarouselUpdates++;
    }
}
