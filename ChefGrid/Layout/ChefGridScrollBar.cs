using System;

namespace OC2DIYChef
{
    // The handle and its mouse hit area share geometry; browsing does not change a chef.
    internal sealed class ChefGridScrollBar
    {
        internal const float CentreX = 944, Width = 8, HitWidth = 12;
        internal const float Top = ChefGridLayout.Top, Height = ChefGridLayout.Height;
        internal bool Dragging { get; private set; }
        float grabOffset;

        internal float ThumbHeight(int count)
        {
            int rows = ChefGridNavigation.RowCount(count);
            return rows <= ChefGridLayout.Rows ? Height : Math.Max(20f, Height * ChefGridLayout.Rows / rows);
        }

        internal float ThumbTop(int firstVisibleRow, int count)
        {
            int last = ChefGridNavigation.LastFirstRow(count);
            return Top + (last == 0 ? 0 : (Height - ThumbHeight(count)) * Math.Max(0, Math.Min(last, firstVisibleRow)) / last);
        }

        internal bool Begin(float x, float y, int firstVisibleRow, int count)
        {
            if (ChefGridNavigation.LastFirstRow(count) == 0 ||
                x < CentreX - HitWidth / 2 || x >= CentreX + HitWidth / 2 || y < Top || y >= Top + Height) return false;
            float thumbTop = ThumbTop(firstVisibleRow, count), thumbHeight = ThumbHeight(count);
            // Keep the grabbed point under the mouse. Track clicks centre the handle.
            grabOffset = y >= thumbTop && y < thumbTop + thumbHeight ? y - thumbTop : thumbHeight / 2;
            Dragging = true;
            return true;
        }

        internal int Drag(float y, int count)
        {
            int last = ChefGridNavigation.LastFirstRow(count);
            float travel = Height - ThumbHeight(count);
            if (last == 0 || travel <= 0) return 0;
            float progress = Math.Max(0, Math.Min(1, (y - Top - grabOffset) / travel));
            return Math.Min(last, (int)Math.Floor(progress * last + 0.5f));
        }

        internal void End() { Dragging = false; }
    }
}
