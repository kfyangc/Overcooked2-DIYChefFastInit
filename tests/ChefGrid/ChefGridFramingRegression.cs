using System;
using OC2DIYChef;

static class ChefGridFramingRegression
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    static bool Near(float a, float b) { return Math.Abs(a - b) < 0.0001f; }
    static ChefPortraitBounds Box(float x0, float y0, float x1, float y1)
    {
        var bounds = new ChefPortraitBounds();
        bounds.Include(x0, y0); bounds.Include(x1, y1);
        return bounds;
    }

    internal static int Run()
    {
        // A short, offset fox head with a tall template hat and a tail extending to the right.
        var all = Box(-0.4f, 0, 1.2f, 2.2f);
        var head = Box(-0.4f, 0.8f, 0.3f, 1.4f);
        var thumbnail = ChefGridFraming.Calculate(all, head, true);
        var detail = ChefGridFraming.Calculate(all, head, false);
        Check(Near(thumbnail.X, -0.05f) && Near(detail.X, -0.05f), "face, rather than asymmetric full-body bounds, sets the horizontal center");
        Check(thumbnail.HalfSize * 2 < detail.HalfSize, "headshot is over twice the full-body scale in the same square");
        Check(head.MinX >= thumbnail.X - thumbnail.HalfSize && head.MaxX <= thumbnail.X + thumbnail.HalfSize &&
            head.MinY >= thumbnail.Y - thumbnail.HalfSize && head.MaxY <= thumbnail.Y + thumbnail.HalfSize,
            "zoom retains the complete head");
        Check(all.MinX >= detail.X - detail.HalfSize && all.MaxX <= detail.X + detail.HalfSize &&
            all.MinY >= detail.Y - detail.HalfSize && all.MaxY <= detail.Y + detail.HalfSize,
            "face-centered detail still contains the complete model");

        var tallHat = Box(-0.4f, 0, 1.2f, 4.4f);
        var withTallHat = ChefGridFraming.Calculate(tallHat, head, true);
        Check(Near(withTallHat.HalfSize, thumbnail.HalfSize) && Near(withTallHat.Y, thumbnail.Y),
            "tall hats cannot shrink or shift the thumbnail face");
        var longTail = ChefGridFraming.Calculate(Box(-0.4f, 0, 3, 2.2f), head, true);
        Check(Near(longTail.X, thumbnail.X) && Near(longTail.HalfSize, thumbnail.HalfSize),
            "tails cannot shrink or shift thumbnail faces");

        var shifted = ChefGridFraming.Calculate(Box(2.6f, -1, 4.2f, 1.2f), Box(2.6f, -0.2f, 3.3f, 0.4f), true);
        Check(Near(shifted.X, thumbnail.X + 3) && Near(shifted.Y, thumbnail.Y - 1) && Near(shifted.HalfSize, thumbnail.HalfSize),
            "model origin offsets do not change portrait size or relative centering");
        var wide = ChefGridFraming.Calculate(all, Box(-1, 0.8f, 1, 1.4f), true);
        Check(wide.X - wide.HalfSize < -1 && wide.X + wide.HalfSize > 1, "wide ears remain within the frame");
        var fallback = ChefGridFraming.Calculate(all, new ChefPortraitBounds(), true);
        Check(fallback.HalfSize > 0 && !float.IsNaN(fallback.X), "full body replacements without a named head have a usable fallback");
        var point = ChefGridFraming.Calculate(Box(1, 2, 1, 2), Box(1, 2, 1, 2), true);
        Check(point.HalfSize > 0 && Near(point.X, 1), "degenerate meshes cannot produce a zero camera size");
        bool rejected = false;
        try { ChefGridFraming.Calculate(new ChefPortraitBounds(), head, true); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "invisible geometry does not generate a bogus portrait");
        return checks;
    }
}
