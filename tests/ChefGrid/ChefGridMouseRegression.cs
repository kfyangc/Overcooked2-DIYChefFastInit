using System;
using OC2DIYChef;

static class ChefGridMouseRegression
{
    static int checks;
    static void Check(bool value, string reason)
    {
        if (!value) throw new Exception(reason);
        checks++;
    }

    internal static int Run()
    {
        checks = 0;
        var click = new ChefGridDoubleClick();
        long p1 = ChefGridNavigation.PlayerKey(0, 0), p2 = ChefGridNavigation.PlayerKey(1, 0);
        Check(!click.Click(p1, 40, 100, 200, 1), "A single click only selects a chef");
        Check(click.Click(p1, 40, 102, 201, 1.2f), "A second nearby click on the same chef confirms");
        Check(!click.Click(p1, 40, 102, 201, 1.25f), "A third click cannot issue another confirmation");
        Check(!click.Click(p2, 40, 102, 201, 1.3f), "The next player's first click cannot complete the previous player's click pair");
        Check(click.Click(p2, 40, 102, 201, 1.5f), "The next player can confirm with their own double click");
        Check(!click.Click(p1, 10, 100, 200, 2) && !click.Click(p1, 11, 100, 200, 2.1f),
            "Clicking different chefs in quick succession does not confirm either");
        Check(click.Click(p1, 11, 100, 200, 2.2f), "The new chef becomes the start of its own click pair");
        Check(!click.Click(p1, 20, 100, 200, 3) && !click.Click(p1, 20, 100, 200, 3.5f),
            "Slow clicks remain selections rather than confirmations");
        Check(click.Click(p1, 20, 100, 200, 3.7f), "A slow click can start the next valid pair");
        Check(!click.Click(p1, 20, 100, 200, 4) && !click.Click(p1, 20, 120, 200, 4.1f),
            "Large pointer movement within a card is not treated as a double click");
        Check(click.Click(p1, 20, 120, 200, 4.2f), "A nearby second click after pointer movement can confirm");
        click.Click(p1, 30, 100, 200, 5);
        click.Reset();
        Check(!click.Click(p1, 30, 100, 200, 5.1f), "Scrolling, keyboard navigation, player changes and other clicks invalidate pending mouse confirmation");
        Check(!click.Click(p1, 30, 100, 200, 4.9f), "A clock reset cannot complete an older click pair");
        return checks;
    }
}
