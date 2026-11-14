using System;
using OC2DIYChef;

// User-visible contracts for independent local panels, rather than a copy of layout formulas.
static class ChefGridParallelRegression
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    internal static int Run()
    {
        checks = 0;
        ReadyIndependently();
        LayoutAndScrolling();
        IndependentInputEdges();
        return checks;
    }

    static void ReadyIndependently()
    {
        long[] identities = { 0, 1, 1L << 32, 2L << 32 };
        // All confirmation orders are allowed, including P4 before P1.
        foreach (long a in identities)
            foreach (long b in identities)
                foreach (long c in identities)
                    foreach (long d in identities)
                    {
                        if (a == b || a == c || a == d || b == c || b == d || c == d) continue;
                        var ready = new ChefGridReadySet();
                        ready.Sync(identities);
                        foreach (long key in new[] { a, b, c })
                            Check(ready.Confirm(key) && ready.IsConfirmed(key) && !ready.Complete,
                                "Any local player can confirm without waiting; the menu remains open for others");
                        Check(ready.Confirm(d) && ready.Complete, "Only all participants being ready permits commit");
                        ready.Sync(new[] { d, c, b, a });
                        Check(ready.Complete, "User index reorder does not undo readiness while native closing is deferred");
                        Check(ready.Reopen(b) && !ready.IsConfirmed(b) && !ready.Complete &&
                            ready.IsConfirmed(a) && ready.IsConfirmed(c) && ready.IsConfirmed(d),
                            "Editing one ready panel cannot reopen another player's selection");
                        ready.Sync(new[] { a, c, d });
                        Check(ready.Complete && !ready.IsConfirmed(b), "A departing unready user cannot block the remaining ready users");
                        ready.Sync(identities);
                        Check(!ready.Complete && !ready.IsConfirmed(b), "A newly joined identity must confirm independently");
                        Check(!ready.Confirm(999) && !ready.Reopen(999), "Unknown identities cannot alter confirmation state");
                        ready.Sync(new long[0]);
                        Check(!ready.Complete && !ready.Confirm(a), "An empty roster must not trigger an automatic successful commit");
                    }
        var duplicate = new ChefGridReadySet();
        duplicate.Sync(new[] { identities[0], identities[0], identities[1] });
        Check(duplicate.Confirm(identities[1]) && !duplicate.Complete &&
            duplicate.Confirm(identities[0]) && duplicate.Complete,
            "Duplicate stable keys create no extra panels or impossible extra confirmations");
    }

    static void LayoutAndScrolling()
    {
        for (int participants = 1; participants <= 4; participants++)
        {
            ChefGridPanelLayout previous = null;
            for (int player = 0; player < participants; player++)
            {
                var panel = new ChefGridPanelLayout(player, participants);
                Check(panel.Width > 0 && panel.Side > 0 && panel.Columns > 0 && panel.Rows == 5 &&
                    panel.VisibleCount == panel.Columns * panel.Rows,
                    "Every participant receives a usable five-row grid");
                Check(panel.Left >= 0 && panel.Left + panel.Width <= 1280 && panel.GridLeft >= panel.Left,
                    "Each horizontal panel and its grid remain inside the screen");
                if (previous != null)
                    Check(panel.Left >= previous.Left + previous.Width && panel.GridTop == previous.GridTop &&
                        panel.Side == previous.Side, "Two, three and four participants stay in one horizontal row without overlap");

                foreach (int count in new[] { 1, 9, 50, 109, 507 })
                {
                    int last = panel.LastFirstRow(count);
                    Check(last >= 0 && panel.Follow(-100, 0, count) == 0 &&
                        panel.Follow(int.MaxValue, count - 1, count) == last,
                        "Independent scroll positions clamp at both roster ends");
                    float previousThumb = float.MinValue;
                    for (int first = 0; first <= last; first++)
                    {
                        float thumb = panel.ThumbTop(first, count);
                        Check(thumb >= previousThumb && panel.ThumbHeight(count) > 0,
                            "Each panel's scrollbar advances monotonically through the roster");
                        previousThumb = thumb;
                        for (int slot = 0; slot < panel.VisibleCount; slot++)
                        {
                            var cell = panel.Cell(slot);
                            Check(cell.X >= panel.Left && cell.X + cell.Size <= panel.Left + panel.Width + 0.001f &&
                                cell.Y >= panel.GridTop && cell.Size > 0,
                                "Thumbnail rectangles stay within their own player's panel");
                            int expected = first * panel.Columns + slot;
                            Check(panel.HitTest(cell.X + cell.Size / 2, cell.Y + cell.Size / 2, first, count) ==
                                (expected < count ? expected : -1),
                                "Scrolled thumbnail hit testing selects its actual chef and rejects empty final cells");
                            if (slot % panel.Columns + 1 < panel.Columns)
                            {
                                var next = panel.Cell(slot + 1);
                                Check(Math.Abs(cell.X + cell.Size - next.X) < 0.001f,
                                    "Horizontal thumbnail edges have no inserted spacing");
                            }
                        }
                    }
                    for (int index = 0; index < count; index++)
                    {
                        int first = panel.Follow(0, index, count), row = index / panel.Columns;
                        Check(first >= 0 && first <= last && row >= first && row < first + panel.Rows,
                            "Directional following keeps the local selection visible without wrapping");
                        if (first > 0 && first < last)
                            Check(row >= first + 1 && row <= first + panel.Rows - 2,
                                "Away from list ends the cursor stays in the middle three rows");
                    }
                }
                var origin = panel.Cell(0);
                Check(panel.HitTest(origin.X - 0.01f, origin.Y + 1, 0, 109) == -1 &&
                    panel.HitTest(origin.X + 1, origin.Y - 0.01f, 0, 109) == -1 &&
                    panel.HitTest(origin.X + 1, origin.Y + 1, 0, 0) == -1,
                    "Panel margins and an empty roster do not hover-select a chef");
                float trackX = panel.GridLeft + panel.Columns * panel.Side + 4;
                Check(panel.BeginScroll(trackX, panel.GridTop + 1, 0, 109) && panel.Dragging &&
                    panel.DragScroll(panel.GridTop - 1000, 109) == 0 &&
                    panel.DragScroll(panel.GridTop + panel.Rows * panel.Side + 1000, 109) == panel.LastFirstRow(109),
                    "Each independent scrollbar can be grabbed and dragged to both list ends, including beyond its bounds");
                panel.EndScroll();
                Check(!panel.Dragging && !panel.BeginScroll(trackX, panel.GridTop + 1, 0, 1),
                    "Releasing a panel drag ends ownership and a roster fitting in one viewport cannot drag");
                if (previous != null)
                {
                    var other = previous.Cell(0);
                    Check(panel.HitTest(other.X + other.Size / 2, other.Y + other.Size / 2, 0, 109) == -1,
                        "A pointer over a neighboring player's chef cannot select this player's chef");
                    Check(!previous.Dragging && !previous.BeginScroll(trackX, panel.GridTop + 1, 0, 109),
                        "Dragging one player's track cannot capture another player's scrollbar");
                    int heldPosition = previous.Follow(0, 80, 109);
                    panel.Follow(0, 0, 109);
                    Check(previous.Follow(heldPosition, 80, 109) == heldPosition,
                        "Following another panel cannot change an existing player's scroll position");
                }
                previous = panel;
            }
        }
    }

    static void IndependentInputEdges()
    {
        var first = new ChefGridControls();
        var second = new ChefGridControls();
        first.Poll(-1, true, false, 0, false);
        second.Poll(-1, false, false, 0, false);
        Check(!first.Poll(-1, true, false, 1, true).Accept && second.Poll(3, true, false, 1, true).Accept,
            "Holding P1's opening confirm cannot prevent P2 from selecting and confirming independently");
        first.Poll(-1, false, false, 2, true);
        second.Poll(-1, false, false, 2, true);
        var a = first.Poll(0, true, false, 3, true);
        var b = second.Poll(3, true, false, 3, true);
        Check(a.Direction == 0 && b.Direction == 3 && a.Accept && b.Accept,
            "Two local devices can move different cursors and confirm during the same frame");
        Check(!first.Poll(0, true, false, 3.01f, true).Accept &&
            !second.Poll(3, true, false, 3.01f, true).Accept,
            "Parallel confirmation still uses press edges rather than held-button repetition");
    }
}
