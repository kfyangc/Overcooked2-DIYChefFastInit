using System;

namespace OC2DIYChef
{
    internal struct ChefGridCellRect
    {
        internal float X, Y, Size;
        internal bool Contains(float x, float y)
        {
            return x >= X && x < X + Size && y >= Y && y < Y + Size;
        }
    }

    // Shared geometry for rendering and mouse hit testing at every configured density.
    internal sealed class ChefGridLayout
    {
        internal const int MinColumns = 4, MaxColumns = 16, MinRows = 2, MaxRows = 8;
        internal const float Left = 48, Top = 160, Width = 888, Height = 440, Gap = 6;
        internal readonly int Columns, Rows;
        internal int PageSize { get { return Columns * Rows; } }
        readonly float stepX, stepY, side;

        internal ChefGridLayout(int columns, int rows)
        {
            Columns = Math.Max(MinColumns, Math.Min(MaxColumns, columns));
            Rows = Math.Max(MinRows, Math.Min(MaxRows, rows));
            stepX = Width / Columns;
            stepY = Height / Rows;
            side = Math.Min(stepX, stepY) - Gap;
        }

        internal ChefGridCellRect Cell(int slot)
        {
            return new ChefGridCellRect { X = Left + slot % Columns * stepX + (stepX - side) * 0.5f,
                Y = Top + slot / Columns * stepY + (stepY - side) * 0.5f, Size = side };
        }

        internal int HitTest(float x, float y, int page, int count)
        {
            if (x < Left || y < Top || x >= Left + Width || y >= Top + Height || page < 0) return -1;
            int column = (int)((x - Left) / stepX), row = (int)((y - Top) / stepY);
            int slot = row * Columns + column;
            int index = page * PageSize + slot;
            return index < count && Cell(slot).Contains(x, y) ? index : -1;
        }
    }
}
