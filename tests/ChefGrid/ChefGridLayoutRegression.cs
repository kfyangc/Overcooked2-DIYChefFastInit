using System;
using OC2DIYChef;

static class ChefGridLayoutRegression
{
    internal static int Run()
    {
        int checks = 0;
        for (int columns = ChefGridLayout.MinColumns; columns <= ChefGridLayout.MaxColumns; columns++)
            for (int rows = ChefGridLayout.MinRows; rows <= ChefGridLayout.MaxRows; rows++)
            {
                var layout = new ChefGridLayout(columns, rows);
                for (int slot = 0; slot < layout.PageSize; slot++)
                {
                    var rect = layout.Cell(slot);
                    if (rect.Size <= 0 || rect.X < ChefGridLayout.Left || rect.Y < ChefGridLayout.Top ||
                        rect.X + rect.Size > ChefGridLayout.Left + ChefGridLayout.Width + 0.001f ||
                        rect.Y + rect.Size > ChefGridLayout.Top + ChefGridLayout.Height + 0.001f)
                        throw new Exception("Configured cells must stay inside the grid panel: " + columns + "x" + rows +
                            " slot=" + slot + " x=" + rect.X.ToString("R") + " y=" + rect.Y.ToString("R") + " size=" + rect.Size.ToString("R"));
                    checks++;
                    float x = rect.X + rect.Size * 0.5f, y = rect.Y + rect.Size * 0.5f;
                    if (layout.HitTest(x, y, 0, layout.PageSize) != slot)
                        throw new Exception("Rendered cell centers must select their corresponding entries");
                    checks++;
                    int next = layout.HitTest(x, y, 1, layout.PageSize + 1);
                    if (next != (slot == 0 ? layout.PageSize : -1))
                        throw new Exception("Partially filled pages must not select empty cells");
                    checks++;
                    if (slot % columns + 1 < columns)
                    {
                        var right = layout.Cell(slot + 1);
                        if (Math.Abs(rect.X + rect.Size - right.X) > 0.0002f || Math.Abs(rect.Y - right.Y) > 0.0002f)
                            throw new Exception("Horizontal neighbors must share an edge with zero spacing");
                        if (layout.HitTest(right.X - 0.01f, y, 0, layout.PageSize) != slot ||
                            layout.HitTest(right.X, y, 0, layout.PageSize) != slot + 1)
                            throw new Exception("A shared horizontal edge must have no dead strip or overlap in mouse selection");
                        checks += 2;
                    }
                    if (slot / columns + 1 < rows)
                    {
                        var below = layout.Cell(slot + columns);
                        if (Math.Abs(rect.Y + rect.Size - below.Y) > 0.0002f || Math.Abs(rect.X - below.X) > 0.0002f)
                            throw new Exception("Vertical neighbors must share an edge with zero spacing");
                        if (layout.HitTest(x, below.Y - 0.01f, 0, layout.PageSize) != slot ||
                            layout.HitTest(x, below.Y, 0, layout.PageSize) != slot + columns)
                            throw new Exception("A shared vertical edge must have no dead strip or overlap in mouse selection");
                        checks += 2;
                    }
                }
                var first = layout.Cell(0);
                float lastX = layout.GridRight, lastY = layout.GridBottom;
                if (layout.HitTest(first.X - 0.01f, first.Y + 1, 0, 999) != -1 ||
                    layout.HitTest(first.X + 1, first.Y - 0.01f, 0, 999) != -1 ||
                    layout.HitTest(lastX, first.Y + 1, 0, 999) != -1 ||
                    layout.HitTest(first.X + 1, lastY, 0, 999) != -1)
                    throw new Exception("The outer grid margin and its right/bottom edges must not select a chef");
                checks++;
                if (layout.HitTest(first.X, first.Y, 0, layout.PageSize) != 0 ||
                    layout.HitTest(lastX - 0.01f, lastY - 0.01f, 0, layout.PageSize) != layout.PageSize - 1)
                    throw new Exception("Packed grid corners must select the first and last chef");
                checks++;
                if (layout.HitTest(first.X, first.Y, 1, layout.PageSize + 1) != layout.PageSize ||
                    layout.HitTest(layout.Cell(1).X, first.Y + 1, 1, layout.PageSize + 1) != -1 ||
                    layout.HitTest(first.X, first.Y, -1, 999) != -1 || layout.HitTest(first.X, first.Y, 0, 0) != -1)
                    throw new Exception("Shared edges cannot select empty cells or invalid pages");
                checks++;
                if (ChefGridNavigation.Move(layout.PageSize - 1, layout.PageSize * 2, columns, 1, 0) != layout.PageSize)
                    throw new Exception("Configured navigation must cross the same page boundary as hit testing");
                checks++;
            }
        var clamped = new ChefGridLayout(-10, 1000);
        if (clamped.Columns != ChefGridLayout.MinColumns || clamped.Rows != ChefGridLayout.MaxRows)
            throw new Exception("Invalid configured dimensions must be clamped");
        checks++;
        if (new ChefGridLayout(10, 5).PageSize != 50) throw new Exception("Default density must show 50 chefs");
        return checks + 1;
    }
}
