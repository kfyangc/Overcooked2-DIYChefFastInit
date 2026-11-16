using System;
using System.IO;
using OC2DIYChef;

static class ChefPreferenceRegression
{
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }

    static void Main()
    {
        var parsed = ChefPreferenceDocument.Parse("# note\r\n// note\n\tChef_Fox\tHAT=Festive\nChef_Cat HAT=Baseball\nChef_Fox HAT=None\n00-狐狸 HAT=巫师帽\nLOBBYSWITCHCHEF=TRUE\n");
        Check(parsed.Chefs.Length == 3 && parsed.Chefs[0].Key == "Chef_Fox" && parsed.Chefs[1].Key == "Chef_Cat" &&
            parsed.Chefs[2].Key == "00-狐狸", "comments, whitespace and duplicate names preserve the first occurrence and file order");
        Check(parsed.Chefs[0].Value == "Santa" && parsed.Chefs[1].Value == "Baseballcap" && parsed.Chefs[2].Value == "巫师帽",
            "legacy hat aliases and Unicode names keep the original preference format");
        Check(parsed.LobbySwitch && !ChefPreferenceDocument.Parse("LOBBYSWITCHCHEF=FALSE").LobbySwitch,
            "the lobby switch directive is configuration rather than a chef entry");
        Check(ChefPreferenceDocument.Parse("Chef_Fox\nChef_Cat HAT=None").Chefs[0].Value == "" &&
            ChefPreferenceDocument.Parse("").Chefs.Length == 0, "missing hats and empty files have their established defaults");

        string directory = Path.Combine(Path.GetTempPath(), "DIYChefPreference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "prefer.txt"), legacy = path + ".txt";
        try
        {
            var file = new ChefPreferenceFile(directory);
            ChefPreferenceDocument document;
            Check(file.TryRead(0, out document) && document.Chefs.Length == 0, "missing preferences start with the official-roster fallback");
            File.WriteAllText(legacy, "Chef_Cat");
            Check(!file.TryRead(4.99, out document), "changes are not polled before five unscaled seconds");
            Check(file.TryRead(5, out document) && document.Chefs[0].Key == "Chef_Cat", "the legacy prefer.txt.txt name remains supported");
            File.WriteAllText(path, "Chef_Fox HAT=Santa");
            Check(file.TryRead(10, out document) && document.Chefs[0].Key == "Chef_Fox", "prefer.txt takes precedence when it appears");
            DateTime timestamp = File.GetLastWriteTimeUtc(path);
            long length = new FileInfo(path).Length;
            File.WriteAllText(path, "Chef_Cat HAT=Fancy");
            File.SetLastWriteTimeUtc(path, timestamp);
            Check(new FileInfo(path).Length == length && file.TryRead(15, out document) && document.Chefs[0].Value == "Fancy",
                "same-size edits with unchanged timestamps are detected by content");
            Check(!file.TryRead(20, out document) && document == null, "unchanged files do not rebuild registrations or UI");
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                bool locked = false;
                try { file.TryRead(25, out document); }
                catch (IOException) { locked = true; }
                Check(locked, "an editor lock reports an IO failure without publishing an empty configuration");
            }
            File.WriteAllText(path, "Chef_Fox HAT=None");
            Check(!file.TryRead(29.9, out document), "failed reads still obey the five-second retry interval");
            Check(file.TryRead(30, out document) && document.Chefs[0].Value == "None", "unlocking the file permits the next poll to apply its update");
            file.RetryNextCheck();
            Check(!file.TryRead(34.9, out document) && file.TryRead(35, out document),
                "failed registration can retry unchanged content on the next scheduled check");
            File.Delete(path);
            Check(file.TryRead(40, out document) && document.Chefs[0].Key == "Chef_Cat", "deleting the primary file falls back to the legacy file");
            File.Delete(legacy);
            Check(file.TryRead(45, out document) && document.Chefs.Length == 0, "deleting both files updates the list to the official fallback");
            File.WriteAllText(path, "Chef_Fox\nChef_Cat HAT=None\nLOBBYSWITCHCHEF=TRUE");
            Check(file.TryRead(50, out document) && document.Chefs.Length == 2 && document.LobbySwitch,
                "recreating preferences after deletion updates the roster and lobby flag");
        }
        finally
        {
            File.Delete(path); File.Delete(legacy); Directory.Delete(directory);
        }
        Console.WriteLine("PASS: " + checks + " preference parsing, polling, precedence and retry checks; no game process used.");
    }
}
