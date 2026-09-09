using System;
using UnityEngine;

namespace SPTFreeAim.Compat
{
    /// <summary>
    /// Where the weapon actually is, measured off the weapon in your hands.
    ///
    /// This exists because asking the user to find the bore axis by eye was the
    /// wrong answer (docs/07-FINDINGS.md F21). The offset was being applied as a
    /// rotation about the weapon root's raw local X and Z, and the bore does not
    /// run along any of those axes in general - so the gun changed attitude
    /// without changing where it pointed. The barrel stayed square to the body
    /// and the whole weapon slid around instead of hinging.
    ///
    /// It does not have to be guessed. Every EFT weapon carries two named
    /// transforms that define its sight line:
    ///
    ///     mod_align_rear   (ProceduralWeaponAnimation.LINE_OF_SIGHT_P0)
    ///     mod_align_front  (ProceduralWeaponAnimation.LINE_OF_SIGHT_P1)
    ///
    /// rear -> front IS the bore direction, on this weapon, with these
    /// attachments, in the weapon root's own local space. Measured, not inferred
    /// from someone else's axis convention.
    ///
    /// The grip and stock are looked up the same way, from the slot objects the
    /// weapon is assembled from. When a weapon has no stock - a pistol, a folded
    /// AKS - the lookup simply fails for that one and the configured fallback
    /// stands in, which is the correct behaviour rather than a degraded one.
    /// </summary>
    public static class WeaponGeometry
    {
        private const string ALIGN_REAR = "mod_align_rear";
        private const string ALIGN_FRONT = "mod_align_front";

        private static readonly string[] GripNames =
            { "mod_pistol_grip", "mod_grip", "pistol_grip" };

        private static readonly string[] StockNames =
            { "mod_stock", "mod_stock_000", "mod_stock_001", "buttstock", "mod_stock_axis" };

        /// <summary>True when the sight line was found and the bore is real.</summary>
        public static bool HaveBore { get; private set; }
        public static bool HaveGrip { get; private set; }
        public static bool HaveStock { get; private set; }

        /// <summary>Unit vector down the bore, muzzle-ward, in weapon-root local space.</summary>
        public static Vector3 Bore { get; private set; }

        /// <summary>Grip and buttpad in weapon-root local space, when found.</summary>
        public static Vector3 Grip { get; private set; }
        public static Vector3 Stock { get; private set; }

        /// <summary>What the last measurement found, for the log and the HUD.</summary>
        public static string Report = "not measured yet";

        private static Transform _measuredFor;
        private static int _measuredChildCount = -1;

        public static void Forget()
        {
            _measuredFor = null;
            _measuredChildCount = -1;
            HaveBore = HaveGrip = HaveStock = false;
            Report = "not measured yet";
        }

        /// <summary>
        /// Measure once per weapon. Cheap to call every frame: it only does work
        /// when the root changes, or when the child count changes, which is what
        /// attaching or removing a mod looks like from here.
        /// </summary>
        public static void EnsureMeasured(Transform root)
        {
            if (root == null) return;

            int n = root.childCount;
            if (ReferenceEquals(root, _measuredFor) && n == _measuredChildCount) return;

            _measuredFor = root;
            _measuredChildCount = n;
            Measure(root);

            Plugin.Log.LogInfo("Weapon geometry: " + Report);
        }

        private static void Measure(Transform root)
        {
            HaveBore = HaveGrip = HaveStock = false;

            Transform rear = FindByName(root, ALIGN_REAR);
            Transform front = FindByName(root, ALIGN_FRONT);

            if (rear != null && front != null)
            {
                Vector3 a = root.InverseTransformPoint(rear.position);
                Vector3 b = root.InverseTransformPoint(front.position);
                Vector3 d = b - a;

                // A rear and front sight closer than a centimetre apart is not a
                // sight line, it is two transforms that happen to coincide.
                // Better to fall back than to normalise noise into an axis.
                if (d.magnitude > 0.01f)
                {
                    Bore = d.normalized;
                    HaveBore = true;
                }
            }

            Transform grip = FindByAnyPrefix(root, GripNames);
            if (grip != null) { Grip = root.InverseTransformPoint(grip.position); HaveGrip = true; }

            Transform stock = FindByAnyPrefix(root, StockNames);
            if (stock != null) { Stock = root.InverseTransformPoint(stock.position); HaveStock = true; }

            Report =
                (HaveBore ? "bore " + Fmt(Bore) : "<no sight line, using the configured axis>")
                + " | " + (HaveGrip ? "grip " + Fmt(Grip) : "grip not found")
                + " | " + (HaveStock ? "stock " + Fmt(Stock) : "no stock on this weapon")
                + " | " + root.name + " (" + root.childCount + " children)";
        }

        private static string Fmt(Vector3 v)
        {
            return string.Format("({0:F3}, {1:F3}, {2:F3})", v.x, v.y, v.z);
        }

        private static Transform FindByName(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (string.Equals(all[i].name, name, StringComparison.OrdinalIgnoreCase))
                    return all[i];
            return null;
        }

        private static Transform FindByAnyPrefix(Transform root, string[] prefixes)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int p = 0; p < prefixes.Length; p++)
                for (int i = 0; i < all.Length; i++)
                    if (all[i].name.StartsWith(prefixes[p], StringComparison.OrdinalIgnoreCase))
                        return all[i];
            return null;
        }
    }
}
