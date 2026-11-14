using System;
using OC2DIYChef;

static class ChefGridTransparencyRegression
{
    internal static int Run()
    {
        // Include opaque black clothing, RGB-only shader outputs and partial coverage at edges.
        byte[] black = { 0, 0, 0, 255, 0, 0, 0, 0, 200, 30, 10, 0, 50, 20, 0, 0 };
        byte[] white = { 255, 255, 255, 255, 0, 0, 0, 0, 200, 30, 10, 0, 177, 147, 127, 0 };
        ChefGridTransparency.Combine(black, white);
        if (black[0] != 0 || black[1] != 0 || black[2] != 0 || black[3] != 0)
            throw new Exception("Background must be fully transparent regardless of camera alpha output");
        if (black[4] != 0 || black[5] != 0 || black[6] != 0 || black[7] != 255)
            throw new Exception("Black clothing must remain opaque");
        if (black[8] != 200 || black[9] != 30 || black[10] != 10 || black[11] != 255)
            throw new Exception("RGB-only character shaders must produce a visible opaque face");
        if (black[12] != 100 || black[13] != 40 || black[14] != 0 || black[15] != 128)
            throw new Exception("Partially covered edges need straight RGB and fractional alpha");
        bool rejected = false;
        try { ChefGridTransparency.Combine(new byte[4], new byte[8]); }
        catch (ArgumentException) { rejected = true; }
        if (!rejected) throw new Exception("Mismatched pixel buffers must be rejected");
        return 5;
    }
}
