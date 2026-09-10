using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Configuration;

namespace SPTFreeAim.Compat
{
    /// <summary>
    /// Warn when one of the mod's hotkeys is a key Tarkov already uses.
    ///
    /// This exists because of a real hour lost (docs/07-FINDINGS.md F47). The
    /// owner had "Master toggle key = Alpha1", and Tarkov has:
    ///
    ///     SecondaryWeapon       = Alpha1
    ///     QuickSecondaryWeapon  = Alpha1
    ///
    /// SecondaryWeapon is the holster - the pistol. So drawing his pistol also
    /// toggled the whole mod off, every time, and the only trace was a pair of
    /// "Free aim ON" / "Free aim OFF" lines that looked like him pressing the
    /// toggle on purpose.
    ///
    /// Nothing was broken. Two independent config files each held a defensible
    /// value and neither could see the other, which is a collision no amount of
    /// care inside either one would catch.
    ///
    /// Tarkov's bindings live in a JSON file despite the .ini extension. This
    /// deliberately does NOT parse it as JSON: a regex over the text cannot throw
    /// on a schema change, and a startup warning is not worth a hard dependency
    /// or a chance of taking the plugin down (F30).
    /// </summary>
    public static class KeyConflicts
    {
        public static string Report = "not checked yet";

        private static readonly string[] Candidates =
        {
            @"SPT_Runtime\user\sptSettings\Control.ini",
            @"user\sptSettings\Control.ini",
            @"SPT_Data\user\sptSettings\Control.ini"
        };

        public static void CheckOnce(params KeyValuePair<string, KeyboardShortcut>[] ours)
        {
            try
            {
                string path = FindControlFile();
                if (path == null)
                {
                    Report = "Tarkov's control file not found - cannot check for key clashes";
                    Plugin.Log.LogInfo("Key clash check: " + Report);
                    return;
                }

                Dictionary<string, List<string>> gameKeys = ParseBindings(File.ReadAllText(path));

                var clashes = new List<string>();
                foreach (var mine in ours)
                {
                    string key = mine.Value.MainKey.ToString();
                    if (key == "None") continue;

                    List<string> actions;
                    if (!gameKeys.TryGetValue(key, out actions)) continue;

                    clashes.Add(string.Format("\"{0}\" is on {1}, which Tarkov also uses for: {2}",
                        mine.Key, key, string.Join(", ", actions.ToArray())));
                }

                if (clashes.Count == 0)
                {
                    Report = "no clashes with Tarkov's own bindings";
                    Plugin.Log.LogInfo("Key clash check: " + Report);
                    return;
                }

                Report = clashes.Count + " clash(es) with Tarkov's bindings";
                var sb = new StringBuilder();
                sb.AppendLine("KEY CLASH - one of this mod's hotkeys is a key Tarkov already uses.");
                sb.AppendLine("Pressing it will do BOTH things, every time:");
                foreach (string c in clashes) sb.AppendLine("    " + c);
                sb.Append("Change the mod's key in the F12 menu. See docs/07-FINDINGS.md F47.");
                Plugin.Log.LogWarning(sb.ToString());
            }
            catch (Exception e)
            {
                // A diagnostic must never be the reason something fails.
                Report = "check failed: " + e.GetType().Name;
                Plugin.Log.LogInfo("Key clash check: " + Report);
            }
        }

        private static string FindControlFile()
        {
            string root = null;
            try { root = BepInEx.Paths.GameRootPath; } catch { }
            if (string.IsNullOrEmpty(root)) return null;

            foreach (string rel in Candidates)
            {
                string full = Path.Combine(root, rel);
                if (File.Exists(full)) return full;
            }
            return null;
        }

        /// <summary>
        /// keyName -> the Tarkov actions bound to it.
        ///
        /// The file is a list of blocks shaped like
        ///     { "keyName": "SecondaryWeapon", "variants": [ { "keyCode": [ "Alpha1" ] } ] }
        /// so splitting on "keyName" and collecting the quoted tokens inside each
        /// block is enough. Modifier combinations list several keys; every one of
        /// them counts, because a bare press of any of them still reaches us.
        /// </summary>
        private static Dictionary<string, List<string>> ParseBindings(string text)
        {
            var map = new Dictionary<string, List<string>>();

            MatchCollection blocks = Regex.Matches(text,
                "\"keyName\"\\s*:\\s*\"([^\"]+)\"(.*?)(?=\"keyName\"|$)",
                RegexOptions.Singleline);

            foreach (Match b in blocks)
            {
                string action = b.Groups[1].Value;

                foreach (Match kc in Regex.Matches(b.Groups[2].Value,
                             "\"keyCode\"\\s*:\\s*\\[([^\\]]*)\\]", RegexOptions.Singleline))
                    foreach (Match k in Regex.Matches(kc.Groups[1].Value, "\"([^\"]+)\""))
                    {
                        string key = k.Groups[1].Value;
                        List<string> list;
                        if (!map.TryGetValue(key, out list)) map[key] = list = new List<string>();
                        if (!list.Contains(action)) list.Add(action);
                    }
            }

            return map;
        }
    }
}
