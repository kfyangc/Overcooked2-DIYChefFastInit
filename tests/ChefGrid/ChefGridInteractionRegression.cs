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
        var turns = new ChefGridTurnOrder();
        long p1 = ChefGridNavigation.PlayerKey(0, 0), p2 = ChefGridNavigation.PlayerKey(0, 1);
        long p3 = ChefGridNavigation.PlayerKey(1, 0), p4 = ChefGridNavigation.PlayerKey(2, 0);
        turns.Sync(new[] { p1, p2, p3, p4 });
        Check(turns.Current == p1 && !turns.Complete, "First local player owns the first turn");
        Check(!turns.Confirm(p2) && !turns.Confirm(p4) && turns.Current == p1,
            "Waiting players cannot confirm or change the current turn");
        Check(!turns.Reopen(p3), "A waiting player cannot bypass earlier turns through the status strip");
        Check(turns.Confirm(p1) && turns.Current == p2 && turns.IsConfirmed(p1), "First confirmation advances to the split-keyboard partner");
        Check(!turns.Confirm(p1) && turns.Current == p2, "Repeated confirmation cannot skip a turn");
        Check(turns.Confirm(p2) && turns.Confirm(p3) && turns.Current == p4, "Players confirm strictly in order");
        Check(turns.Reopen(p2) && turns.Current == p2 && turns.IsConfirmed(p1) &&
            !turns.IsConfirmed(p2) && !turns.IsConfirmed(p3), "Editing an earlier player restarts all subsequent confirmations");
        turns.Sync(new[] { p2, p4 });
        Check(turns.Current == p2 && !turns.IsConfirmed(p1), "Departed identities are removed rather than retained by player index");
        Check(turns.Confirm(p2) && turns.Current == p4, "Remaining player retains their own pending turn");
        turns.Sync(new[] { p4, p2 });
        Check(turns.Current == p4 && turns.IsConfirmed(p2), "Reordering users preserves confirmation by input identity");
        Check(turns.Confirm(p4) && turns.Complete && !turns.Current.HasValue, "Last confirmation marks the menu ready to commit");
        turns.Sync(new[] { p4, p2 });
        Check(turns.Complete, "Confirmation survives the next menu update while waiting for native Hide");
        turns.Sync(new[] { p1, p4, p2 });
        Check(!turns.Complete && turns.Current == p1, "A new participant must confirm even when native closing was deferred");
        turns.Sync(new[] { p4, p2 });
        Check(turns.Complete, "Removing the pending new player permits completed participants to finish");
        turns.Sync(new long[0]);
        Check(!turns.Complete && !turns.Current.HasValue && !turns.Confirm(p1), "An empty menu has no active or confirmable turn");
        turns.Sync(new[] { p3, p3 });
        Check(turns.Confirm(p3) && turns.Complete, "Duplicate input identities must not create duplicate turns");

        var first = new ChefGridControls();
        var second = new ChefGridControls();
        first.Poll(-1, true, false, 0, false);
        second.Poll(-1, true, false, 0, false);
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
        return checks;
    }
}
