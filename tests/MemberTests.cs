using System;
using System.Reflection;
using SPTFreeAim.Compat;

// Reflection-layer tests. These exist because of docs/07-FINDINGS.md F19: a
// redeclared property on an EFT type threw AmbiguousMatchException out of
// Member.Bind and killed the mod mid-raid.
//
// The shape matters, and my first attempt at this file got it wrong. Plain
// `new` shadowing with the SAME type does NOT throw - Mono picks the derived
// declaration without complaint, and a test built on that would have passed
// while guarding nothing. What throws is a redeclaration with a DIFFERENT
// type, which is exactly what the game does:
//
//   AbstractHandsController   public virtual Item   Item { get; }
//   FirearmController         public          Weapon Item { get; }
//
// Same name, narrower type, two separate vtable slots. So the fixtures below
// mirror that, and the first check proves the trap is still live - if a future
// runtime stops throwing, this file says so instead of quietly going hollow.
public static class MemberTests
{
    static int _fail;
    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + "   " + detail);
        if (!ok) _fail++;
    }

    // --- the F19 shape: same name, different type ---------------------------
    class Item { public virtual float Kg { get { return 1f; } } }
    class Weapon : Item { public override float Kg { get { return 42f; } } }

    class AbstractHandsController
    {
        public virtual Item Held { get { return new Item(); } }
        public float BaseOnly { get { return 7f; } }

        // Deliberately object, not bool, so the derived redeclaration below is
        // ambiguous the same way Held is. BoolProbe hit this too - its old
        // catch-all turned the exception into a silent "not found", which is
        // worse than a crash because nothing in the log says why.
        public virtual object Ready { get { return false; } }
    }

    class FirearmController : AbstractHandsController
    {
        // Narrower type under the same name. This is the one that throws.
        public new Weapon Held { get { return new Weapon(); } }
        public float DerivedOnly { get { return 3f; } }
        public new bool Ready { get { return true; } }
    }

    class WithField { public float Weight = 5f; }

    // A grenade is an Item but not a Weapon: binding TotalWeight against one
    // and then reusing it on the other is the second half of F19.
    class Grenade : Item { }

    public static void Main()
    {
        const BindingFlags ALL = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        Console.WriteLine("=== Member.Bind against a narrowed redeclaration (F19) ===");

        bool threw = false;
        try { typeof(FirearmController).GetProperty("Held", ALL); }
        catch (AmbiguousMatchException) { threw = true; }
        Check("a whole-hierarchy lookup really is ambiguous here",
              threw, threw ? "throws, as FirearmController.Item did in game"
                           : "did NOT throw - guard is now untested, fix the fixture");

        var m = new Member("held", "Held");
        bool bound = m.Bind(typeof(FirearmController));
        Check("Member.Bind survives it", bound, m.Describe());

        object held = m.Get(new FirearmController());
        Check("and picks the most-derived declaration",
              held is Weapon, held == null ? "null" : held.GetType().Name + " (want Weapon, base gives Item)");

        Console.WriteLine();
        Console.WriteLine("=== rebinding to a different type does not keep the old one ===");

        var w = new Member("weight", "Kg");
        w.Bind(typeof(Weapon));
        float asWeapon = w.Get(new Weapon(), 0f);
        w.Bind(typeof(Grenade));
        float asGrenade = w.Get(new Grenade(), 0f);
        Check("a rebound Member reads the new type, not the old",
              Math.Abs(asWeapon - 42f) < 0.001f && Math.Abs(asGrenade - 1f) < 0.001f,
              string.Format("weapon {0:F1}, grenade {1:F1}", asWeapon, asGrenade));

        var stale = new Member("stale", "DerivedOnly");
        stale.Bind(typeof(FirearmController));
        bool rebound = stale.Bind(typeof(AbstractHandsController));
        Check("a failed rebind clears the binding instead of leaving a stale one",
              !rebound && !stale.Resolved, stale.Describe());

        Console.WriteLine();
        Console.WriteLine("=== ordinary cases still work ===");

        var b = new Member("base only", "BaseOnly");
        Check("a member declared only on the base still binds",
              b.Bind(typeof(FirearmController)) && Math.Abs(b.Get(new FirearmController(), 0f) - 7f) < 0.001f,
              b.Describe());

        var d = new Member("derived only", "DerivedOnly");
        Check("a member declared only on the derived type binds",
              d.Bind(typeof(FirearmController)) && Math.Abs(d.Get(new FirearmController(), 0f) - 3f) < 0.001f,
              d.Describe());

        var f = new Member("field", "Held", "Weight");
        Check("falls through to the next candidate name, and finds a field",
              f.Bind(typeof(WithField)) && Math.Abs(f.Get(new WithField(), 0f) - 5f) < 0.001f,
              f.Describe());

        var missing = new Member("nope", "NoSuchMember");
        Check("a genuinely missing member reports missing rather than throwing",
              !missing.Bind(typeof(FirearmController)) && !missing.Resolved,
              missing.Describe());

        Console.WriteLine();
        Console.WriteLine("=== BoolProbe, same trap ===");

        bool probeTrapLive = false;
        try { typeof(FirearmController).GetProperty("Ready", ALL); }
        catch (AmbiguousMatchException) { probeTrapLive = true; }
        Check("the bool lookup is ambiguous too", probeTrapLive,
              probeTrapLive ? "throws" : "did NOT throw - guard is now untested");

        var probe = new BoolProbe("ready", "Ready");
        probe.TryResolve(new FirearmController());
        Check("BoolProbe resolves it instead of silently missing it",
              probe.Resolved && probe.Read(new FirearmController()),
              probe.Describe() + "  (base says false, derived says true)");

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "ALL MEMBER CHECKS PASSED" : _fail + " CHECK(S) FAILED");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }
}
