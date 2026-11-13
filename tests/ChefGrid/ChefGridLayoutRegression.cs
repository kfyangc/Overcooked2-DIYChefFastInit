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
                        throw new Exception("Configured cells must stay inside the grid panel");
                    checks++;
                    float x = rect.X + rect.Size * 0.5f, y = rect.Y + rect.Size * 0.5f;
                    if (layout.HitTest(x, y, 0, layout.PageSize) != slot)
                        throw new Exception("Rendered cell centers must select their corresponding entries");
                    checks++;
                    int next = layout.HitTest(x, y, 1, layout.PageSize + 1);
                    if (next != (slot == 0 ? layout.PageSize : -1))
                        throw new Exception("Partially filled pages must not select empty cells");
                    checks++;
                }
                if (layout.HitTest(ChefGridLayout.Left, ChefGridLayout.Top, 0, 999) != -1 ||
                    layout.HitTest(ChefGridLayout.Left + ChefGridLayout.Width, ChefGridLayout.Top + 10, 0, 999) != -1 ||
                    layout.HitTest(ChefGridLayout.Left + 10, ChefGridLayout.Top + ChefGridLayout.Height, 0, 999) != -1)
                    throw new Exception("Gaps and panel edges must not select a chef");
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
