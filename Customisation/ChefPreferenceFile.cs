using System;
using System.Collections.Generic;
using System.IO;

namespace OC2DIYChef
{
    internal sealed class ChefPreferenceDocument
    {
        internal KeyValuePair<string, string>[] Chefs;
        internal bool LobbySwitch;

        internal static ChefPreferenceDocument Parse(string text)
        {
            var chefs = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new ChefPreferenceDocument();
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                    if (line.StartsWith("LOBBYSWITCHCHEF="))
                    {
                        result.LobbySwitch = line.Substring("LOBBYSWITCHCHEF=".Length).Trim() == "TRUE";
                        continue;
                    }
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (!seen.Add(parts[0])) continue;
                    string hat = parts.Length > 1 && parts[1].StartsWith("HAT=") ? parts[1].Substring(4) : "";
                    if (hat == "Festive") hat = "Santa";
                    if (hat == "Baseball") hat = "Baseballcap";
                    chefs.Add(new KeyValuePair<string, string>(parts[0], hat));
                }
            }
            result.Chefs = chefs.ToArray();
            return result;
        }
    }

    // Unscaled time is supplied by the caller; read/parse happens only once per
    // five seconds. Comparing content also detects same-size, same-time edits.
    internal sealed class ChefPreferenceFile
    {
        readonly string directory;
        string previousPath, previousText;
        bool observed;
        double nextCheck = double.NegativeInfinity;

        internal ChefPreferenceFile(string directory) { this.directory = directory; }

        internal void RetryNextCheck() { observed = false; }

        internal bool TryRead(double now, out ChefPreferenceDocument document)
        {
            document = null;
            if (now < nextCheck) return false;
            nextCheck = now + 5;
            string path = Path.Combine(directory, "prefer.txt");
            if (!File.Exists(path)) path = Path.Combine(directory, "prefer.txt.txt");
            if (!File.Exists(path)) path = null;
            // If an editor temporarily locks the file, propagate the IO error
            // without advancing the applied snapshot; the next poll retries it.
            string text = path == null ? "" : File.ReadAllText(path);
            if (observed && path == previousPath && text == previousText) return false;
            document = ChefPreferenceDocument.Parse(text);
            previousPath = path; previousText = text; observed = true;
            return true;
        }
    }
}
