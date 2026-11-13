using System;
using System.Collections.Generic;
using OC2DIYChef;

static class ChefGridEngagementRegression
{
    static int checks;
    static void Check(bool condition, string reason)
    {
        if (!condition) throw new Exception(reason);
        checks++;
    }

    internal static int Run()
    {
        checks = 0;
        // Model the separate native cancel bindings, not the grid's own button instances.
        // Both keyboard and controller slots, and both Unity Update orders, must work.
        for (int cancelSlot = 0; cancelSlot < 2; cancelSlot++)
            foreach (bool managerFirst in new[] { false, true })
            {
                ChefGridEngagementGuard.Shutdown();
                var manager = new GamepadEngagementManager();
                UnityEngine.Time.frameCount = 10;
                manager.Tick();
                ChefGridEngagementGuard.Open();
                Check(manager.Suppressor.IsSuppressed(), "Opening the grid suppresses native manual engagement immediately");
                manager.Buttons[cancelSlot].Down = true;
                if (managerFirst) manager.Tick();
                ChefGridEngagementGuard.Close();
                if (!managerFirst) manager.Tick();
                Check(manager.Players == 2 && manager.Suppressor.IsSuppressed(),
                    "Menu cancel cannot remove the secondary player in either Update order");
                for (int frame = 11; frame <= 15; frame++)
                {
                    UnityEngine.Time.frameCount = frame;
                    manager.Tick();
                    Check(manager.Players == 2 && manager.Suppressor.IsSuppressed(),
                        "Holding Esc/B after closing the grid must not become a leave request");
                }
                manager.Buttons[cancelSlot].Down = false;
                UnityEngine.Time.frameCount = 16;
                manager.Tick();
                Check(manager.Players == 2 && !manager.Suppressor.IsSuppressed(),
                    "Releasing the closing key restores native handling without dropping a player");
                manager.Buttons[cancelSlot].Down = true;
                UnityEngine.Time.frameCount = 17;
                manager.Tick();
                Check(manager.Players == 1, "A fresh leave press outside the grid retains original behavior");
            }

        ChefGridEngagementGuard.Shutdown();
        var waiting = new GamepadEngagementManager();
        waiting.Tick();
        ChefGridEngagementGuard.Open();
        waiting.Buttons[1].Down = true;
        for (int frame = 20; frame < 23; frame++)
        {
            UnityEngine.Time.frameCount = frame;
            waiting.Tick();
            Check(waiting.Players == 2, "A waiting player's cancel cannot bypass grid ownership and remove them");
        }
        // Other menus' suppression tokens must survive ours being released.
        var otherMenu = waiting.Suppressor.AddSuppressor(new object());
        ChefGridEngagementGuard.Close();
        waiting.Buttons[1].Down = false;
        waiting.Tick();
        Check(waiting.Suppressor.IsSuppressed(), "Even an already-released key remains protected through the closing frame");
        UnityEngine.Time.frameCount++;
        waiting.Tick();
        Check(!otherMenu.IsReleased() && waiting.Suppressor.IsSuppressed(), "Closing the grid preserves other menus' suppression");
        otherMenu.Release();
        waiting.Tick();
        Check(!waiting.Suppressor.IsSuppressed(), "Our token is released independently of another menu's token");

        ChefGridEngagementGuard.Open();
        ChefGridEngagementGuard.Close();
        ChefGridEngagementGuard.Open();
        UnityEngine.Time.frameCount++;
        waiting.Tick();
        Check(waiting.Suppressor.IsSuppressed(), "Reopening during the release guard keeps the new grid protected");
        waiting.Suppressor.Reset();
        waiting.Tick();
        Check(waiting.Suppressor.IsSuppressed(), "A native suppression reset is repaired while the grid remains open");
        ChefGridEngagementGuard.Shutdown();
        waiting.Tick();
        Check(!waiting.Suppressor.IsSuppressed(), "Plugin shutdown releases the grid's suppression token");
        return checks;
    }
}

// Small stand-ins for the native APIs exercised by the production engagement guard.
// The button claim and suppressor cleanup behavior follows the local game assembly.
interface ILogicalButton
{
    bool IsDown();
    bool HasUnclaimedPressEvent();
    bool HasUnclaimedReleaseEvent();
    void ClaimPressEvent();
    void ClaimReleaseEvent();
}

sealed class NativeCancelButton : ILogicalButton
{
    internal bool Down;
    bool pressClaimed = true, releaseClaimed = true;
    public bool IsDown()
    {
        pressClaimed = pressClaimed && Down;
        releaseClaimed = releaseClaimed && !Down;
        return Down;
    }
    public bool HasUnclaimedPressEvent() { return IsDown() && !pressClaimed; }
    public bool HasUnclaimedReleaseEvent() { return !IsDown() && !releaseClaimed; }
    public void ClaimPressEvent() { pressClaimed = true; }
    public void ClaimReleaseEvent() { releaseClaimed = true; }
    internal bool JustPressed()
    {
        if (!HasUnclaimedPressEvent()) return false;
        ClaimPressEvent();
        return true;
    }
}

sealed class Suppressor
{
    object owner;
    internal Suppressor(object value) { owner = value; }
    public bool IsReleased() { return owner == null; }
    public void Release() { owner = null; }
}

sealed class SuppressionController
{
    readonly List<Suppressor> tokens = new List<Suppressor>();
    public Suppressor AddSuppressor(object owner)
    {
        var token = new Suppressor(owner);
        tokens.Add(token);
        return token;
    }
    internal bool IsSuppressed() { return tokens.Count != 0; }
    internal void UpdateSuppressors() { tokens.RemoveAll(x => x.IsReleased()); }
    internal void Reset()
    {
        foreach (var token in tokens) token.Release();
        tokens.Clear();
    }
}

sealed class GamepadEngagementManager
{
    internal static GamepadEngagementManager Instance;
    internal readonly NativeCancelButton[] Buttons = { new NativeCancelButton(), new NativeCancelButton() };
    public readonly SuppressionController Suppressor = new SuppressionController();
    internal int Players = 2;
    internal GamepadEngagementManager() { Instance = this; }
    internal void Tick()
    {
        ChefGridEngagementGuard.BeforeUpdate(this, Buttons);
        if (!Suppressor.IsSuppressed())
            foreach (var button in Buttons)
                if (button.JustPressed()) Players--;
        Suppressor.UpdateSuppressors();
    }
}

namespace UnityEngine { static class Time { internal static int frameCount; } }
