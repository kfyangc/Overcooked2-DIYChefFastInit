using System;
using OC2DIYChef;

static class ChefGridInteractionRegression
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
        var first = new ChefGridControls();
        var second = new ChefGridControls();
        Check(!first.Poll(-1, true, false, 0, true).Accept && !second.Poll(-1, true, false, 0, true).Accept,
            "First observation consumes opening input even when the caller already enables the menu");
        Check(!first.Poll(-1, true, false, 1, true).Accept, "Holding the key that opened the menu cannot confirm immediately");
        first.Poll(-1, false, false, 2, true);
        second.Poll(-1, false, false, 2, false);
        var e1 = first.Poll(-1, true, false, 3, true);
        var e2 = second.Poll(-1, true, false, 3, false);
        Check(e1.Accept && !e2.Accept, "Only the active player receives a shared keyboard confirmation");
        Check(!second.Poll(-1, true, false, 4, true).Accept,
            "Holding the first player's confirm must not also confirm the next player");
        second.Poll(-1, false, false, 5, true);
        Check(second.Poll(-1, true, false, 6, true).Accept, "The next fresh press confirms the second player");
        Check(!second.Poll(-1, true, false, 7, true).Accept, "Confirmation does not repeat while held");
        Check(second.Poll(-1, false, true, 8, true).Cancel, "Cancellation also uses a fresh mapped press");
        Check(!second.Poll(-1, false, true, 9, true).Cancel, "Held cancellation cannot repeat");
        second.Poll(-1, false, false, 10, false);
        Check(!second.Poll(-1, true, false, 11, false).Accept &&
            !second.Poll(-1, true, false, 12, true).Accept, "A press during a dialog is consumed without confirming after it closes");

        var movement = new ChefGridControls();
        movement.Poll(-1, false, false, -1, true);
        Check(movement.Poll(3, false, false, 0, true).Direction == 3, "Movement has no camera-animation gate");
        Check(movement.Poll(3, false, false, 0.2f, true).Direction == -1, "Held direction observes the initial repeat delay");
        Check(movement.Poll(3, false, false, 0.36f, true).Direction == 3 &&
            movement.Poll(3, false, false, 0.4f, true).Direction == -1 &&
            movement.Poll(3, false, false, 0.47f, true).Direction == 3, "Directional repeat uses the subsequent shorter interval");
        Check(movement.Poll(2, false, false, 0.48f, true).Direction == 2, "Changing direction responds immediately");
        Check(movement.Poll(1, false, false, 1, false).Direction == -1 &&
            movement.Poll(1, false, false, 2, true).Direction == -1, "A waiting player's held stick cannot move the viewport on handoff");
        movement.Poll(-1, false, false, 3, true);
        Check(movement.Poll(1, false, false, 4, true).Direction == 1, "Neutral then move arms the next player's direction");

        var bound = new ChefGridControls();
        bound.Bind(0, 0);
        bound.Poll(-1, false, false, 0, true);
        Check(bound.Poll(1, false, false, 0.01f, true).Direction == 1, "A newly observed binding permits the first move");
        bound.Bind(0, 0); // Native selection replaces PlayerGameInput with the same Pad/Side.
        Check(bound.Poll(1, false, false, 0.2f, true).Direction == -1,
            "Equivalent input replacement retains observation and the initial repeat delay");
        Check(bound.Poll(1, false, false, 0.37f, true).Direction == 1,
            "Held direction repeats after 0.35 seconds despite selection rebinding");
        bound.Bind(0, 0);
        Check(bound.Poll(1, false, false, 0.42f, true).Direction == -1 &&
            bound.Poll(1, false, false, 0.48f, true).Direction == 1,
            "Equivalent replacement also retains the subsequent 0.1 second repeat interval");
        bound.Bind(0, 0);
        Check(bound.Poll(1, false, false, 0.59f, true).Direction == 1,
            "Repeated native replacements permit continuous held movement");

        var partner = new ChefGridControls();
        partner.Bind(0, 1);
        partner.Poll(-1, false, false, 0.6f, true);
        Check(partner.Poll(2, false, false, 0.61f, true).Direction == 2, "A split keyboard partner moves independently");
        bound.Bind(0, 0);
        partner.Bind(0, 1);
        Check(bound.Poll(1, false, false, 0.7f, true).Direction == 1 &&
            partner.Poll(2, false, false, 0.7f, true).Direction == -1,
            "Refreshing all players after one selection cannot reset or share repeat timers");
        bound.Bind(0, 0);
        partner.Bind(0, 1);
        Check(partner.Poll(2, false, false, 0.97f, true).Direction == 2,
            "The partner keeps their own initial repeat deadline across other players' selections");

        bound.Poll(-1, true, true, 1, true);
        bound.Bind(0, 0);
        var held = bound.Poll(-1, true, true, 1.01f, true);
        Check(!held.Accept && !held.Cancel, "Equivalent replacements preserve held confirm and cancel edges");
        foreach (var replacement in new[] { new[] { 1, 0 }, new[] { 1, 1 } })
        {
            bound.Bind(replacement[0], replacement[1]);
            var initial = bound.Poll(3, true, true, 2, true);
            var continuing = bound.Poll(3, true, true, 2.5f, true);
            Check(initial.Direction == -1 && !initial.Accept && !initial.Cancel &&
                continuing.Direction == -1 && !continuing.Accept && !continuing.Cancel,
                "Held direction, confirm and cancel are suppressed on a real binding change");
            bound.Poll(-1, false, false, 3, true);
            var fresh = bound.Poll(3, true, true, 3.1f, true);
            Check(fresh.Direction == 3 && fresh.Accept && fresh.Cancel,
                "Releasing and pressing arms the replacement device normally");
        }
        // Returning from a menu and immediately reopening it starts a new input
        // lifetime, even when the native device and side are unchanged.
        var reopened = new ChefGridControls();
        reopened.Bind(1, 1);
        Check(!reopened.Poll(-1, false, true, 4, true).Cancel &&
            !reopened.Poll(-1, false, true, 5, true).Cancel,
            "A new menu session consumes the held closing key without caller-owned observation state");
        reopened.Poll(-1, false, false, 6, true);
        Check(reopened.Poll(-1, false, true, 7, true).Cancel,
            "A fresh Esc/B press works after reopening on the same input binding");
        var hats = new ChefGridControls();
        hats.Bind(0, 0);
        Check(!hats.Poll(-1, false, false, 0, true, true).CycleHat &&
            !hats.Poll(-1, false, false, 1, true, true).CycleHat,
            "A held opening hat key is consumed instead of cycling on entry");
        hats.Poll(-1, false, false, 2, true);
        Check(hats.Poll(-1, false, false, 3, true, true).CycleHat,
            "Pressing the mapped hat key alone cycles without a direction modifier");
        var simultaneous = hats.Poll(3, false, false, 4, true, true);
        Check(!simultaneous.CycleHat && simultaneous.Direction == 3,
            "Holding the hat key neither repeats hats nor redirects normal down navigation");
        hats.Poll(-1, false, false, 5, true);
        hats.Poll(-1, false, false, 6, false, true);
        Check(!hats.Poll(-1, false, false, 7, true, true).CycleHat,
            "Hat presses during a dialog cannot leak into the resumed menu");
        hats.Bind(0, 0);
        Check(!hats.Poll(-1, false, false, 8, true, true).CycleHat,
            "Equivalent native input replacement preserves the held hat edge");
        hats.Poll(-1, false, false, 9, true);
        var otherHats = new ChefGridControls(); otherHats.Bind(0, 1);
        otherHats.Poll(-1, false, false, 9, true);
        Check(hats.Poll(-1, false, false, 10, true, true).CycleHat &&
            !otherHats.Poll(-1, false, false, 10, true).CycleHat &&
            otherHats.Poll(-1, false, false, 11, true, true).CycleHat,
            "Split players cycle independently on their own fresh presses");
        hats.Bind(1, 0);
        Check(!hats.Poll(-1, false, false, 12, true, true).CycleHat,
            "Switching devices consumes the newly observed held hat button");
        hats.Poll(-1, false, false, 13, true);
        Check(hats.Poll(-1, false, false, 14, true, true).CycleHat,
            "The replacement device cycles after release and a fresh press");
        return checks;
    }
}
