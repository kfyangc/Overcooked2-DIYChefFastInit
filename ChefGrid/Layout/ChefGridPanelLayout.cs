using System;
namespace OC2DIYChef
{
    internal sealed class ChefGridPanelLayout
    {
        internal readonly float Left, Width, GridLeft, GridTop = 270, Side = 72;
        internal readonly int Columns;
        internal int Rows { get { return 5; } }
        internal int VisibleCount { get { return Rows * Columns; } }
        internal bool Dragging { get; private set; }
        float grab;
        internal ChefGridPanelLayout(int index, int count)
        {
            count = Math.Max(1, Math.Min(4, count));
            Width = 1200f / count - 8;
            Left = 40 + index * (Width + 8);
            Columns = Math.Max(1, Math.Min(10, (int)((Width - 36) / Side)));
            GridLeft = Left + (Width - Columns * Side - 14) / 2;
        }
        internal ChefGridCellRect Cell(int slot) { return new ChefGridCellRect { X = GridLeft + slot % Columns * Side, Y = GridTop + slot / Columns * Side, Size = Side }; }
        internal int HitTest(float x, float y, int firstRow, int count)
        {
            if (x < GridLeft || x >= GridLeft + Columns * Side || y < GridTop || y >= GridTop + Rows * Side) return -1;
            int value = firstRow * Columns + (int)((y - GridTop) / Side) * Columns + (int)((x - GridLeft) / Side);
            return value >= 0 && value < count ? value : -1;
        }
        int RowCount(int count) { return (Math.Max(0, count) + Columns - 1) / Columns; }
        internal int LastFirstRow(int count) { return Math.Max(0, RowCount(count) - Rows); }
        internal int Follow(int first, int index, int count)
        {
            int row = Math.Max(0, Math.Min(Math.Max(0, count - 1), index)) / Columns;
            if (row < first + 1) first = row - 1;
            else if (row > first + Rows - 2) first = row - Rows + 2;
            return Math.Max(0, Math.Min(LastFirstRow(count), first));
        }
        internal float ThumbHeight(int count) { return RowCount(count) <= Rows ? Rows * Side : Math.Max(20, Rows * Side * Rows / RowCount(count)); }
        internal float ThumbTop(int first, int count) { return GridTop + (LastFirstRow(count) == 0 ? 0 : (Rows * Side - ThumbHeight(count)) * Math.Max(0, Math.Min(LastFirstRow(count), first)) / LastFirstRow(count)); }
        internal bool BeginScroll(float x, float y, int first, int count)
        {
            if (LastFirstRow(count) == 0 || x < GridLeft + Columns * Side + 1 || x >= GridLeft + Columns * Side + 15 || y < GridTop || y >= GridTop + Rows * Side) return false;
            float top = ThumbTop(first, count), height = ThumbHeight(count);
            grab = y >= top && y < top + height ? y - top : height / 2;
            Dragging = true; return true;
        }
        internal int DragScroll(float y, int count)
        {
            float travel = Rows * Side - ThumbHeight(count);
            return travel <= 0 ? 0 : (int)Math.Floor(Math.Max(0, Math.Min(1, (y - GridTop - grab) / travel)) * LastFirstRow(count) + 0.5f);
        }
        internal void EndScroll() { Dragging = false; }
    }
}
