using System;
using OC2DIYChef;

static class ChefGridScrollBarRegression
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
        var bar = new ChefGridScrollBar();
        for (int count = 0; count <= 400; count++)
        {
            int last = ChefGridNavigation.LastFirstRow(count);
            float height = bar.ThumbHeight(count), travel = ChefGridScrollBar.Height - height;
            Check(height > 0 && height <= ChefGridScrollBar.Height, "Scrollbar handles remain within the track for empty, small and growing rosters");
            if (last == 0)
            {
                Check(!bar.Begin(ChefGridScrollBar.CentreX, ChefGridScrollBar.Top + 10, 0, count) &&
                    bar.Drag(10000, count) == 0, "A list fitting in the viewport cannot be dragged or divided by zero");
                continue;
            }
            for (int first = 0; first <= last; first++)
            {
                float grabbedY = bar.ThumbTop(first, count) + height * 0.25f;
                Check(bar.Begin(ChefGridScrollBar.CentreX, grabbedY, first, count) && bar.Dragging &&
                    bar.Drag(grabbedY, count) == first, "Grabbing any visible handle preserves its scroll position without a jump");
                Check(bar.Drag(-10000, count) == 0 && bar.Drag(10000, count) == last,
                    "Dragging beyond the panel clamps to the first or last row without wrapping");
                int target = last - first;
                float targetY = ChefGridScrollBar.Top + travel * target / last + height * 0.25f;
                Check(bar.Drag(targetY, count) == target, "Dragging maps the grabbed point to the intended row offset");
                bar.End();
                Check(!bar.Dragging, "Mouse release ends the scrollbar capture");
            }
        }
        int middle = 3, roster = 109;
        Check(bar.Begin(ChefGridScrollBar.CentreX, ChefGridScrollBar.Top + 1, middle, roster) &&
            bar.Drag(ChefGridScrollBar.Top + 1, roster) == 0, "Clicking the track above the handle moves the list to the top");
        bar.End();
        Check(bar.Begin(ChefGridScrollBar.CentreX, ChefGridScrollBar.Top + ChefGridScrollBar.Height - 1, middle, roster) &&
            bar.Drag(ChefGridScrollBar.Top + ChefGridScrollBar.Height - 1, roster) == ChefGridNavigation.LastFirstRow(roster),
            "Clicking the track below the handle moves the list to the bottom");
        bar.End();
        Check(!bar.Begin(ChefGridScrollBar.CentreX - ChefGridScrollBar.HitWidth / 2 - 0.01f, 200, 0, roster) &&
            !bar.Begin(ChefGridScrollBar.CentreX + ChefGridScrollBar.HitWidth / 2, 200, 0, roster) &&
            !bar.Begin(ChefGridScrollBar.CentreX, ChefGridScrollBar.Top - 1, 0, roster) &&
            !bar.Begin(ChefGridScrollBar.CentreX, ChefGridScrollBar.Top + ChefGridScrollBar.Height, 0, roster),
            "Clicks in thumbnails or beyond the track cannot start scrollbar capture");
        return checks;
    }
}
