using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

// Every Member that is READ must also be BOUND somewhere.
//
// docs/07-FINDINGS.md F45, and F29 before it. Compat/Member.Get does not bind on
// demand: an unresolved Member returns null from every read, forever, without
// throwing. That is indistinguishable from a member that is legitimately null, so
// the feature just quietly does nothing.
//
//   F29  M_CurrentScope was never bound -> every optic effect silently inert
//   F45  M_Physical was never bound     -> arm drain AND hold-breath zoom inert
//
// Twice is a pattern, and it is a pattern a text scan can catch: if the source
// contains M_Foo.Get( or M_Foo.Set( but never M_Foo.Bind(, that member can never
// resolve. Reflection cannot be checked at compile time, so this checks the one
// thing that is visible - that somebody, somewhere, remembered to bind it.
public static class BindTests
{
    static int _fail;
    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + "   " + detail);
        if (!ok) _fail++;
    }

    /// <summary>
    /// Remove comments before scanning.
    ///
    /// Written after the first regression attempt for this very test PASSED on a
    /// deliberately broken copy: the bug had been reintroduced by commenting the
    /// Bind call out, and the scanner counted the commented text as a real call.
    /// A scanner that reads dead code as live code is worse than no scanner,
    /// because it reports confidence it has not earned.
    /// </summary>
    static string StripComments(string s)
    {
        s = Regex.Replace(s, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        s = Regex.Replace(s, @"//[^\n]*", " ");
        return s;
    }

    public static void Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "Compat/GameRefs.cs";
        if (!File.Exists(path))
        {
            Console.WriteLine("FAIL  cannot find " + path);
            Environment.Exit(1);
        }

        string src = StripComments(File.ReadAllText(path));

        var declared = new HashSet<string>();
        foreach (Match m in Regex.Matches(src, @"Member\s+(M_\w+)\s*="))
            declared.Add(m.Groups[1].Value);

        var read = new HashSet<string>();
        foreach (Match m in Regex.Matches(src, @"\b(M_\w+)\s*\.\s*(?:Get|Set)\s*[<(]"))
            read.Add(m.Groups[1].Value);

        var bound = new HashSet<string>();
        foreach (Match m in Regex.Matches(src, @"\b(M_\w+)\s*\.\s*Bind\s*\("))
            bound.Add(m.Groups[1].Value);

        Console.WriteLine("=== " + declared.Count + " Members declared, "
                          + read.Count + " read, " + bound.Count + " bound ===");

        Check("the scan actually found Members", declared.Count > 5,
              declared.Count + " declared");

        var unbound = new List<string>();
        foreach (string r in read)
            if (!bound.Contains(r)) unbound.Add(r);
        unbound.Sort();

        Check("every Member that is read is also bound somewhere",
              unbound.Count == 0,
              unbound.Count == 0
                  ? "no silent nulls"
                  : "READ BUT NEVER BOUND: " + string.Join(", ", unbound.ToArray()));

        // A Member nobody reads is dead weight, not a bug - report without failing.
        var unread = new List<string>();
        foreach (string d in declared)
            if (!read.Contains(d)) unread.Add(d);
        unread.Sort();
        if (unread.Count > 0)
            Console.WriteLine("  note   declared but never read: " + string.Join(", ", unread.ToArray()));

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "ALL BIND CHECKS PASSED" : _fail + " CHECK(S) FAILED");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }
}
