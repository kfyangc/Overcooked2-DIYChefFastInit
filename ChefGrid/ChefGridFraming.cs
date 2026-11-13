using System;

namespace OC2DIYChef
{
    // Camera-plane measurements, independent of Unity so asymmetric chefs can be checked offline.
    internal struct ChefPortraitBounds
    {
        internal bool Valid;
        internal float MinX, MinY, MaxX, MaxY;
        internal float Width { get { return MaxX - MinX; } }
        internal float Height { get { return MaxY - MinY; } }
        internal float CenterX { get { return (MinX + MaxX) * 0.5f; } }
        internal float CenterY { get { return (MinY + MaxY) * 0.5f; } }

        internal void Include(float x, float y)
        {
            if (!Valid) { MinX = MaxX = x; MinY = MaxY = y; Valid = true; return; }
            MinX = Math.Min(MinX, x); MinY = Math.Min(MinY, y);
            MaxX = Math.Max(MaxX, x); MaxY = Math.Max(MaxY, y);
        }

        internal void Include(ChefPortraitBounds other)
        {
            if (!other.Valid) return;
            Include(other.MinX, other.MinY);
            Include(other.MaxX, other.MaxY);
        }
    }

    internal struct ChefPortraitFrame
    {
        internal float X, Y, HalfSize;
    }

    internal static class ChefGridFraming
    {
        internal static ChefPortraitFrame Calculate(ChefPortraitBounds all, ChefPortraitBounds head, bool thumbnail)
        {
            return Calculate(all, head, new ChefPortraitBounds(), thumbnail);
        }

        internal static ChefPortraitFrame Calculate(ChefPortraitBounds all, ChefPortraitBounds head, ChefPortraitBounds eyes, bool thumbnail)
        {
            if (!all.Valid) throw new InvalidOperationException("model has no visible geometry");
            if (!head.Valid)
            {
                // A full-body replacement may not expose a separate head mesh.
                head.Include(all.MinX, all.MinY + all.Height * 0.4f);
                head.Include(all.MaxX, all.MaxY - all.Height * 0.15f);
            }
            float x = head.CenterX;
            // Long twin tails can make the head mesh many times wider than the actual face.
            // Keep normal heads unchanged, but anchor oversized silhouettes to their open eyes.
            if (thumbnail && eyes.Valid && eyes.Width > 0.0001f &&
                (head.Width > eyes.Width * 4 || head.Height > eyes.Width * 3.2f))
                return new ChefPortraitFrame { X = eyes.CenterX, Y = eyes.CenterY + eyes.Width * 0.2f,
                    HalfSize = Math.Max(0.01f, Math.Max(eyes.Width * 1.4f, eyes.Height * 2.6f)) };
            if (thumbnail)
                return new ChefPortraitFrame { X = x, Y = head.CenterY - head.Height * 0.05f,
                    HalfSize = Math.Max(0.01f, Math.Max(head.Width * 0.56f, head.Height * 0.6f)) };
            // Keep the face centered even when a tail or prop extends to one side.
            float width = Math.Max(Math.Abs(all.MinX - x), Math.Abs(all.MaxX - x));
            return new ChefPortraitFrame { X = x, Y = all.CenterY,
                HalfSize = Math.Max(0.01f, Math.Max(width, all.Height * 0.5f) * 1.08f) };
        }
    }
}
