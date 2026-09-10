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

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "ALL CONFIG KEY CHECKS PASSED" : _fail + " CHECK(S) FAILED");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }
}
