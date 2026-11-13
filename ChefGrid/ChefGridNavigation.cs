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
            if (dx != 0) return (index + Math.Sign(dx) + count) % count;
            int rows = (count + columns - 1) / columns;
            int row = (index / columns + Math.Sign(dy) + rows) % rows;
            return Math.Min(row * columns + index % columns, count - 1);
        }

        internal static int PageCount(int count, int pageSize)
        {
            return Math.Max(1, (count + Math.Max(1, pageSize) - 1) / Math.Max(1, pageSize));
        }

        internal static int TurnPage(int page, int delta, int count, int pageSize)
        {
            int pages = PageCount(count, pageSize);
            return ((page + delta) % pages + pages) % pages;
        }

        internal static long PlayerKey(int engagement, int split)
        {
            return ((long)(uint)engagement << 32) | (uint)split;
        }
    }
}
