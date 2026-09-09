using System;
using SPTFreeAim.Core;
using UnityEngine;

// The two pieces of F20 that are pure maths: where the pivot ends up, and how
// the cone narrows on the side the buttstock cannot swing to.
//
// Both are worth testing precisely because neither is visible as a number in
// game. You see a gun swinging and you form an opinion. That is how F13 got
// through - every visible indicator agreed and the mechanism was wrong - so
// these assert on what the maths DECIDES.
public static class AnchorTests
{
    static int _fail;
    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + "   " + detail);
        if (!ok) _fail++;
    }

    static WeaponAnchors Rifle(float leeway = 0f)
    {
        return new WeaponAnchors
        {
            Grip = new Vector3(0.2f, 0.1f, 0f),
            Bore = new Vector3(0f, 1f, 0f),   // Y, the default
            StockBehind = 0.30f,
            LeftHandAhead = 0.30f,
            Leeway = leeway
        };
    }

    static FreeAimState.Tuning Tune(float inward = 0.55f, float sign = 1f)
    {
        return new FreeAimState.Tuning
        {
            Mode = DriveMode.Intercept,
            ConeDegrees = 10f,
            CapDegrees = 90f,
            SpringK = 5f,
            PushFactor = 0.5f,
            AimCoupling = 1f,
            DisengageBoost = 4f,
            InwardConeScale = inward,
            StrongSideSign = sign
        };
    }

    public static void Main()
    {
        var a = Rifle();

        Console.WriteLine("=== the three anchors sit where a rifle's do ===");

        Check("the support hand is ahead of the grip, down the bore",
              Math.Abs(a.LeftHand.y - (0.1f + 0.30f)) < 0.001f, a.LeftHand.ToString());
        Check("the buttpad is behind it, the same way",
              Math.Abs(a.Stock.y - (0.1f - 0.30f)) < 0.001f, a.Stock.ToString());
        Check("neither moves off the bore line",
              Math.Abs(a.LeftHand.x - a.Grip.x) < 0.001f && Math.Abs(a.Stock.x - a.Grip.x) < 0.001f,
              "x stays " + a.Grip.x.ToString("F2"));

        Console.WriteLine();
        Console.WriteLine("=== the pivot moves from grip to buttpad as the weapon comes up ===");

        Vector3 hip = a.Pivot(0f);
        Vector3 half = a.Pivot(0.5f);
        Vector3 shouldered = a.Pivot(1f);

        Check("not shouldered, the gun turns about the RIGHT HAND",
              (hip - a.Grip).magnitude < 0.001f, hip.ToString() + "  grip " + a.Grip);
        Check("shouldered, it turns about the BUTTPAD",
              (shouldered - a.Stock).magnitude < 0.001f, shouldered.ToString() + "  stock " + a.Stock);
        Check("and it slides between the two rather than jumping",
              half.y < hip.y && half.y > shouldered.y,
              string.Format("{0:F2} -> {1:F2} -> {2:F2}", hip.y, half.y, shouldered.y));

        Console.WriteLine();
        Console.WriteLine("=== leeway lets the braced hand give a little ===");

        var loose = Rifle(0.08f);
        Vector3 braced = Rifle(0f).Pivot(0f);
        Vector3 given = loose.Pivot(0f);

        Check("a little leeway slides the pivot toward the driving hand",
              given.y > braced.y, string.Format("{0:F3} -> {1:F3}", braced.y, given.y));
        Check("but only a little - it stays far nearer the grip than the support hand",
              (given - loose.Grip).magnitude < (given - loose.LeftHand).magnitude,
              string.Format("{0:F3} from grip, {1:F3} from support hand",
                            (given - loose.Grip).magnitude, (given - loose.LeftHand).magnitude));
        Check("zero leeway is a perfectly rigid brace",
              (Rifle(0f).Pivot(0f) - Rifle(0f).Grip).magnitude < 0.0001f, "exact");

        Console.WriteLine();
        Console.WriteLine("=== the cone is not centred on the body ===");

        var st = new FreeAimState();
        var t = Tune();

        float outward = st.EffectiveCone(new Vector2(-10f, 0f), t);
        float inward = st.EffectiveCone(new Vector2(10f, 0f), t);
        float vertical = st.EffectiveCone(new Vector2(0f, 10f), t);

        Check("swinging away from the body keeps the full cone",
              Math.Abs(outward - 10f) < 0.01f, outward.ToString("F2"));
        Check("swinging the stock into the body has less room",
              Math.Abs(inward - 5.5f) < 0.01f, inward.ToString("F2") + " (10 x 0.55)");
        Check("pure pitch is unaffected - the hip constrains yaw, not elevation",
              Math.Abs(vertical - 10f) < 0.01f, vertical.ToString("F2"));

        float diagonal = st.EffectiveCone(new Vector2(7.07f, 7.07f), t);
        Check("a diagonal is constrained in proportion, not all-or-nothing",
              diagonal > inward && diagonal < outward,
              string.Format("{0:F2} sits between {1:F2} and {2:F2}", diagonal, inward, outward));

        Console.WriteLine();
        Console.WriteLine("=== shouldering removes the constraint ===");

        st.SetAimBlend(1f);
        float shoulderedCone = st.EffectiveCone(new Vector2(10f, 0f), t);
        Check("once the stock is in the pocket there is no hip to hit",
              Math.Abs(shoulderedCone - 10f) < 0.01f, shoulderedCone.ToString("F2"));

        st.SetAimBlend(0f);
        Check("strong side is a switch, not a constant",
              Math.Abs(st.EffectiveCone(new Vector2(-10f, 0f), Tune(0.55f, -1f)) - 5.5f) < 0.01f,
              "left-handed: the constrained side flips");
        Check("scale 1.0 restores the old symmetric cone exactly",
              Math.Abs(st.EffectiveCone(new Vector2(10f, 0f), Tune(1f)) - 10f) < 0.001f, "10.00");

        Console.WriteLine();
        Console.WriteLine("=== the cant eases in and is frame-rate independent ===");

        var r1 = new FreeAimState(); r1.RollTarget = 45f;
        for (int i = 0; i < 120; i++) r1.UpdateRoll(1f / 120f, 12f);
        var r2 = new FreeAimState(); r2.RollTarget = 45f;
        for (int i = 0; i < 30; i++) r2.UpdateRoll(1f / 30f, 12f);

        Check("one second at 12/s is essentially there",
              r1.Roll > 44.5f, r1.Roll.ToString("F2"));
        Check("30 fps and 120 fps agree",
              Math.Abs(r1.Roll - r2.Roll) < 0.05f,
              string.Format("{0:F3} vs {1:F3}", r1.Roll, r2.Roll));

        var r3 = new FreeAimState(); r3.RollTarget = 45f;
        r3.UpdateRoll(1f / 120f, 12f);
        Check("a single frame moves it only slightly - it is a movement, not a snap",
              r3.Roll > 0f && r3.Roll < 5f, r3.Roll.ToString("F2"));

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "ALL ANCHOR CHECKS PASSED" : _fail + " CHECK(S) FAILED");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }
}
