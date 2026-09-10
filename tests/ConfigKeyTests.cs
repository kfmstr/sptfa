using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

// Every config key, checked against the characters BepInEx refuses.
//
// docs/07-FINDINGS.md F32: a key was named "Keep peripheral vision when aiming
// [BROKEN]". Square brackets are illegal in a BepInEx section or key name, so
// ConfigFile.Bind threw, Awake died on its second statement, and the entire mod
// failed to start. A label meant to warn about a broken feature broke everything.
//
// This reads FreeAimConfig.cs as text rather than running it, because running it
// needs BepInEx and the game. The rule it enforces is a naming rule, and naming
// is visible in the source.
public static class ConfigKeyTests
{
    // From BepInEx.Configuration.ConfigDefinition.CheckInvalidConfigChars.
    private const string Illegal = "=\n\t\\\"'[]";

    static int _fail;
    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + "   " + detail);
        if (!ok) _fail++;
    }

    public static void Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "FreeAimConfig.cs";
        if (!File.Exists(path))
        {
            Console.WriteLine("FAIL  cannot find " + path);
            Environment.Exit(1);
        }

        string src = File.ReadAllText(path);

        // cfg.Bind(SECTION, "Key", ...
        MatchCollection ms = Regex.Matches(src, @"cfg\.Bind\(\s*(S_[A-Z]+)\s*,\s*""([^""]+)""");
        Console.WriteLine("=== " + ms.Count + " config keys ===");
        Check("the file actually contains config keys", ms.Count > 20, ms.Count + " found");

        var offenders = new List<string>();
        var seen = new Dictionary<string, string>();
        var dupes = new List<string>();

        foreach (Match m in ms)
        {
            string section = m.Groups[1].Value;
            string key = m.Groups[2].Value;

            foreach (char c in key)
                if (Illegal.IndexOf(c) >= 0)
                {
                    offenders.Add(key + "  (contains '" + c + "')");
                    break;
                }

            string id = section + "|" + key;
            if (seen.ContainsKey(id)) dupes.Add(id);
            else seen[id] = key;
        }

        Check("no key contains a character BepInEx refuses",
              offenders.Count == 0,
              offenders.Count == 0 ? "= \\n \\t \\ \" ' [ ] all absent"
                                   : string.Join("; ", offenders.ToArray()));

        // Binding the same section+key twice also throws, and is just as fatal.
        Check("no section and key pair is bound twice",
              dupes.Count == 0,
              dupes.Count == 0 ? "all unique" : string.Join("; ", dupes.ToArray()));

        // No Vector2/Vector3 config entries.
        //
        // docs/07-FINDINGS.md F39: the config UI draws a vector as three text
        // boxes and re-parses each on every keystroke, so a lone "-" has nothing
        // to parse and the character is discarded. The value was never clamped -
        // a negative simply could not be TYPED, which looks identical from the
        // outside and wasted a round of "are you sure you implemented this".
        //
        // Individual floats with signed AcceptableValueRanges get sliders, which
        // have no such problem. This keeps them from creeping back.
        MatchCollection vecs = Regex.Matches(src, @"ConfigEntry<(Vector[234])>\s+(\w+)");
        var vecNames = new List<string>();
        foreach (Match m in vecs) vecNames.Add(m.Groups[2].Value + " (" + m.Groups[1].Value + ")");

        Check("no config entry is a vector type",
              vecNames.Count == 0,
              vecNames.Count == 0 ? "all scalar"
                                  : "cannot type a minus into: " + string.Join("; ", vecNames.ToArray()));

        // Anything measured in metres or degrees is a direction as well as a
        // magnitude, so its range has to reach below zero.
        MatchCollection signed = Regex.Matches(src,
            @"cfg\.Bind\(\s*S_[A-Z]+\s*,\s*""([^""]*\((?:m|deg)\)[^""]*)""[\s\S]{0,600}?AcceptableValueRange<float>\(\s*(-?[\d.]+)f");
        // Three genuine magnitudes, named here rather than silently skipped. A
        // cone and a cap are radii - a negative cone is not a mirrored cone, it
        // is nothing - and a focus distance is how far out in front to look.
        // Where flipping IS meaningful there is an explicit invert switch.
        var magnitudes = new List<string>
        {
            "Cone size (deg)",
            "Hard cap (deg)",
            "Gun blur: max focus distance (m)"
        };

        var unsigned = new List<string>();
        int signedCount = 0;
        foreach (Match m in signed)
        {
            string key = m.Groups[1].Value;
            if (magnitudes.Contains(key)) continue;
            signedCount++;
            if (double.Parse(m.Groups[2].Value) >= 0) unsigned.Add(key);
        }

        Check("every (m) and (deg) dial reaches below zero",
              unsigned.Count == 0,
              unsigned.Count == 0 ? signedCount + " checked, all signed"
                                  : "positive-only: " + string.Join("; ", unsigned.ToArray()));

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "ALL CONFIG KEY CHECKS PASSED" : _fail + " CHECK(S) FAILED");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }
}
