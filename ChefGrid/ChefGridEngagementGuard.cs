using UnityEngine;

namespace OC2DIYChef
{
    // The engagement manager owns separate cancel buttons, so claiming the grid's
    // buttons alone cannot stop Esc/B from removing a secondary local player.
    internal static class ChefGridEngagementGuard
    {
        static GamepadEngagementManager manager;
        static Suppressor suppression;
        static bool open, pending;
        static int closedFrame;

        internal static void Open()
        {
            open = pending = true;
            Acquire(GameUtils.RequestManager<GamepadEngagementManager>());
        }

        internal static void Close()
        {
            if (!open) return;
            open = false;
            closedFrame = Time.frameCount;
        }

        internal static void BeforeUpdate(GamepadEngagementManager instance, ILogicalButton[] cancelButtons)
        {
            if (!pending) return;
            Acquire(instance);
            bool held = false;
            if (cancelButtons != null)
                foreach (var button in cancelButtons)
                {
                    if (button == null) continue;
                    held |= button.IsDown();
                    // Keep the actual native button history current while suppressed.
                    // Otherwise a held menu-cancel key looks like a fresh leave request
                    // when engagement handling resumes after the menu closes.
                    button.HasUnclaimedPressEvent();
                    button.ClaimPressEvent();
                    button.HasUnclaimedReleaseEvent();
                    button.ClaimReleaseEvent();
                }
            // Guard the entire closing frame regardless of Unity Update ordering,
            // and retain ownership until every native cancel binding is released.
            if (!open && !held && Time.frameCount > closedFrame)
            {
                pending = false;
                Release();
            }
        }

        static void Acquire(GamepadEngagementManager instance)
        {
            if (instance == null) return;
            if (manager != instance) Release();
            if (suppression != null && !suppression.IsReleased()) return;
            manager = instance;
            // The manager outlives a disabled/destroyed chef menu. Release explicitly
            // after the closing key is up instead of using the menu as the owner.
            suppression = instance.Suppressor.AddSuppressor(instance);
        }

        static void Release()
        {
            if (suppression != null) suppression.Release();
            suppression = null;
            manager = null;
        }

        internal static void Shutdown()
        {
            open = pending = false;
            Release();
        }
    }
}
