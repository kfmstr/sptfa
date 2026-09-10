using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SPTFreeAim.Compat
{
    /// <summary>
    /// Glass effects INSIDE the scope, not across the whole screen.
    ///
    /// The owner, looking at the first working version of the lens glare: "you
    /// are applying it to the entire view, not on the transparent lense
    /// material, perhaps would be most appropriate on the sniper scope view
    /// (since it is view in the view)."
    ///
    /// He is right, and the game is built for it. The scope image is rendered by
    /// its own camera with its own PostProcessing v2 stack:
    ///
    ///     OpticCameraManager.Init:
    ///         GetComponent&lt;PostProcessVolume&gt;()  -> _postProcessVolume
    ///         GetComponent&lt;PostProcessLayer&gt;()   -> _postProcessLayer
    ///     OpticCameraManager.SetSSR:
    ///         _postProcessVolume.profile.TryGetSettings&lt;ScreenSpaceReflections&gt;(...)
    ///
    /// F48's dump showed that profile contains ScreenSpaceReflections and nothing
    /// else, so the effects have to be CREATED and added rather than switched on.
    /// PostProcessProfile carries non-generic AddSettings(Type) /
    /// RemoveSettings(Type) / HasSettings(Type), so that needs no generic-method
    /// reflection - which is the only reason this is forty lines rather than two
    /// hundred.
    ///
    /// This one mechanism covers all three cues from the reference footage (F43):
    ///     Bloom              - the bright hotspot on the glass
    ///     ChromaticAberration- the colour fringing at the edge
    ///     Vignette           - the ring, darkening toward the rim
    ///     LensDistortion     - the round distortion that says the glass is curved
    ///
    /// Anything this ADDS, it removes on release. The profile is the game's.
    /// </summary>
    public static class OpticStack
    {
        public static string Status = "not resolved yet";
        public static bool Ready { get { return _profile != null; } }

        private const string Ns = "UnityEngine.Rendering.PostProcessing.";

        private static object _profile;
        private static MethodInfo _mAdd, _mRemove, _mHas;

        // effect short name -> the settings instance in the profile
        private static readonly Dictionary<string, object> _fx = new Dictionary<string, object>();
        // the ones we created, so release puts the profile back as we found it
        private static readonly List<Type> _added = new List<Type>();

        private static readonly string[] Wanted =
        {
            "Bloom", "ChromaticAberration", "Vignette", "LensDistortion"
        };

        public static bool Resolve()
        {
            if (_profile != null) return true;
            try
            {
                Assembly asmCSharp = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asmCSharp == null) { Status = "Assembly-CSharp not loaded"; return false; }

                Type camMgr = asmCSharp.GetType("EFT.CameraControl.CameraManager", false);
                PropertyInfo pInst = camMgr == null ? null : camMgr.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object inst = pInst == null ? null : pInst.GetValue(null, null);
                if (inst == null) { Status = "not in a raid yet"; return false; }

                PropertyInfo pOcm = camMgr.GetProperty("OpticCameraManager",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object ocm = pOcm == null ? null : pOcm.GetValue(inst, null);
                if (ocm == null) { Status = "OpticCameraManager is null"; return false; }

                FieldInfo fVol = ocm.GetType().GetField("_postProcessVolume",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object vol = fVol == null ? null : fVol.GetValue(ocm);
                if (vol == null) { Status = "optic PostProcessVolume is null"; return false; }

                PropertyInfo pProfile = vol.GetType().GetProperty("profile",
                    BindingFlags.Instance | BindingFlags.Public);
                object profile = pProfile == null ? null : pProfile.GetValue(vol, null);
                if (profile == null) { Status = "optic volume has no profile"; return false; }

                Type tProfile = profile.GetType();
                Type tSettings = FindType(Ns + "PostProcessEffectSettings");
                if (tSettings == null) { Status = "PostProcessEffectSettings type not found"; return false; }

                _mAdd = tProfile.GetMethod("AddSettings", new[] { typeof(Type) });
                _mRemove = tProfile.GetMethod("RemoveSettings", new[] { typeof(Type) });
                _mHas = tProfile.GetMethod("HasSettings", new[] { typeof(Type) });
                if (_mAdd == null) { Status = "PostProcessProfile.AddSettings(Type) not found"; return false; }

                _fx.Clear();
                _added.Clear();

                var report = new List<string>();
                foreach (string name in Wanted)
                {
                    Type t = FindType(Ns + name);
                    if (t == null) { report.Add(name + ":TYPE MISSING"); continue; }

                    bool had = _mHas != null && (bool)_mHas.Invoke(profile, new object[] { t });
                    object settings = had ? GetExisting(profile, t)
                                          : _mAdd.Invoke(profile, new object[] { t });

                    if (settings == null) { report.Add(name + ":ADD FAILED"); continue; }

                    if (!had) _added.Add(t);
                    _fx[name] = settings;
                    report.Add(name + (had ? ":was present" : ":added"));
                }

                if (_fx.Count == 0) { Status = "no effects could be added"; return false; }

                _profile = profile;
                Status = "driving the optic's own stack";
                Plugin.Log.LogInfo("Optic glass: " + Status + " - " + string.Join(", ", report.ToArray()));
                return true;
            }
            catch (Exception e)
            {
                Status = e.GetType().Name + ": " + e.Message;
                Plugin.Log.LogWarning("Optic glass: " + Status);
                return false;
            }
        }

        private static object GetExisting(object profile, Type effectType)
        {
            FieldInfo fList = profile.GetType().GetField("settings",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var list = fList == null ? null : fList.GetValue(profile) as System.Collections.IEnumerable;
            if (list == null) return null;
            foreach (object s in list)
                if (s != null && effectType.IsInstanceOfType(s)) return s;
            return null;
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = a.GetType(fullName, false); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>
        /// A ParameterOverride only participates when overrideState is true.
        /// Writing .value alone leaves the effect reading the profile default and
        /// doing nothing visible - F34's rule, and the one that would silently
        /// waste a whole round here.
        /// </summary>
        private static void SetParam(object settings, string field, object value)
        {
            if (settings == null) return;
            FieldInfo f = settings.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.Public);
            object param = f == null ? null : f.GetValue(settings);
            if (param == null) return;

            Type tp = param.GetType();
            FieldInfo fv = tp.GetField("value", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo fo = tp.GetField("overrideState", BindingFlags.Instance | BindingFlags.Public);
            if (fv != null) fv.SetValue(param, value);
            if (fo != null) fo.SetValue(param, true);
        }

        private static void Enable(string name, bool on)
        {
            object s;
            if (!_fx.TryGetValue(name, out s) || s == null) return;

            SetParam(s, "enabled", on);
            FieldInfo fActive = s.GetType().GetField("active",
                BindingFlags.Instance | BindingFlags.Public);
            if (fActive != null) fActive.SetValue(s, on);
        }

        public static void Drive(float bloom, float fringe, float vignette, float distortion)
        {
            if (_profile == null) return;
            try
            {
                object s;

                bool bloomOn = bloom > 0.001f;
                Enable("Bloom", bloomOn);
                if (bloomOn && _fx.TryGetValue("Bloom", out s))
                {
                    SetParam(s, "intensity", bloom);
                    SetParam(s, "threshold", 0.9f);
                    SetParam(s, "diffusion", 7f);
                }

                bool fringeOn = fringe > 0.001f;
                Enable("ChromaticAberration", fringeOn);
                if (fringeOn && _fx.TryGetValue("ChromaticAberration", out s))
                    SetParam(s, "intensity", fringe);

                bool vigOn = vignette > 0.001f;
                Enable("Vignette", vigOn);
                if (vigOn && _fx.TryGetValue("Vignette", out s))
                {
                    SetParam(s, "intensity", vignette);
                    SetParam(s, "smoothness", 0.6f);
                    SetParam(s, "roundness", 1f);
                    SetParam(s, "rounded", true);
                }

                bool distOn = Mathf.Abs(distortion) > 0.001f;
                Enable("LensDistortion", distOn);
                if (distOn && _fx.TryGetValue("LensDistortion", out s))
                {
                    // PPv2 LensDistortion intensity is -100..100. The config dial is
                    // -1..1 because nobody thinks in hundreds; scaled here so the
                    // number in the menu stays something a person can picture.
                    SetParam(s, "intensity", distortion * 100f);
                    SetParam(s, "scale", 1f);
                }

                MarkDirty();
            }
            catch (Exception e)
            {
                Status = e.GetType().Name + " while writing - giving up";
                Plugin.Log.LogWarning("Optic glass: " + Status);
                _profile = null;
            }
        }

        private static void MarkDirty()
        {
            FieldInfo f = _profile.GetType().GetField("isDirty",
                BindingFlags.Instance | BindingFlags.Public);
            if (f != null) f.SetValue(_profile, true);
        }

        /// <summary>Put the profile back the way it was found. Never throws.</summary>
        public static void Release()
        {
            if (_profile == null) return;
            try
            {
                foreach (string name in Wanted) Enable(name, false);

                if (_mRemove != null)
                    foreach (Type t in _added) _mRemove.Invoke(_profile, new object[] { t });

                MarkDirty();
            }
            catch { }

            _added.Clear();
            _fx.Clear();
            _profile = null;
            Status = "released";
        }
    }
}
