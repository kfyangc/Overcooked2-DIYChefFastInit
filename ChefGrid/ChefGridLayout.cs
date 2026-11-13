using System;

namespace OC2DIYChef
{
    internal struct ChefGridCellRect
    {
        internal float X, Y, Size;
    }

    // Shared geometry for rendering and mouse hit testing at every configured density.
    internal sealed class ChefGridLayout
    {
        internal const int MinColumns = 4, MaxColumns = 16, MinRows = 2, MaxRows = 8;
        internal const float Left = 48, Top = 160, Width = 888, Height = 440;
        internal readonly int Columns, Rows;
        internal int PageSize { get { return Columns * Rows; } }
        readonly float[] xEdges, yEdges;
        readonly float side;
        internal float GridRight { get { return xEdges[Columns]; } }
        internal float GridBottom { get { return yEdges[Rows]; } }

        internal ChefGridLayout(int columns, int rows)
        {
            Columns = Math.Max(MinColumns, Math.Min(MaxColumns, columns));
            Rows = Math.Max(MinRows, Math.Min(MaxRows, rows));
            side = Math.Min(Width / Columns, Height / Rows);
            float originX = Left + Math.Max(0, (Width - Columns * side) * 0.5f);
            float originY = Top + Math.Max(0, (Height - Rows * side) * 0.5f);
            xEdges = new float[Columns + 1];
            yEdges = new float[Rows + 1];
            // Store float edges once: legacy x86 Mono can retain extra precision during
            // arithmetic, so recomputing an edge in hit testing can disagree with the UI.
            for (int i = 0; i < xEdges.Length; i++) xEdges[i] = originX + i * side;
            for (int i = 0; i < yEdges.Length; i++) yEdges[i] = originY + i * side;
        }

        internal ChefGridCellRect Cell(int slot)
        {
            return new ChefGridCellRect { X = xEdges[slot % Columns], Y = yEdges[slot / Columns], Size = side };
        }

        internal int HitTest(float x, float y, int page, int count)
        {
            if (x < xEdges[0] || y < yEdges[0] || x >= GridRight ||
                y >= GridBottom || page < 0 || count <= 0) return -1;
            int column = AxisIndex(x, xEdges), row = AxisIndex(y, yEdges);
            int slot = row * Columns + column;
            int index = page * PageSize + slot;
            return index < count ? index : -1;
        }

        static int AxisIndex(float value, float[] edges)
        {
            int index = 0;
            while (index + 1 < edges.Length - 1 && value >= edges[index + 1]) index++;
            return index;
        }
    }
}
