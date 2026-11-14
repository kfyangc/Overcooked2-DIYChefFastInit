using System;

namespace OC2DIYChef
{
    internal struct ChefGridCellRect
    {
        internal float X, Y, Size;
    }

    // Fixed viewport; rendering and hit testing share the same stored float edges.
    internal sealed class ChefGridLayout
    {
        internal const int Columns = 10, Rows = 5, VisibleCount = Columns * Rows;
        internal const float Left = 48, Top = 160, Width = 888, Height = 440;
        readonly float[] xEdges, yEdges;
        readonly float side;
        internal float GridRight { get { return xEdges[Columns]; } }
        internal float GridBottom { get { return yEdges[Rows]; } }

        internal ChefGridLayout()
        {
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

        internal int HitTest(float x, float y, int firstVisibleRow, int count)
        {
            if (x < xEdges[0] || y < yEdges[0] || x >= GridRight ||
                y >= GridBottom || firstVisibleRow < 0 || count <= 0) return -1;
            int column = AxisIndex(x, xEdges), row = AxisIndex(y, yEdges);
            int slot = row * Columns + column;
            long index = (long)firstVisibleRow * Columns + slot;
            return index < count ? (int)index : -1;
        }

        static int AxisIndex(float value, float[] edges)
        {
            int index = 0;
            while (index + 1 < edges.Length - 1 && value >= edges[index + 1]) index++;
            return index;
        }
    }
}
