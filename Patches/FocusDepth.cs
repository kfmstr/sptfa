using System;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace SPTFreeAim.Patches
{
    /// <summary>
    /// Focus on the target, and let the gun go soft.
    ///
    /// A real camera depth of field, not an approximation. I told the owner this
    /// was unreachable because I had only looked inside Assembly-CSharp; the
    /// game ships Unity's Post Processing Stack v2 as its own assembly, and its
    /// DepthOfField effect takes exactly the three numbers a real lens does
    /// (docs/07-FINDINGS.md F28).
    ///
    /// The focus distance is not a setting. It is a raycast down the middle of
    /// the screen, so the plane of focus lands on whatever you are actually
    /// looking at - which is the whole point. Look at a wall two metres away and
    /// the gun sharpens up; look down a street and it melts.
    ///
    /// This is the one feature here that costs frames. A DOF pass is real work
    /// for the GPU, which is why it is OFF by default and why the thread the
    /// owner linked spent half its comments arguing about exactly that.
    /// </summary>
    public static class FocusDepth
    {
        private static GameObject _host;
        private static PostProcessVolume _volume;
        private static PostProcessProfile _profile;
        private static DepthOfField _dof;
        private static bool _failed;

        public static string Status = "not started";
        public static float LastFocus;

        public struct Options
        {
            public bool Enabled;
            public float Aperture;      // lower = shallower depth of field = more blur
            public float FocalLength;   // mm
            public float MaxDistance;   // where to focus when the ray hits nothing
            public bool OnlyWhileAiming;
            public float Strength;      // 0..1, blended by the aim blend
        }

        public static void Frame(Camera cam, float aimBlend, Options o)
        {
            if (!o.Enabled || _failed)
            {
                if (_volume != null) _volume.weight = 0f;
                return;
            }

            if (_host == null && !Build(cam)) return;

            float w = o.OnlyWhileAiming ? aimBlend * o.Strength : o.Strength;
            _volume.weight = Mathf.Clamp01(w);
            if (_volume.weight <= 0.001f) return;

            _dof.aperture.value = o.Aperture;
            _dof.focalLength.value = o.FocalLength;
            _dof.focusDistance.value = FocusDistance(cam, o.MaxDistance);
            LastFocus = _dof.focusDistance.value;
        }

        /// <summary>
        /// Where the eye is focused: straight down the middle of the view, to
        /// whatever is there. Nothing hit means focus at the far limit, which is
        /// what looking at open sky does.
        ///
        /// Deliberately not the gun's bearing. Free aim means the gun is often
        /// pointing somewhere the eye is not, and the eye is what focuses.
        /// </summary>
        private static float FocusDistance(Camera cam, float max)
        {
            RaycastHit hit;
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, max))
                return Mathf.Max(hit.distance, 0.3f);
            return max;
        }

        private static bool Build(Camera cam)
        {
            try
            {
                if (cam == null) { Status = "no camera yet"; return false; }

                // The volume has to sit on a layer the game's own PostProcessLayer
                // is watching, or it is simply ignored. Read the mask rather than
                // guessing a layer number.
                PostProcessLayer layer = cam.GetComponent<PostProcessLayer>()
                                         ?? UnityEngine.Object.FindObjectOfType<PostProcessLayer>();
                if (layer == null)
                {
                    _failed = true;
                    Status = "no PostProcessLayer on the camera - DOF unavailable";
                    Plugin.Log.LogWarning("Depth of field: " + Status);
                    return false;
                }

                int mask = layer.volumeLayer.value;
                int useLayer = -1;
                for (int i = 0; i < 32; i++)
                    if ((mask & (1 << i)) != 0) { useLayer = i; break; }

                if (useLayer < 0)
                {
                    _failed = true;
                    Status = "PostProcessLayer watches no layers - DOF unavailable";
                    Plugin.Log.LogWarning("Depth of field: " + Status);
                    return false;
                }

                _profile = ScriptableObject.CreateInstance<PostProcessProfile>();
                _profile.hideFlags = HideFlags.HideAndDontSave;

                _dof = _profile.AddSettings<DepthOfField>();
                _dof.enabled.Override(true);
                _dof.focusDistance.Override(10f);
                _dof.aperture.Override(2.8f);
                _dof.focalLength.Override(50f);
                _dof.kernelSize.Override(KernelSize.Medium);

                _host = new GameObject("sptfa_focus");
                _host.layer = useLayer;
                _host.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(_host);

                _volume = _host.AddComponent<PostProcessVolume>();
                _volume.isGlobal = true;
                _volume.sharedProfile = _profile;
                _volume.weight = 0f;

                // Above the game's own volumes so ours is what decides the focus
                // while it is weighted in, and irrelevant when its weight is zero.
                _volume.priority = 1000f;

                Status = "active on layer " + LayerMask.LayerToName(useLayer) + " (" + useLayer + ")";
                Plugin.Log.LogInfo("Depth of field: " + Status);
                return true;
            }
            catch (Exception e)
            {
                _failed = true;
                Status = "failed: " + e.Message;
                Plugin.Log.LogError("Depth of field could not be built, switching it off: " + e);
                return false;
            }
        }

        /// <summary>
        /// Tear the volume down. It is marked DontDestroyOnLoad, so without this
        /// it would outlive the raid and keep blurring the menu.
        /// </summary>
        public static void Release()
        {
            try
            {
                if (_volume != null) _volume.weight = 0f;
                if (_host != null) UnityEngine.Object.Destroy(_host);
                if (_profile != null) UnityEngine.Object.Destroy(_profile);
            }
            catch { }
            _host = null; _volume = null; _profile = null; _dof = null;
            Status = "released";
        }
    }
}
