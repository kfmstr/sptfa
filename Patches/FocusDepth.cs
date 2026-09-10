using System;
using UnityEngine;

namespace SPTFreeAim.Patches
{
    /// <summary>
    /// The gun goes soft while your eye is focused downrange.
    ///
    /// The owner's hunch was right, and so was his reference frame: in the
    /// Bodycam-style footage the blur is a GRADIENT down the weapon. The
    /// receiver, nearest the eye, is heavily soft; the front sight, a good half
    /// metre further out, is nearly sharp; the world beyond is in focus.
    ///
    /// That gradient is the whole tell, and it rules out every per-renderer
    /// trick - swapping a material, or blurring "the weapon", gives one flat
    /// amount over the whole object. Only a depth-based effect blurs by how far
    /// each pixel sits from the plane of focus.
    ///
    /// The game already owns one. Not the PostProcessing v2 DepthOfField hanging
    /// off CameraManager - that one is only read by SetZBlur - but the legacy
    /// UnityStandardAssets image effect at EffectsController._dof, which
    /// CameraManager.ApplyFoV drives on every FOV change. It has `nearBlur`,
    /// which is the field this needs: blur things NEARER than focus. See F36 and
    /// Compat/GameRefs.cs.
    ///
    /// Focus follows the VIEW ray, not the bore. Free aim means the gun is
    /// usually pointing somewhere the eye is not, and the eye is what focuses.
    /// </summary>
    public static class FocusDepth
    {
        public struct Options
        {
            public bool Enabled;
            public float BlurSize;      // maxBlurSize - the strength dial, 0 = nothing
            public float Band;          // focalSize - depth of the sharp zone
            public float MaxDistance;   // focus here when the ray hits nothing
            public bool OnlyWhileAiming;
            public float Strength;      // scales BlurSize
        }

        /// <summary>
        /// The ray starts this far in front of the eye so it cannot hit the
        /// player's own weapon or arms. The distance is added back afterwards, so
        /// the focus plane still lands on the real surface. Nothing worth
        /// focusing on is closer than a metre.
        /// </summary>
        private const float NearSkip = 1.0f;

        public static string Status = "not started";
        public static float LastFocus;
        public static float LastBlur;
        public static bool Driving;

        public static void Frame(Camera cam, float aimBlend, Options o)
        {
            if (!o.Enabled)
            {
                if (Driving) Stop();
                return;
            }

            if (cam == null) { Status = "no camera yet"; return; }

            if (!Compat.GameRefs.HaveGameDof)
            {
                if (!Compat.GameRefs.ResolveGameDof())
                {
                    // Not fatal and not necessarily permanent - none of this
                    // exists outside a raid, so keep trying rather than latching
                    // a failure the way the first version did.
                    Status = Compat.GameRefs.DofWhyNot;
                    return;
                }
                Status = "driving the game's own DepthOfField";
            }

            float w = o.OnlyWhileAiming ? aimBlend : 1f;
            w = Mathf.Clamp01(w * o.Strength);

            if (w <= 0.001f)
            {
                if (Driving) Stop();
                return;
            }

            LastFocus = FocusDistance(cam, o.MaxDistance);

            // maxBlurSize is the strength dial, and it is the honest one: it is a
            // blur RADIUS, so 0 is unambiguously "no blur" and larger is
            // unambiguously "more". Ramping it with the aim blend means the
            // weapon eases out of focus as it comes up rather than snapping.
            LastBlur = o.BlurSize * w;

            Driving = true;
            Compat.GameRefs.DriveDepthOfField(LastFocus, o.Band, LastBlur);
        }

        private static void Stop()
        {
            Driving = false;
            LastBlur = 0f;
            Compat.GameRefs.ReleaseDepthOfField();
        }

        private static float FocusDistance(Camera cam, float max)
        {
            Transform t = cam.transform;
            Vector3 origin = t.position + t.forward * NearSkip;
            float reach = Mathf.Max(max - NearSkip, 1f);

            RaycastHit hit;
            if (Physics.Raycast(origin, t.forward, out hit, reach,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.distance + NearSkip;

            return max;   // nothing out there: focus at infinity, gun at its softest
        }

        public static void Release()
        {
            Stop();
            Status = "released";
        }
    }
}
