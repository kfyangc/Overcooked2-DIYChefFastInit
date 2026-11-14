using System;
using System.IO;
using System.Text;
using OC2DIYChef;

static class ChefGridVisibilityRegression
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }

    static bool Equal(int[] a, params int[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    internal static int Run()
    {
        Check(ChefGridVisibility.IsBlink("Eyes2_Blinks") && ChefGridVisibility.IsBlink("Eyes2_Blinks 1"),
            "native eyelid bone suffixes are recognised");
        Check(!ChefGridVisibility.IsBlink("Eyes") && !ChefGridVisibility.IsBlink("Eyebrows") &&
            !ChefGridVisibility.IsBlink(null), "open eyes and eyebrows are never treated as eyelids");
        Check((ChefGridVisibility.VertexRole(0, 0, 1) & ChefGridVertexRole.Blink) != 0 &&
            (ChefGridVisibility.VertexRole(0, 0, 0.01f) & ChefGridVertexRole.Blink) != 0,
            "any positive eyelid influence marks a vertex");
        Check(ChefGridVisibility.VertexRole(0, 0, 0) == ChefGridVertexRole.None &&
            (ChefGridVisibility.VertexRole(1, 1, 0) & ChefGridVertexRole.Blink) == 0,
            "zero-weight and open-eye influences cannot hide vertices");
        // One merged renderer: open eyes, eyebrows, closed eyes, and a mixed triangle.
        var hidden = new[] { false, false, false, false, false, false, true, true, true };
        var original = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 1, 6, 2 };
        var visible = ChefGridVisibility.FilterTriangles(original, hidden);
        Check(Equal(visible, 0, 1, 2, 3, 4, 5), "only eyelid triangles are removed from a merged head");
        Check(original.Length == 12 && original[6] == 6, "shared input indices remain unchanged");
        Check(Equal(ChefGridVisibility.FilterTriangles(new[] { 2, 1, 0 }, new bool[3]), 2, 1, 0),
            "ordinary geometry preserves triangle winding");
        var submeshes = new[] { new[] { 5, 3, 1 }, new[] { 1, 3, 0 }, new int[0] };
        var used = ChefGridVisibility.CompactIndices(submeshes, 9);
        Check(Equal(used, 5, 3, 1, 0) && Equal(submeshes[0], 0, 1, 2) && Equal(submeshes[1], 2, 1, 3) &&
            submeshes[2].Length == 0, "compaction shares vertices across material slots and retains empty slots");
        Check(Equal(ChefGridVisibility.Compact(new[] { 10, 11, 12, 13, 14, 15, 16, 17, 18 }, used), 15, 13, 11, 10),
            "vertex attributes follow the same compact mapping as positions");
        Check(ChefGridVisibility.Compact(new float[0], used).Length == 0, "absent optional mesh attributes stay absent");
        var empty = new[] { ChefGridVisibility.FilterTriangles(new[] { 0, 1, 2 }, new[] { true, true, true }) };
        Check(empty[0].Length == 0 && ChefGridVisibility.CompactIndices(empty, 3).Length == 0,
            "a separate closed-eye mesh contributes no vertices to framing");
        // Optional read-only export from installed assets; no game/Unity process is needed.
        string fixture = Environment.GetEnvironmentVariable("DIY_CHEF_EYE_FIXTURE");
        if (!string.IsNullOrEmpty(fixture)) NativeFixtures(fixture);
        return checks;
    }

    static void NativeFixtures(string path)
    {
        using (var input = new BinaryReader(File.OpenRead(path)))
        {
            int count = input.ReadInt32();
            for (int part = 0; part < count; part++)
            {
                string name = Encoding.UTF8.GetString(input.ReadBytes(input.ReadInt32()));
                var bones = new ChefGridVertexRole[input.ReadInt32()];
                for (int i = 0; i < bones.Length; i++)
                    bones[i] = ChefGridVisibility.BoneRole(Encoding.UTF8.GetString(input.ReadBytes(input.ReadInt32())));
                var hidden = new bool[input.ReadInt32()];
                for (int i = 0; i < hidden.Length; i++)
                {
                    float head = 0, eyes = 0, blink = 0;
                    for (int slot = 0; slot < 4; slot++)
                    {
                        int bone = input.ReadInt32();
                        float weight = input.ReadSingle();
                        if (weight <= 0 || bone < 0 || bone >= bones.Length) continue;
                        if ((bones[bone] & ChefGridVertexRole.Head) != 0) head += weight;
                        if ((bones[bone] & ChefGridVertexRole.Eyes) != 0) eyes += weight;
                        if ((bones[bone] & ChefGridVertexRole.Blink) != 0) blink += weight;
                    }
                    hidden[i] = (ChefGridVisibility.VertexRole(head, eyes, blink) & ChefGridVertexRole.Blink) != 0;
                }
                var submeshes = new int[input.ReadInt32()][];
                int triangles = 0;
                for (int i = 0; i < submeshes.Length; i++)
                {
                    var indices = new int[input.ReadInt32()];
                    for (int j = 0; j < indices.Length; j++) indices[j] = input.ReadInt32();
                    submeshes[i] = ChefGridVisibility.FilterTriangles(indices, hidden);
                    triangles += submeshes[i].Length / 3;
                }
                Check(triangles == input.ReadInt32(), name + ": native triangle count matches independent asset analysis");
                var used = ChefGridVisibility.CompactIndices(submeshes, hidden.Length);
                foreach (int vertex in used) Check(!hidden[vertex], name + ": open-eye/head vertices are retained without eyelids");
                Console.WriteLine("Native eye fixture " + name + ": " + triangles + " triangles, " + used.Length + " vertices");
            }
            Check(input.BaseStream.Position == input.BaseStream.Length, "all native fixtures were consumed");
        }
    }
}
