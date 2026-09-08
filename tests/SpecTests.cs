using System;
using SPTFreeAim.Core;
using UnityEngine;

// Exercises FreeAimState against the measured behaviours in docs/01-SPEC.md.
// Not a unit-test framework, just a harness that prints pass/fail so the core
// mechanic is validated before it ever reaches a raid.
public static class SpecTests
{
    const float DT = 1f / 120f;

    static FreeAimState.Tuning Tune(DriveMode mode, float cone = 10f, float k = 5f, float push = 0.5f, float cap = 90f)
    {
        return new FreeAimState.Tuning
        {
            Mode = mode,
            ConeDegrees = cone,
            CapDegrees = cap,
            SpringK = k,
            PushFactor = push,
            AimCoupling = 1f,
            DisengageBoost = 4f
        };
    }

    static int _fail;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + "   " + detail);
        if (!ok) _fail++;
    }

    /// <summary>Turn the mouse at a steady rate for a given time, return final state.</summary>
    static FreeAimState Turn(DriveMode mode, float degPerSec, float seconds, FreeAimState.Tuning t, FreeAimState st = null)
    {
        if (st == null) { st = new FreeAimState(); st.Step(Vector2.zero, DT, t); }

        Vector2 raw = new Vector2(st.Gun.x, st.Gun.y);
        int steps = (int)(seconds / DT);
        for (int i = 0; i < steps; i++)
        {
            // In Compensate the game moves raw by the full mouse delta.
            // In Intercept we write the body back, so raw restarts from Body.
            if (mode == DriveMode.Intercept) raw = st.Body;
            raw = new Vector2(raw.x + degPerSec * DT, raw.y);
            st.Step(raw, DT, t);
        }
        return st;
    }

    static FreeAimState Settle(DriveMode mode, FreeAimState st, FreeAimState.Tuning t, float seconds)
    {
        Vector2 raw = mode == DriveMode.Intercept ? st.Body : st.Gun;
        int steps = (int)(seconds / DT);
        for (int i = 0; i < steps; i++)
        {
            if (mode == DriveMode.Intercept) raw = st.Body;
            st.Step(raw, DT, t);
        }
        return st;
    }

    public static void Main()
    {
        foreach (DriveMode mode in new[] { DriveMode.Compensate, DriveMode.Intercept })
        {
            Console.WriteLine();
            Console.WriteLine("=== " + mode + " ===");
            var t = Tune(mode);

            // [spec] "Slow mouse movement: no visible offset builds"
            var slow = Turn(mode, 10f, 2f, t);
            Check("slow turn (10 deg/s) builds no visible offset",
                  slow.Offset.magnitude < 3f,
                  string.Format("offset {0:F2} deg", slow.Offset.magnitude));

            // [spec] "Moderate steady turn: gun leads by the cone size"
            var mod = Turn(mode, 60f, 2f, t);
            Check("moderate turn (60 deg/s) parks near the cone",
                  mod.Offset.magnitude > 6f && mod.Offset.magnitude < 16f,
                  string.Format("offset {0:F2} deg, cone 10", mod.Offset.magnitude));

            // [spec] "Hard flick: gun reaches ~90 deg"
            var flick = Turn(mode, 900f, 0.25f, t);
            Check("hard flick approaches the cap without exceeding it",
                  flick.Offset.magnitude > 25f && flick.Offset.magnitude <= 90.5f,
                  string.Format("offset {0:F2} deg, cap 90", flick.Offset.magnitude));

            // [spec] "Stop moving: body always converges exactly onto the gun bearing"
            var stopped = Settle(mode, flick, t, 1.0f);
            Check("converges to ~zero within 1s of stopping",
                  stopped.Offset.magnitude < 1.0f,
                  string.Format("offset {0:F3} deg after 1.0s", stopped.Offset.magnitude));

            var settledLong = Settle(mode, stopped, t, 2f);
            Check("offset does not linger (unlike lualeet's accumulator)",
                  settledLong.Offset.magnitude < 0.05f,
                  string.Format("offset {0:F4} deg after 3s", settledLong.Offset.magnitude));

            // Hard cap holds under an absurd input
            var absurd = Turn(mode, 5000f, 0.5f, t);
            Check("cap holds under an absurd flick",
                  absurd.Offset.magnitude <= 90.5f,
                  string.Format("offset {0:F2} deg", absurd.Offset.magnitude));
        }

        // -- wrapping ---------------------------------------------------
        Console.WriteLine();
        Console.WriteLine("=== angle wrapping ===");
        Check("Wrap180(190) == -170", Math.Abs(AngleMath.Wrap180(190f) + 170f) < 0.001f,
              AngleMath.Wrap180(190f).ToString("F2"));
        Check("Wrap180(-190) == 170", Math.Abs(AngleMath.Wrap180(-190f) - 170f) < 0.001f,
              AngleMath.Wrap180(-190f).ToString("F2"));
        Check("Delta across the 180 seam is short",
              Math.Abs(AngleMath.Delta(new Vector2(179f, 0f), new Vector2(-179f, 0f)).x - 2f) < 0.001f,
              AngleMath.Delta(new Vector2(179f, 0f), new Vector2(-179f, 0f)).x.ToString("F2"));

        var seam = new FreeAimState();
        var tw = Tune(DriveMode.Compensate);
        seam.Step(new Vector2(175f, 0f), DT, tw);
        for (int i = 0; i < 60; i++) seam.Step(new Vector2(AngleMath.Wrap180(175f + i * 0.5f), 0f), DT, tw);
        Check("crossing the 180 seam does not spin the body",
              seam.Offset.magnitude < 20f,
              string.Format("offset {0:F2} deg", seam.Offset.magnitude));

        // -- spring shape matches the spec's stated convergence ----------
        Console.WriteLine();
        Console.WriteLine("=== spring ===");
        float f = AngleMath.SpringFactor(5f, 1f);
        Check("k=5 gives ~99% convergence in 1s", f > 0.98f && f < 1.0f, string.Format("{0:P2}", f));
        float lo = AngleMath.SpringFactor(5f, 1f / 30f);
        float hi = AngleMath.SpringFactor(5f, 1f / 240f);
        // Same wall-clock decay regardless of frame rate: (1-f)^n over 1s should match.
        double decay30 = Math.Pow(1 - lo, 30);
        double decay240 = Math.Pow(1 - hi, 240);
        Check("frame-rate independent (30fps vs 240fps residual)",
              Math.Abs(decay30 - decay240) < 0.005,
              string.Format("{0:F4} vs {1:F4}", decay30, decay240));

        // -- gate --------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine("=== stance gate ===");
        var gated = Turn(DriveMode.Compensate, 200f, 0.5f, tw);
        float before = gated.AppliedOffset(tw).magnitude;
        for (int i = 0; i < 240; i++) gated.UpdateGate(false, DT, 4f);
        float after = gated.AppliedOffset(tw).magnitude;
        Check("gate off suppresses the applied offset", after < 0.01f && before > 1f,
              string.Format("{0:F2} -> {1:F4} deg", before, after));

        // -- disengaged pass-through (gun down / sprint / knife / grenade) --
        Console.WriteLine();
        Console.WriteLine("=== disengaged pass-through ===");
        {
            var st = Turn(DriveMode.Intercept, 200f, 0.5f, tw);
            // Capture before stepping: Step() mutates st, and reading the offset
            // afterwards would report a value the assertion never saw.
            float engagedOffset = st.Offset.magnitude;
            bool engagedWrites = st.Step(st.Body, DT, Tune(DriveMode.Intercept)).HasValue;
            Check("engaged: offset is open and Intercept writes the body",
                  engagedOffset > 1f && engagedWrites,
                  string.Format("offset {0:F2} deg, writes={1}", engagedOffset, engagedWrites));

            for (int i = 0; i < 240; i++) st.UpdateGate(false, DT, 4f);   // gun goes down

            var ti = Tune(DriveMode.Intercept);
            Vector2 raw = st.Body;
            Vector2? wrote = null;
            for (int i = 0; i < 30; i++) { raw = new Vector2(raw.x + 300f * DT, raw.y); wrote = st.Step(raw, DT, ti); }

            Check("disengaged: nothing is written back, so the game's bearing stands",
                  !wrote.HasValue, wrote.HasValue ? "still writing" : "no write");
            Check("disengaged: no offset builds while looking around",
                  st.Offset.magnitude < 0.001f,
                  string.Format("offset {0:F4} deg", st.Offset.magnitude));
            Check("disengaged: view tracks the mouse 1:1",
                  Math.Abs(AngleMath.Delta(st.Body, raw).magnitude) < 0.001f,
                  string.Format("body-vs-raw {0:F4} deg", AngleMath.Delta(st.Body, raw).magnitude));

            // and it re-engages cleanly
            for (int i = 0; i < 240; i++) st.UpdateGate(true, DT, 4f);
            var re = st.Step(new Vector2(raw.x + 2f, raw.y), DT, ti);
            Check("re-engages without a jump",
                  re.HasValue && st.Offset.magnitude < 5f,
                  string.Format("offset {0:F2} deg on the frame after re-engaging", st.Offset.magnitude));
        }

        // -- aim coupling (Q2) -------------------------------------------
        var aimT = Tune(DriveMode.Compensate);
        aimT.AimCoupling = 0.25f;
        var aimSt = Turn(DriveMode.Compensate, 200f, 0.5f, aimT);
        for (int i = 0; i < 240; i++) aimSt.UpdateAimBlend(true, DT);
        Check("aim coupling 0.25 reduces the applied offset to ~a quarter",
              Math.Abs(aimSt.AppliedOffset(aimT).magnitude / aimSt.Offset.magnitude - 0.25f) < 0.02f,
              string.Format("ratio {0:F3}", aimSt.AppliedOffset(aimT).magnitude / aimSt.Offset.magnitude));

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "ALL CHECKS PASSED" : _fail + " CHECK(S) FAILED");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }
}
