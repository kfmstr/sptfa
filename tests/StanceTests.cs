using System;
using SPTFreeAim.Core;
using UnityEngine;

// Exercises the stance state machine. Separate from SpecTests because that one
// asserts against docs/01-SPEC.md's measurements; this asserts against the state
// machine's own rules, which are a design decision rather than a measurement.
//
// The reason this exists: F13. The stance gate had been wired to the output
// stage instead of the mechanism, every visible indicator said it worked, and
// the test passed because it asserted the wrong layer. So these check what the
// machine DECIDES, not what happens to be visible afterwards.
public static class StanceTests
{
    const float DT = 1f / 120f;
    static int _fail;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + "   " + detail);
        if (!ok) _fail++;
    }

    static StanceProfiles Profiles()
    {
        return new StanceProfiles
        {
            Down      = new StanceProfile { Pos = new Vector3(1,0,0), Coupling = 0f,   HandsRecovery = 2.5f, BlendSpeed = 6f },
            LowReady  = new StanceProfile { Pos = new Vector3(2,0,0), Coupling = 1f,   HandsRecovery = 1.5f, BlendSpeed = 6f },
            HighReady = new StanceProfile { Pos = new Vector3(3,0,0), Coupling = 0.8f, HandsRecovery = 1.5f, BlendSpeed = 6f },
            Shouldered= new StanceProfile { Pos = new Vector3(4,0,0), Coupling = 1f,   HandsRecovery = 0.5f, BlendSpeed = 6f },
        };
    }

    static StanceState Settle(StanceState s, bool aiming, float seconds)
    {
        var p = Profiles();
        for (int i = 0; i < (int)(seconds / DT); i++) s.Update(aiming, DT, p, true);
        return s;
    }

    public static void Main()
    {
        var p = Profiles();

        Console.WriteLine("=== state selection ===");

        var s = new StanceState();
        Settle(s, false, 1f);
        Check("default with the weapon up is LOW READY, not shouldered",
              s.Current == Stance.LowReady, s.Current.ToString() + "  [F12.2]");

        s = new StanceState(); Settle(s, true, 1f);
        Check("aiming shoulders it", s.Current == Stance.Shouldered, s.Current.ToString());

        s = new StanceState(); s.UserWantsDown = true; Settle(s, false, 1f);
        Check("stance key lowers it", s.Current == Stance.Down, s.Current.ToString());

        s = new StanceState(); s.UserWantsDown = true; Settle(s, true, 1f);
        Check("stance key beats aiming (cannot shoulder a lowered weapon)",
              s.Current == Stance.Down, s.Current.ToString());

        s = new StanceState(); s.SuspendSprinting = true; Settle(s, true, 1f);
        Check("a suspension beats everything, including aiming",
              s.Current == Stance.Down, s.Current.ToString());

        s = new StanceState(); s.UserWantsHighReady = true; Settle(s, false, 1f);
        Check("high ready is chosen when asked for",
              s.Current == Stance.HighReady, s.Current.ToString());

        s = new StanceState(); s.UserWantsHighReady = true; Settle(s, true, 1f);
        Check("aiming beats high ready", s.Current == Stance.Shouldered, s.Current.ToString());

        s = new StanceState(); Settle(s, false, 1f);
        for (int i = 0; i < 120; i++) s.Update(false, DT, p, false);
        Check("gating disabled pins LOW READY, so the coupling never gates off",
              s.Current == Stance.LowReady && s.Coupling > 0.99f,
              s.Current + " coupling " + s.Coupling.ToString("F2"));

        Console.WriteLine();
        Console.WriteLine("=== coupling follows the stance ===");

        s = new StanceState(); Settle(s, false, 2f);
        Check("low ready couples fully", s.Coupling > 0.99f, s.Coupling.ToString("F3"));

        s.UserWantsDown = true; Settle(s, false, 2f);
        Check("weapon down drops coupling to zero (F13)",
              s.Coupling < 0.01f, s.Coupling.ToString("F3"));

        s.UserWantsDown = false; Settle(s, false, 2f);
        Check("raising it again restores coupling", s.Coupling > 0.99f, s.Coupling.ToString("F3"));

        Console.WriteLine();
        Console.WriteLine("=== pose is reached over time, not snapped ===");

        s = new StanceState(); Settle(s, false, 2f);
        float atLowReady = s.PosePos.x;
        s.Update(true, DT, p, true);           // one frame of aiming
        float afterOneFrame = s.PosePos.x;
        Settle(s, true, 2f);
        float atShouldered = s.PosePos.x;

        Check("one frame moves the pose only slightly",
              Math.Abs(afterOneFrame - atLowReady) < 0.2f,
              string.Format("{0:F3} -> {1:F3}", atLowReady, afterOneFrame));
        Check("settling reaches the shouldered pose",
              Math.Abs(atShouldered - 4f) < 0.05f, atShouldered.ToString("F3"));

        Console.WriteLine();
        Console.WriteLine("=== arm stamina depends on stance ===");

        s = new StanceState(); Settle(s, true, 2f);
        float shoulderedRec = s.HandsRecovery;
        Settle(s, false, 2f);
        float lowReadyRec = s.HandsRecovery;
        s.UserWantsDown = true; Settle(s, false, 2f);
        float downRec = s.HandsRecovery;

        Check("recovery is worst shouldered, better at low ready, best down",
              shoulderedRec < lowReadyRec && lowReadyRec < downRec,
              string.Format("shouldered {0:F2} < low ready {1:F2} < down {2:F2}",
                            shoulderedRec, lowReadyRec, downRec));
        Check("that ordering is what gives low ready a reason to exist",
              shoulderedRec < 1f && downRec > 1f,
              string.Format("shouldered {0:F2} below stock, down {1:F2} above", shoulderedRec, downRec));

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "ALL STANCE CHECKS PASSED" : _fail + " CHECK(S) FAILED");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }
}
