using System;
namespace OC2DIYChef
{
    internal sealed class ChefGridPanelLayout
    {
        internal readonly float Left, Width, GridLeft, GridTop = 140, Side = 72, Gap = 4;
        internal readonly float DetailLeft, DetailTextWidth;
        internal float GridWidth { get { return Columns * Side + (Columns - 1) * Gap; } }
        internal float GridHeight { get { return Rows * Side + (Rows - 1) * Gap; } }
        internal float ScrollLeft { get { return GridLeft + GridWidth + 4; } }
        internal float DetailTop { get { return GridTop + GridHeight + 4; } }
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
            Columns = Math.Max(1, (int)((Width - 36 + Gap) / (Side + Gap)));
            GridLeft = Left + (Width - GridWidth - 14) / 2;
            DetailTextWidth = Math.Min(240, Width - 152);
            DetailLeft = Left + (Width - 128 - 8 - DetailTextWidth) / 2;
        }
        internal ChefGridCellRect Cell(int slot) { return new ChefGridCellRect { X = GridLeft + slot % Columns * (Side + Gap), Y = GridTop + slot / Columns * (Side + Gap), Size = Side }; }
        internal int HitTest(float x, float y, int firstRow, int count)
        {
            if (x < GridLeft || x >= GridLeft + GridWidth || y < GridTop || y >= GridTop + GridHeight) return -1;
            float localX = x - GridLeft, localY = y - GridTop, pitch = Side + Gap;
            if (localX % pitch >= Side || localY % pitch >= Side) return -1;
            int value = firstRow * Columns + (int)(localY / pitch) * Columns + (int)(localX / pitch);
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
        internal float ThumbHeight(int count) { return RowCount(count) <= Rows ? GridHeight : Math.Max(20, GridHeight * Rows / RowCount(count)); }
        internal float ThumbTop(int first, int count) { return GridTop + (LastFirstRow(count) == 0 ? 0 : (GridHeight - ThumbHeight(count)) * Math.Max(0, Math.Min(LastFirstRow(count), first)) / LastFirstRow(count)); }
        internal bool BeginScroll(float x, float y, int first, int count)
        {
            if (LastFirstRow(count) == 0 || x < ScrollLeft - 3 || x >= ScrollLeft + 11 || y < GridTop || y >= GridTop + GridHeight) return false;
            float top = ThumbTop(first, count), height = ThumbHeight(count);
            grab = y >= top && y < top + height ? y - top : height / 2;
            Dragging = true; return true;
        }
        internal int DragScroll(float y, int count)
        {
            float travel = GridHeight - ThumbHeight(count);
            return travel <= 0 ? 0 : (int)Math.Floor(Math.Max(0, Math.Min(1, (y - GridTop - grab) / travel)) * LastFirstRow(count) + 0.5f);
        }
        internal void EndScroll() { Dragging = false; }
    }
}
