using System;
using System.IO;
using System.Text;
using OC2DIYChef;

static class ChefGridFaceRegression
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
        var anchors = new ChefPortraitAnchors { Valid = true, CenterX = 0, NeckY = 0.5861373f, EyeY = 0.9141513f };
        var all = Box(-0.8f, 0, 0.8f, 1.8f);
        // Actual installed OBJ extents, with the importer's X inversion applied.
        var squirrel = Box(-0.471434f, 0.570455f, 0.455028f, 1.389129f);
        var squirrelEye = Box(-0.176223f, 0.909601f, -0.109596f, 1.017710f);
        var frame = ChefGridFraming.Calculate(all, squirrel, squirrelEye, anchors, true);
        Check(Near(frame.X, 0) && frame.HalfSize > 0.45f, "the squirrel's one visible eye cannot become a tiny, offset face crop");
        Check(frame.X - frame.HalfSize < squirrel.MinX && frame.X + frame.HalfSize > squirrel.MaxX,
            "the squirrel's eyepatch side stays inside the frame");
        var shifted = ChefGridFraming.Calculate(Box(1.4f, -1, 4.6f, 2.6f),
            Box(3 - 0.471434f * 2, -1 + 0.570455f * 2, 3 + 0.455028f * 2, -1 + 1.389129f * 2),
            Box(3 - 0.176223f * 2, -1 + 0.909601f * 2, 3 - 0.109596f * 2, -1 + 1.017710f * 2),
            new ChefPortraitAnchors { Valid = true, CenterX = 3, NeckY = anchors.NeckY * 2 - 1, EyeY = anchors.EyeY * 2 - 1 }, true);
        Check(Near(shifted.X, frame.X * 2 + 3) && Near(shifted.Y, frame.Y * 2 - 1) && Near(shifted.HalfSize, frame.HalfSize * 2),
            "single-eye framing follows translated/scaled skeletons rather than a hard-coded world origin");
        var turtle = Box(-0.406387f, 0.567944f, 0.377919f, 1.192411f);
        var turtleEye = Box(0.200678f, 0.922978f, 0.310214f, 1.071961f);
        frame = ChefGridFraming.Calculate(all, turtle, turtleEye, anchors, true);
        Check(Near(frame.X, 0) && frame.HalfSize > 0.4f && frame.X - frame.HalfSize < turtle.MinX,
            "the turtle's eye on the opposite side retains a centered face and eyepatch");
        var dragon = Box(-0.765713f, 0.508420f, 0.765713f, 1.366736f);
        var dragonEyes = Box(-0.257088f, 0.970471f, 0.257088f, 1.088098f);
        var oldDragon = ChefGridFraming.Calculate(all, dragon, true);
        frame = ChefGridFraming.Calculate(all, dragon, dragonEyes, anchors, true);
        Check(frame.HalfSize < oldDragon.HalfSize * 0.7f && Near(frame.X, 0),
            "dragon whiskers do not shrink the face into a full-body thumbnail");
        var detail = ChefGridFraming.Calculate(all, dragon, dragonEyes, anchors, false);
        Check(detail.X - detail.HalfSize < all.MinX && detail.X + detail.HalfSize > all.MaxX &&
            detail.Y - detail.HalfSize < all.MinY && detail.Y + detail.HalfSize > all.MaxY,
            "headshot limits never crop the right-hand full-body portrait");
        var face = new ChefPortraitBounds();
        ChefGridFraming.IncludeHead(ref face, -3, 0.2f, anchors); // Rigid Head.obj also contains torso/arms.
        ChefGridFraming.IncludeHead(ref face, 3, 0.4f, anchors);
        Check(!face.Valid, "a rigid head replacement's torso and arms do not contribute to head framing");
        ChefGridFraming.IncludeHead(ref face, -0.35f, 0.6f, anchors);
        ChefGridFraming.IncludeHead(ref face, 0.35f, 1.2f, anchors);
        Check(Near(face.MinY, 0.6f) && Near(face.Width, 0.7f), "the actual face remains available above the neck plane");
        var missingAnchors = new ChefPortraitBounds();
        ChefGridFraming.IncludeHead(ref missingAnchors, 1, 0.1f, new ChefPortraitAnchors());
        Check(missingAnchors.Valid, "nonstandard models without named skeleton anchors retain a usable geometry fallback");
        Check(ChefGridVisibility.BoneRole("Eyes") == (ChefGridVertexRole.Head | ChefGridVertexRole.Eyes) &&
            ChefGridVisibility.BoneRole("Hair") == ChefGridVertexRole.Head &&
            ChefGridVisibility.BoneRole("RightHand") == ChefGridVertexRole.None &&
            ChefGridVisibility.BoneRole("HatBase") == ChefGridVertexRole.None,
            "merged head measurements distinguish facial features from hands, body and hats");
        Check(ChefGridVisibility.VertexRole(0.1f, 0, 0) == ChefGridVertexRole.None &&
            (ChefGridVisibility.VertexRole(0.5f, 0, 0) & ChefGridVertexRole.Head) != 0,
            "minor head influences along a blended torso do not count as a face");
        string fixture = Environment.GetEnvironmentVariable("DIY_CHEF_FACE_FIXTURE");
        if (!string.IsNullOrEmpty(fixture)) RealFaces(fixture);
        return checks;
    }

    static void RealFaces(string path)
    {
        using (var input = new BinaryReader(File.OpenRead(path)))
        {
            int count = input.ReadInt32();
            Check(count >= 10, "the real-asset corpus includes native and custom chef cases");
            Console.WriteLine("Face corpus: " + count + " native/custom chefs");
            for (int sample = 0; sample < count; sample++)
            {
                string name = Encoding.UTF8.GetString(input.ReadBytes(input.ReadInt32()));
                var anchors = new ChefPortraitAnchors { Valid = true,
                    CenterX = input.ReadSingle(), NeckY = input.ReadSingle(), EyeY = input.ReadSingle() };
                var all = new ChefPortraitBounds();
                var legacyHead = new ChefPortraitBounds();
                var legacyEyes = new ChefPortraitBounds();
                var head = new ChefPortraitBounds();
                var eyes = new ChefPortraitBounds();
                int vertices = input.ReadInt32();
                for (int v = 0; v < vertices; v++)
                {
                    float x = input.ReadSingle(), y = input.ReadSingle();
                    var role = ChefGridVisibility.VertexRole(input.ReadSingle(), input.ReadSingle(), 0);
                    byte part = input.ReadByte();
                    all.Include(x, y);
                    if (part == 1)
                    {
                        legacyHead.Include(x, y);
                        if ((role & ChefGridVertexRole.Head) != 0) ChefGridFraming.IncludeHead(ref head, x, y, anchors);
                    }
                    if (part == 2) legacyEyes.Include(x, y);
                    if ((role & ChefGridVertexRole.Eyes) != 0) eyes.Include(x, y);
                }
                var frame = ChefGridFraming.Calculate(all, head, eyes, anchors, true);
                var before = ChefGridFraming.Calculate(all, legacyHead, legacyEyes, true);
                Check(head.Valid && frame.HalfSize > 0 && !float.IsNaN(frame.Y), name + ": a usable face is located");
                Check(head.MinY >= anchors.ChinY, name + ": below-neck geometry does not set the thumbnail bounds");
                if (eyes.Valid)
                    Check(eyes.MinX >= frame.X - frame.HalfSize && eyes.MaxX <= frame.X + frame.HalfSize &&
                        eyes.MinY >= frame.Y - frame.HalfSize && eyes.MaxY <= frame.Y + frame.HalfSize,
                        name + ": the visible eyes fit within the resulting crop");
                if (name == "Chef_dragon" || name == "Chef_strongman" || name == "135-GRIL_L_T1" || name == "19-Everpeckish")
                    Check(frame.HalfSize < before.HalfSize * 0.85f, name + ": the full-body thumbnail becomes a substantially larger face");
                if (name == "16-Squirrel_Grey" || name == "17-Turtle_pale" || name == "Chef_FemalePirate")
                    Check(Near(frame.X, anchors.CenterX) && frame.HalfSize > 0.35f, name + ": one eye cannot pull the crop onto one side");
                Console.WriteLine("Face fixture " + name + ": half-size " + frame.HalfSize.ToString("F3") +
                    " (previous " + before.HalfSize.ToString("F3") + "), center " + frame.X.ToString("F3") + "," + frame.Y.ToString("F3"));
            }
            Check(input.BaseStream.Position == input.BaseStream.Length, "all face fixtures were consumed");
        }
    }
}
