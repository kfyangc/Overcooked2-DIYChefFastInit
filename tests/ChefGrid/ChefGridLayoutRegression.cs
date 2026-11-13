using System;
using OC2DIYChef;

static class ChefGridLayoutRegression
{
    internal static int Run()
    {
        int checks = 0;
        var layout = new ChefGridLayout();
        int columns = ChefGridLayout.Columns, rows = ChefGridLayout.Rows, visible = ChefGridLayout.VisibleCount;
        if (columns != 10 || rows != 5 || visible != 50) throw new Exception("The viewport must be fixed at 10x5");
        checks++;
        for (int slot = 0; slot < visible; slot++)
        {
            var rect = layout.Cell(slot);
            if (rect.Size <= 0 || rect.X < ChefGridLayout.Left || rect.Y < ChefGridLayout.Top ||
                rect.X + rect.Size > ChefGridLayout.Left + ChefGridLayout.Width + 0.001f ||
                rect.Y + rect.Size > ChefGridLayout.Top + ChefGridLayout.Height + 0.001f)
                throw new Exception("Cells must remain inside the viewport");
            checks++;
            float x = rect.X + rect.Size * 0.5f, y = rect.Y + rect.Size * 0.5f;
            // Every row offset of the user's list, including the partial last row.
            for (int first = 0; first <= ChefGridNavigation.LastFirstRow(109); first++)
            {
                int index = first * columns + slot;
                if (layout.HitTest(x, y, first, 109) != (index < 109 ? index : -1))
                    throw new Exception("Scrolled thumbnails must hit their actual chef, without selecting empty cells");
                checks++;
            }
            if (slot % columns + 1 < columns)
            {
                var right = layout.Cell(slot + 1);
                if (Math.Abs(rect.X + rect.Size - right.X) > 0.0002f || Math.Abs(rect.Y - right.Y) > 0.0002f)
                    throw new Exception("Horizontal neighbors must share an edge with zero spacing");
                if (layout.HitTest(right.X - 0.01f, y, 1, 109) != slot + columns ||
                    layout.HitTest(right.X, y, 1, 109) != slot + columns + 1)
                    throw new Exception("Scrolled horizontal edges must have no dead strip or overlap");
                checks += 2;
            }
            if (slot / columns + 1 < rows)
            {
                var below = layout.Cell(slot + columns);
                if (Math.Abs(rect.Y + rect.Size - below.Y) > 0.0002f || Math.Abs(rect.X - below.X) > 0.0002f)
                    throw new Exception("Vertical neighbors must share an edge with zero spacing");
                if (layout.HitTest(x, below.Y - 0.01f, 1, 109) != slot + columns ||
                    layout.HitTest(x, below.Y, 1, 109) != slot + columns * 2)
                    throw new Exception("Scrolled vertical edges must have no dead strip or overlap");
                checks += 2;
            }
        }
        var origin = layout.Cell(0);
        if (layout.HitTest(origin.X - 0.01f, origin.Y + 1, 0, 109) != -1 ||
            layout.HitTest(origin.X + 1, origin.Y - 0.01f, 0, 109) != -1 ||
            layout.HitTest(layout.GridRight, origin.Y + 1, 0, 109) != -1 ||
            layout.HitTest(origin.X + 1, layout.GridBottom, 0, 109) != -1)
            throw new Exception("Outer grid margins must not select chefs");
        checks++;
        if (layout.HitTest(origin.X, origin.Y, 1, 109) != columns ||
            layout.HitTest(origin.X, origin.Y, -1, 109) != -1 ||
            layout.HitTest(origin.X, origin.Y, int.MaxValue, 109) != -1 ||
            layout.HitTest(origin.X, origin.Y, 0, 0) != -1)
            throw new Exception("Hit testing must use row offsets and reject invalid/empty ranges");
        return checks + 1;
    }
}
