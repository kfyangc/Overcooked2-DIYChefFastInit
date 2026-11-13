using System;

namespace OC2DIYChef
{
    internal static class ChefGridTransparency
    {
        // Some game UI shaders only write RGB. Two background passes recover alpha without
        // depending on their alpha output, and retain black clothing and translucent effects.
        internal static void Combine(byte[] black, byte[] white)
        {
            if (black == null || white == null || black.Length != white.Length || black.Length % 4 != 0)
                throw new ArgumentException("Expected equal RGBA pixel buffers");
            for (int i = 0; i < black.Length; i += 4)
            {
                int difference = Math.Max(0, Math.Max(white[i] - black[i],
                    Math.Max(white[i + 1] - black[i + 1], white[i + 2] - black[i + 2])));
                int alpha = 255 - difference;
                black[i + 3] = (byte)alpha;
                if (alpha == 255) continue;
                for (int c = 0; c < 3; c++)
                    black[i + c] = alpha == 0 ? (byte)0 : (byte)Math.Min(255, (black[i + c] * 255 + alpha / 2) / alpha);
            }
        }
    }
}
