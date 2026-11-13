using System;

namespace OC2DIYChef
{
    // Independent of Unity so edge rows and growing rosters can be verified offline.
    internal static class ChefGridNavigation
    {
        internal static int Move(int index, int count, int columns, int dx, int dy)
        {
            if (count <= 0) return -1;
            columns = Math.Max(1, columns);
            index = Math.Max(0, Math.Min(index, count - 1));
            if (dx != 0) return Math.Max(0, Math.Min(count - 1, index + Math.Sign(dx)));
            int rows = (count + columns - 1) / columns;
            int row = Math.Max(0, Math.Min(rows - 1, index / columns + Math.Sign(dy)));
            return Math.Min(row * columns + index % columns, count - 1);
        }

        internal static int RowCount(int count)
        {
            return (int)((Math.Max(0L, count) + ChefGridLayout.Columns - 1) / ChefGridLayout.Columns);
        }

        internal static int LastFirstRow(int count)
        {
            return Math.Max(0, RowCount(count) - ChefGridLayout.Rows);
        }

        internal static int Follow(int firstVisibleRow, int index, int count)
        {
            int first = Math.Max(0, Math.Min(LastFirstRow(count), firstVisibleRow));
            int row = Math.Max(0, Math.Min(Math.Max(0, count - 1), index)) / ChefGridLayout.Columns;
            // One row of context above and below the cursor, except at the list ends.
            if (row < first + 1) first = row - 1;
            else if (row > first + ChefGridLayout.Rows - 2) first = row - (ChefGridLayout.Rows - 2);
            return Math.Max(0, Math.Min(LastFirstRow(count), first));
        }

        internal static int ShiftRows(int index, int count, int delta)
        {
            if (count <= 0) return -1;
            index = Math.Max(0, Math.Min(count - 1, index));
            long row = Math.Max(0L, Math.Min(RowCount(count) - 1L, (long)index / ChefGridLayout.Columns + delta));
            return (int)Math.Min(count - 1L, row * ChefGridLayout.Columns + index % ChefGridLayout.Columns);
        }

        internal static long PlayerKey(int engagement, int split)
        {
            return ((long)(uint)engagement << 32) | (uint)split;
        }
    }
}
